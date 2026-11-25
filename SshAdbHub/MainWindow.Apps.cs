using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace SshAdbHub;

// "Apps (ADB)" tab: list packages, uninstall / disable / restore, force stop, clear data, export APK.
public partial class MainWindow
{
    private readonly List<AppPackage> _allApps = new();
    private readonly ObservableCollection<AppPackage> _apps = new();
    private bool _appsBusy;

    private static readonly Regex PkgNameRx = new(@"^[A-Za-z0-9._]+$");

    private AdbDevice? AppDevice => AppDeviceBox.SelectedItem as AdbDevice;

    private void UpdateAppDeviceBox()
    {
        var prev = (AppDeviceBox.SelectedItem as AdbDevice)?.Serial;
        var ready = _devices.Where(d => d.State == "device").ToList();
        AppDeviceBox.ItemsSource = ready;
        AppDeviceBox.SelectedItem = ready.FirstOrDefault(d => d.Serial == prev) ?? ready.FirstOrDefault();
    }

    private async void AppsLoad_Click(object sender, RoutedEventArgs e) => await LoadAppsAsync();

    private async Task LoadAppsAsync()
    {
        var d = AppDevice;
        if (d == null) { Out(AppOut, "Pick a ready device first (a device with state 'device')."); return; }
        if (_appsBusy) return;
        _appsBusy = true;
        AppStatus.Text = "Loading...";
        try
        {
            async Task<HashSet<string>> Pm(string flags)
            {
                var set = new HashSet<string>();
                await _adb.RunAsync(new[] { "-s", d.Serial, "shell", "pm list packages " + flags }, l =>
                {
                    var s = l.Trim();
                    if (s.StartsWith("package:")) lock (set) set.Add(s.Substring(8));
                });
                return set;
            }

            var all = await Pm("-u");      // includes packages removed for the user
            var installed = await Pm("");
            var disabled = await Pm("-d");
            var third = await Pm("-3");    // user-installed apps

            if (all.Count == 0 && installed.Count == 0)
            {
                AppStatus.Text = "No packages returned (is the device authorised?)";
                return;
            }
            foreach (var p in installed) all.Add(p);

            _allApps.Clear();
            foreach (var pkg in all.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                _allApps.Add(new AppPackage
                {
                    Package = pkg,
                    Kind = third.Contains(pkg) ? "user" : "system",
                    State = !installed.Contains(pkg) ? "removed" : disabled.Contains(pkg) ? "disabled" : "enabled"
                });
            }
            ApplyAppFilter();
        }
        catch (Exception ex) { AppStatus.Text = "Error: " + ex.Message; }
        finally { _appsBusy = false; }
    }

    private void ApplyAppFilter()
    {
        var q = AppFilterBox.Text.Trim();
        var kind = AppKindBox.SelectedIndex; // 0 all, 1 user, 2 system, 3 disabled, 4 removed
        var shown = _allApps.Where(a =>
            (q.Length == 0 || a.Package.Contains(q, StringComparison.OrdinalIgnoreCase)) &&
            kind switch
            {
                1 => a.Kind == "user",
                2 => a.Kind == "system",
                3 => a.State == "disabled",
                4 => a.State == "removed",
                _ => true
            }).ToList();
        _apps.Clear();
        foreach (var a in shown) _apps.Add(a);
        AppStatus.Text = $"{_apps.Count} shown / {_allApps.Count} total";
    }

