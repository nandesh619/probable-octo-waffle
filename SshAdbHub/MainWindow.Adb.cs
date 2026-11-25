using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace SshAdbHub;

// ADB devices tab: connect/pair, nicknames, install, screenshot, shell, drag-and-drop.
public partial class MainWindow
{
    // ------------------------------------------------------------ saved devices (nicknames / notes)
    private void MigrateSavedDevices()
    {
        if (Cfg.SavedAdbDevices.Count == 0) return;
        foreach (var a in Cfg.SavedAdbDevices)
            if (!Cfg.Devices.Any(x => x.Address == a))
                Cfg.Devices.Add(new SavedDevice { Address = a });
        Cfg.SavedAdbDevices.Clear();
        _store.Save();
    }

    private SavedDevice SaveDevice(string address)
    {
        var s = Cfg.Devices.FirstOrDefault(x => x.Address == address);
        if (s == null)
        {
            s = new SavedDevice { Address = address };
            Cfg.Devices.Add(s);
        }
        return s;
    }

    private void RefreshSavedDevices()
    {
        var cur = SavedDeviceBox.Text;
        SavedDeviceBox.ItemsSource = null;
        SavedDeviceBox.ItemsSource = Cfg.Devices.Where(d => d.Address.Contains(':')).Select(d => d.Address).ToList();
        SavedDeviceBox.Text = cur;
    }