    private void AppFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        ApplyAppFilter();
    }

    private void AppTickAll_Click(object sender, RoutedEventArgs e) { foreach (var a in _apps) a.Selected = true; }
    private void AppUntickAll_Click(object sender, RoutedEventArgs e) { foreach (var a in _apps) a.Selected = false; }

    private List<AppPackage> TickedApps() => _apps.Where(a => a.Selected).ToList();

    private async Task RunPmAsync(string title, string confirmText, Func<string, string> commandFor)
    {
        var d = AppDevice;
        var items = TickedApps();
        if (d == null || items.Count == 0) { Out(AppOut, "Pick a device and tick at least one app."); return; }

        if (confirmText.Length > 0)
        {
            var list = string.Join("\n", items.Take(10).Select(i => i.Package)) + (items.Count > 10 ? $"\n... and {items.Count - 10} more" : "");
            if (MessageBox.Show(this, $"{confirmText}\n\nDevice: {d.Label}\nApps ({items.Count}):\n{list}", title,
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        }

        foreach (var a in items)
        {
            var pkg = a.Package;
            if (!PkgNameRx.IsMatch(pkg)) { Out(AppOut, $"{pkg}: skipped (unusual package name)"); continue; }
            await _adb.RunAsync(new[] { "-s", d.Serial, "shell", commandFor(pkg) }, l => Out(AppOut, $"{pkg}: {l}"));
        }
        await LoadAppsAsync();
    }

    private async void AppUninstall_Click(object sender, RoutedEventArgs e) =>
        await RunPmAsync("Uninstall",
            "Uninstall these apps for the main user?\nSystem apps are only removed for user 0 and can be brought back with 'Restore'. " +
            "Removing essential system apps can break the device.",
            p => $"pm uninstall --user 0 {p}");

    private async void AppDisable_Click(object sender, RoutedEventArgs e) =>
        await RunPmAsync("Disable", "Disable these apps?", p => $"pm disable-user --user 0 {p}");

    private async void AppEnable_Click(object sender, RoutedEventArgs e) =>
        await RunPmAsync("Enable", "", p => $"pm enable --user 0 {p}");

    private async void AppRestore_Click(object sender, RoutedEventArgs e) =>
        await RunPmAsync("Restore", "", p => $"cmd package install-existing {p} || pm install-existing {p}");

    private async void AppForceStop_Click(object sender, RoutedEventArgs e) =>
        await RunPmAsync("Force stop", "", p => $"am force-stop {p}");

    private async void AppClearData_Click(object sender, RoutedEventArgs e) =>
        await RunPmAsync("Clear data", "Clear ALL data of these apps (logins, settings, downloads inside the app)?", p => $"pm clear {p}");

    private async void AppExport_Click(object sender, RoutedEventArgs e)
    {
        var d = AppDevice;
        var items = TickedApps();
        if (d == null || items.Count == 0) { Out(AppOut, "Pick a device and tick at least one app."); return; }
        var dlg = new OpenFolderDialog { Title = "Export APKs to..." };
        if (dlg.ShowDialog(this) != true) return;

        foreach (var a in items)
        {
            var pkg = a.Package;
            if (!PkgNameRx.IsMatch(pkg)) continue;
            var paths = new List<string>();
            await _adb.RunAsync(new[] { "-s", d.Serial, "shell", "pm path " + pkg }, l =>
            {
                var s = l.Trim();
                if (s.StartsWith("package:")) lock (paths) paths.Add(s.Substring(8));
            });
            if (paths.Count == 0) { Out(AppOut, $"{pkg}: no APK path (removed app?)"); continue; }

            if (paths.Count == 1)
            {
                var local = Path.Combine(dlg.FolderName, pkg + ".apk");
                await _adb.RunAsync(new[] { "-s", d.Serial, "pull", paths[0], local }, l => Out(AppOut, $"{pkg}: {l}"));
            }
            else
            {
                var dir = Path.Combine(dlg.FolderName, pkg);
                Directory.CreateDirectory(dir);
                foreach (var rp in paths)
                    await _adb.RunAsync(new[] { "-s", d.Serial, "pull", rp, Path.Combine(dir, Path.GetFileName(rp)) }, l => Out(AppOut, $"{pkg}: {l}"));
            }
        }
        Out(AppOut, "Export finished -> " + dlg.FolderName);
    }
}