    private async void AdbRename_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceGrid.SelectedItem is not AdbDevice d) { Out(AdbOut, "Click a device row first."); return; }
        var saved = SaveDevice(d.Serial);
        var name = Dialogs.Prompt(this, "Device name", $"Name for {d.Serial} (for example: Bedroom Fire Stick):", saved.Name);
        if (name == null) return;
        var notes = Dialogs.Prompt(this, "Device notes", "Notes (optional):", saved.Notes);
        if (notes == null) return;
        saved.Name = name.Trim();
        saved.Notes = notes.Trim();
        _store.Save();
        await RefreshDevicesAsync(force: true);
    }

    private async void AdbForget_Click(object sender, RoutedEventArgs e)
    {
        if (DeviceGrid.SelectedItem is not AdbDevice d) { Out(AdbOut, "Click a device row first."); return; }
        var saved = Cfg.Devices.FirstOrDefault(x => x.Address == d.Serial);
        if (saved == null) { Out(AdbOut, "That device is not in the saved list."); return; }
        if (MessageBox.Show(this, $"Remove '{d.Label}' from the saved list (name, notes, 'connect all')?", "Forget device",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        Cfg.Devices.Remove(saved);
        _store.Save();
        RefreshSavedDevices();
        await RefreshDevicesAsync(force: true);
    }

    // ------------------------------------------------------------ device list
    private async Task RefreshDevicesAsync(bool force = false)
    {
        if (_adbBusy) return;
        _adbBusy = true;
        try
        {
            var list = await _adb.ListDevicesAsync();
            foreach (var d in list)
            {
                var s = Cfg.Devices.FirstOrDefault(x => x.Address == d.Serial);
                if (s != null) { d.Name = s.Name; d.Notes = s.Notes; }
            }
            foreach (var s in Cfg.Devices)
                if (!list.Any(d => d.Serial == s.Address))
                    list.Add(new AdbDevice { Serial = s.Address, State = "not connected", Name = s.Name, Notes = s.Notes });

            string Sig(IEnumerable<AdbDevice> l) => string.Join("|", l.Select(d => d.Serial + "/" + d.State + "/" + d.Name + "/" + d.Notes));
            if (force || Sig(_devices) != Sig(list))
            {
                var sel = _devices.Where(d => d.Selected).Select(d => d.Serial).ToHashSet();
                _devices.Clear();
                foreach (var d in list) { d.Selected = sel.Contains(d.Serial); _devices.Add(d); }
                UpdateAppDeviceBox();
            }
            AdbStatus.Text = $"{list.Count(d => d.State == "device")} ready, {list.Count} listed";
        }
        catch (Exception ex)
        {
            AdbStatus.Text = "adb error: " + ex.Message;
        }
        finally { _adbBusy = false; }
    }

    private async void AdbRefresh_Click(object sender, RoutedEventArgs e) => await RefreshDevicesAsync(force: true);

    private void AdbBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "Select adb.exe", Filter = "adb.exe|adb.exe|All files|*.*" };
        if (dlg.ShowDialog(this) != true) return;
        Cfg.AdbPath = dlg.FileName;
        _store.Save();
        _adb.Configure(Cfg.AdbPath);
        AdbPathBox.Text = _adb.AdbPath;
    }

    // ------------------------------------------------------------ connect / pair
    private async Task AdbConnectAddressAsync(string addr)
    {
        addr = addr.Trim();
        if (addr.Length == 0) return;
        if (!addr.Contains(':')) addr += ":5555";
        Out(AdbOut, "> adb connect " + addr);
        await _adb.RunAsync(new[] { "connect", addr }, l => Out(AdbOut, l));
        if (!Cfg.Devices.Any(x => x.Address == addr))
        {
            SaveDevice(addr);
            _store.Save();
            RefreshSavedDevices();
        }
    }

    private async void AdbConnect_Click(object sender, RoutedEventArgs e)
    {
        await AdbConnectAddressAsync(SavedDeviceBox.Text);
        await RefreshDevicesAsync(force: true);
    }

    private async void AdbConnectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var d in Cfg.Devices.Where(d => d.Address.Contains(':')).ToList())
            await AdbConnectAddressAsync(d.Address);
        await RefreshDevicesAsync(force: true);
    }

    private async void AdbPair_Click(object sender, RoutedEventArgs e)
    {
        var hp = Dialogs.Prompt(this, "Pair device", "Pairing address shown on the device (IP:port):");
        if (string.IsNullOrWhiteSpace(hp)) return;
        var code = Dialogs.Prompt(this, "Pair device", "Pairing code:");
        if (string.IsNullOrWhiteSpace(code)) return;
        await _adb.RunAsync(new[] { "pair", hp.Trim(), code.Trim() }, l => Out(AdbOut, l));
    }

    private async void AdbDisconnect_Click(object sender, RoutedEventArgs e)
    {
        foreach (var d in SelectedDevices().Where(d => d.Serial.Contains(':')))
            await _adb.RunAsync(new[] { "disconnect", d.Serial }, DeviceLogTag(d));
        await RefreshDevicesAsync(force: true);
    }

    // ------------------------------------------------------------ helpers
    private List<AdbDevice> SelectedDevices()
    {
        var res = _devices.Where(d => d.Selected && d.State == "device").ToList();
        if (res.Count == 0 && DeviceGrid.SelectedItem is AdbDevice one && one.State == "device") res.Add(one);
        return res;
    }

    private async Task ForEachDeviceAsync(Func<AdbDevice, Task> work)
    {
        var devs = SelectedDevices();
        if (devs.Count == 0)
        {
            Out(AdbOut, "No ready device selected. Tick 'Use' on one or more devices whose state is 'device'.");
            return;
        }
        await Task.WhenAll(devs.Select(work));
    }

    private Action<string> DeviceLogTag(AdbDevice d) => l => Out(AdbOut, $"[{d.DisplayName}] {l}");

    private Task<int> InstallApkAsync(AdbDevice d, string apk) =>
        _adb.RunAsync(new[] { "-s", d.Serial, "install", "-r", apk }, DeviceLogTag(d));

    private Task<int> PushFileAsync(AdbDevice d, string file) =>
        _adb.RunAsync(new[] { "-s", d.Serial, "push", file, "/sdcard/Download/" + Path.GetFileName(file) }, DeviceLogTag(d));

    // ------------------------------------------------------------ actions
    private async void AdbInstall_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "Select APK", Filter = "APK files|*.apk|All files|*.*" };
        if (dlg.ShowDialog(this) != true) return;
        var apk = dlg.FileName;
        await ForEachDeviceAsync(d => InstallApkAsync(d, apk));
    }

    private async void AdbScreenshot_Click(object sender, RoutedEventArgs e)
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "SshAdbHub");
        Directory.CreateDirectory(folder);
        await ForEachDeviceAsync(async d =>
        {
            var tag = DeviceLogTag(d);
            var remote = "/sdcard/__hub_shot.png";
            var local = Path.Combine(folder, $"{d.Serial.Replace(':', '_')}_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            await _adb.RunAsync(new[] { "-s", d.Serial, "shell", "screencap", "-p", remote }, tag);
            await _adb.RunAsync(new[] { "-s", d.Serial, "pull", remote, local }, tag);
            await _adb.RunAsync(new[] { "-s", d.Serial, "shell", "rm", "-f", remote }, _ => { });
            tag("saved " + local);
        });
    }

    private async void AdbReboot_Click(object sender, RoutedEventArgs e)
    {
        var devs = SelectedDevices();
        if (devs.Count == 0) { Out(AdbOut, "No ready device selected."); return; }
        if (MessageBox.Show(this, $"Reboot {devs.Count} device(s)?\n\n" + string.Join("\n", devs.Select(d => d.Label)),
                "Reboot", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        await ForEachDeviceAsync(d => _adb.RunAsync(new[] { "-s", d.Serial, "reboot" }, DeviceLogTag(d)));
    }

    private async void AdbShell_Click(object sender, RoutedEventArgs e)
    {
        var cmd = AdbShellInput.Text.Trim();
        if (cmd.Length == 0) return;
        Out(AdbOut, "> adb shell " + cmd);
        await ForEachDeviceAsync(d => _adb.RunAsync(new[] { "-s", d.Serial, "shell", cmd }, DeviceLogTag(d)));
    }

    private void AdbShellInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) AdbShell_Click(sender, e);
    }

    // ------------------------------------------------------------ drag and drop
    private static T? FindParent<T>(DependencyObject? d) where T : DependencyObject
    {
        while (d != null)
        {
            if (d is T t) return t;
            d = d is System.Windows.Media.Visual || d is System.Windows.Media.Media3D.Visual3D
                ? System.Windows.Media.VisualTreeHelper.GetParent(d)
                : LogicalTreeHelper.GetParent(d);
        }
        return null;
    }

    private static string[] DroppedPaths(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop) ? (string[])e.Data.GetData(DataFormats.FileDrop)! : Array.Empty<string>();

    private void FileDrag_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>Drop .apk files on a device row to install; other files are pushed to /sdcard/Download.</summary>
    private async void DeviceGrid_Drop(object sender, DragEventArgs e)
    {
        var paths = DroppedPaths(e);
        if (paths.Length == 0) return;

        var row = FindParent<DataGridRow>(e.OriginalSource as DependencyObject);
        List<AdbDevice> targets = row?.Item is AdbDevice one ? new List<AdbDevice> { one } : SelectedDevices();
        targets = targets.Where(t => t.State == "device").ToList();
        if (targets.Count == 0)
        {
            Out(AdbOut, "Drop onto a device row whose state is 'device' (or tick devices first).");
            return;
        }

        var apks = paths.Where(p => p.EndsWith(".apk", StringComparison.OrdinalIgnoreCase) && File.Exists(p)).ToList();
        var others = paths.Where(p => File.Exists(p) && !apks.Contains(p)).ToList();
        if (apks.Count == 0 && others.Count == 0) { Out(AdbOut, "Only files can be dropped (not folders)."); return; }

        await Task.WhenAll(targets.Select(async d =>
        {
            foreach (var apk in apks) await InstallApkAsync(d, apk);
            foreach (var f in others) await PushFileAsync(d, f);
        }));
    }

    /// <summary>Drop files or folders on the file list to upload them to the current router folder.</summary>
    private async void FileListView_Drop(object sender, DragEventArgs e)
    {
        var paths = DroppedPaths(e);
        if (paths.Length == 0 || !NeedSftp()) return;
        foreach (var p in paths)
        {
            var name = Path.GetFileName(p.TrimEnd('\\', '/'));
            var dest = Join(_remotePath, name);
            try
            {
                SetStatus($"Uploading {name}...");
                if (Directory.Exists(p)) await Task.Run(() => _ssh.UploadDirectory(p, dest));
                else if (File.Exists(p)) await Task.Run(() => _ssh.Upload(p, dest));
                Log($"Uploaded {p} -> {dest}");
            }
            catch (Exception ex) { Log($"Upload of {name} failed: {ex.Message}"); }
        }
        UpdateConnectionUi();
        await LoadRemoteAsync(_remotePath);
    }

    /// <summary>Drop script files on the script list to copy them into the scripts folder.</summary>
    private void ScriptList_Drop(object sender, DragEventArgs e)
    {
        var exts = new[] { ".sh", ".ps1", ".bat", ".cmd" };
        var n = 0;
        try { Directory.CreateDirectory(ScriptsDir); } catch { }
        foreach (var p in DroppedPaths(e))
        {
            if (!File.Exists(p) || !exts.Contains(Path.GetExtension(p).ToLowerInvariant())) continue;
            try { File.Copy(p, Path.Combine(ScriptsDir, Path.GetFileName(p)), true); n++; }
            catch (Exception ex) { Log($"Could not copy {p}: {ex.Message}"); }
        }
        Log($"{n} script(s) added to {ScriptsDir}");
        RefreshScripts();
    }
}
