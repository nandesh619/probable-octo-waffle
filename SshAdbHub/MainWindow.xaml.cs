using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;

namespace SshAdbHub;

public partial class MainWindow : Window
{
    private readonly SettingsStore _store = new();
    private readonly SshService _ssh = new();
    private readonly AdbService _adb = new();
    private readonly ObservableCollection<RemoteItem> _files = new();
    private readonly ObservableCollection<AdbDevice> _devices = new();
    private readonly DispatcherTimer _reconnectTimer = new() { Interval = TimeSpan.FromSeconds(10) };
    private readonly DispatcherTimer _adbTimer = new() { Interval = TimeSpan.FromSeconds(5) };

    private CancellationTokenSource? _scriptCts;
    private readonly DispatcherTimer _shellPollTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private string _remotePath = "/";
    private string _connectedLabel = "";
    private bool _connecting, _wantConnected, _wasConnected, _adbBusy, _loading;
    private SshProfile? _connectedProfile;


    public MainWindow()
    {
        InitializeComponent();
        _shellPollTimer.Tick += ShellPollTimer_Tick;
    }

    private AppSettings Cfg => _store.Settings;

    // =====================================================================  startup / shutdown
    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _store.Load();
        MigrateSavedDevices();
        FileListView.ItemsSource = _files;
        DeviceGrid.ItemsSource = _devices;
        AppGrid.ItemsSource = _apps;
        InitDashboard();
        InitHistory();

        AdbPathBox.Text = Cfg.AdbPath;
        _adb.Configure(Cfg.AdbPath);
        if (string.IsNullOrWhiteSpace(AdbPathBox.Text)) AdbPathBox.Text = _adb.AdbPath;

        AutoReconnectChk.IsChecked = Cfg.AutoReconnect;
        StartupChk.IsChecked = SettingsStore.GetStartWithWindows();
        StartMinChk.IsChecked = Cfg.StartMinimized;
        CloseTrayChk.IsChecked = Cfg.CloseToTray;

        RefreshProfiles(Cfg.Profiles.FirstOrDefault(p => p.AutoConnect));
        RefreshSavedDevices();
        RefreshScripts();

        InitTray();
        Application.Current.SessionEnding += (_, _) => _exiting = true;
        if (Environment.GetCommandLineArgs().Contains("--minimized"))
        {
            Hide();
            ShowTrayTip();
        }

        _reconnectTimer.Tick += ReconnectTimer_Tick;
        _reconnectTimer.Start();
        _adbTimer.Tick += async (_, _) =>
        {
            if (AdbAutoChk.IsChecked == true && (ReferenceEquals(MainTabs.SelectedItem, AdbTab) || ReferenceEquals(MainTabs.SelectedItem, AppsTab)))
                await RefreshDevicesAsync();
        };
        _adbTimer.Start();

        Log("App started. adb: " + _adb.AdbPath);
        await RefreshDevicesAsync();

        var auto = Cfg.Profiles.FirstOrDefault(p => p.AutoConnect);
        if (auto != null)
        {
            ProfileBox.SelectedItem = auto;
            await ConnectAsync(auto: true);
        }
    }

    // =====================================================================  helpers
    private void Out(TextBox tb, string line) =>
        Dispatcher.BeginInvoke(new Action(() =>
        {
            tb.AppendText(line + Environment.NewLine);
            tb.ScrollToEnd();
        }));

    private void Log(string msg) => Out(LogBox, $"{DateTime.Now:HH:mm:ss}  {msg}");

    private void SetStatus(string s) => Dispatcher.BeginInvoke(new Action(() => StatusText.Text = s));

    private void UpdateConnectionUi()
    {
        var c = _ssh.IsConnected;
        ConnectBtn.Content = c ? "Disconnect" : "Connect";
        StatusText.Text = c
            ? $"Connected to {_connectedLabel}" + (_ssh.SftpAvailable ? "" : "   (SFTP unavailable - see Log)")
            : "Disconnected";
        UpdateTrayText();
    }

    private static string Join(string dir, string name) => dir.TrimEnd('/') + "/" + name;

    // =====================================================================  profiles
    private void RefreshProfiles(SshProfile? select = null)
    {
        _loading = true;
        ProfileBox.ItemsSource = null;
        ProfileBox.ItemsSource = Cfg.Profiles.ToList();
        ProfileBox.SelectedItem = select ?? (ProfileBox.Items.Count > 0 ? ProfileBox.Items[0] : null);
        _loading = false;
        UpdateProfileUi();
    }

    private void UpdateProfileUi()
    {
        AutoConnectChk.IsChecked = (ProfileBox.SelectedItem as SshProfile)?.AutoConnect == true;
        UpdateBackupLabel();
    }

    private void ProfileBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading) UpdateProfileUi();
    }

    private void AddProfile_Click(object sender, RoutedEventArgs e)
    {
        var p = new SshProfile();
        if (new ProfileDialog(p) { Owner = this }.ShowDialog() != true) return;
        Cfg.Profiles.Add(p);
        _store.Save();
        RefreshProfiles(p);
    }

    private void EditProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileBox.SelectedItem is not SshProfile old) return;
        var work = old.Clone();
        if (new ProfileDialog(work) { Owner = this }.ShowDialog() != true) return;
        var idx = Cfg.Profiles.IndexOf(old);
        Cfg.Profiles[idx] = work;
        if (work.AutoConnect) foreach (var q in Cfg.Profiles.Where(q => !ReferenceEquals(q, work))) q.AutoConnect = false;
        _store.Save();
        RefreshProfiles(work);
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileBox.SelectedItem is not SshProfile p) return;
        if (MessageBox.Show(this, $"Delete profile '{p.Name}'?", "Delete", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        Cfg.Profiles.Remove(p);
        _store.Save();
        RefreshProfiles();
    }

    private void AutoConnectChk_Click(object sender, RoutedEventArgs e)
    {
        if (_loading || ProfileBox.SelectedItem is not SshProfile p) return;
        var on = AutoConnectChk.IsChecked == true;
        foreach (var q in Cfg.Profiles) q.AutoConnect = false;
        p.AutoConnect = on;
        _store.Save();
    }

    private void AutoReconnectChk_Click(object sender, RoutedEventArgs e)
    {
        Cfg.AutoReconnect = AutoReconnectChk.IsChecked == true;
        _store.Save();
    }

    private void StartupChk_Click(object sender, RoutedEventArgs e)
    {
        try { SettingsStore.SetStartWithWindows(StartupChk.IsChecked == true, Cfg.StartMinimized); }
        catch (Exception ex) { Log("Could not change startup setting: " + ex.Message); }
    }

    // =====================================================================  connect
    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (_ssh.IsConnected)
        {
            _wantConnected = false;
            _ssh.Disconnect();
            _files.Clear();
            Log("Disconnected.");
            UpdateConnectionUi();
            return;
        }
        await ConnectAsync(auto: false);
    }

    private bool ConfirmHostKey(string host, string fingerprint) =>
        Dispatcher.Invoke(() =>
            MessageBox.Show(this,
                $"First connection to {host}.\n\nHost key fingerprint (SHA256):\n{fingerprint}\n\nTrust this host and remember its key?",
                "Verify host key", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes);

    private async Task ConnectAsync(bool auto)
    {
        if (_connecting) return;
        if (ProfileBox.SelectedItem is not SshProfile p)
        {
            SetStatus("No SSH profile selected - click Add.");
            return;
        }
        _connecting = true;
        _wantConnected = true;
        ConnectBtn.IsEnabled = false;
        SetStatus($"Connecting to {p.Host}...");
        try
        {
            await _ssh.ConnectAsync(p, ConfirmHostKey, () => _store.Save());
            _connectedLabel = $"{p.Username}@{p.Host}:{p.Port}";
            _connectedProfile = p;
            Log($"Connected to {_connectedLabel}");
            if (ReferenceEquals(MainTabs.SelectedItem, DashTab)) _ = RefreshDashboardAsync();
            if (!_ssh.SftpAvailable)
                Log("SFTP unavailable: " + _ssh.SftpError + "  (OpenWrt: opkg install openssh-sftp-server)");
            else
                await LoadRemoteAsync("/");
        }
        catch (Exception ex)
        {
            Log("Connect failed: " + ex.Message);
            if (!auto) _wantConnected = false;
        }
        finally
        {
            _connecting = false;
            ConnectBtn.IsEnabled = true;
            UpdateConnectionUi();
        }
    }

    private async void ReconnectTimer_Tick(object? sender, EventArgs e)
    {
        var now = _ssh.IsConnected;
        if (_wasConnected && !now) Log("Connection lost.");
        _wasConnected = now;
        UpdateConnectionUi();

        if (!now && _wantConnected && !_connecting && Cfg.AutoReconnect)
        {
            Log("Reconnecting...");
            await ConnectAsync(auto: true);
        }
    }

    // =====================================================================  files
    private bool NeedSftp()
    {
        if (_ssh.SftpAvailable) return true;
        Log("SFTP is not available (not connected, or the router has no SFTP server).");
        return false;
    }

    private async Task LoadRemoteAsync(string path)
    {
        try
        {
            var items = await Task.Run(() => _ssh.List(path));
            _files.Clear();
            foreach (var i in items) _files.Add(i);
            _remotePath = path;
            PathBox.Text = path;
        }
        catch (Exception ex)
        {
            Log($"Cannot open '{path}': {ex.Message}");
        }
    }

    private async void PathBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && NeedSftp()) await LoadRemoteAsync(PathBox.Text.Trim());
    }

    private async void Up_Click(object sender, RoutedEventArgs e)
    {
        if (!NeedSftp()) return;
        var t = _remotePath.TrimEnd('/');
        var i = t.LastIndexOf('/');
        await LoadRemoteAsync(i <= 0 ? "/" : t[..i]);
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (NeedSftp()) await LoadRemoteAsync(_remotePath);
    }

    private async void FileListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FileListView.SelectedItem is RemoteItem it && (it.IsDirectory || it.IsLink) && NeedSftp())
            await LoadRemoteAsync(it.FullName);
    }

    private async void Upload_Click(object sender, RoutedEventArgs e)
    {
        if (!NeedSftp()) return;
        var dlg = new OpenFileDialog { Multiselect = true, Title = "Upload to " + _remotePath };
        if (dlg.ShowDialog(this) != true) return;
        foreach (var f in dlg.FileNames)
        {
            var name = Path.GetFileName(f);
            try
            {
                SetStatus($"Uploading {name}...");
                await Task.Run(() => _ssh.Upload(f, Join(_remotePath, name)));
                Log($"Uploaded {f} -> {Join(_remotePath, name)}");
            }
            catch (Exception ex) { Log($"Upload of {name} failed: {ex.Message}"); }
        }
        UpdateConnectionUi();
        await LoadRemoteAsync(_remotePath);
    }

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        if (!NeedSftp()) return;
        var items = FileListView.SelectedItems.Cast<RemoteItem>().Where(i => !i.IsDirectory).ToList();
        if (items.Count == 0) { SetStatus("Select one or more files first."); return; }
        var dlg = new OpenFolderDialog { Title = "Download to..." };
        if (dlg.ShowDialog(this) != true) return;
        foreach (var it in items)
        {
            var dest = Path.Combine(dlg.FolderName, it.Name);
            try
            {
                SetStatus($"Downloading {it.Name}...");
                await Task.Run(() => _ssh.Download(it.FullName, dest));
                Log($"Downloaded {it.FullName} -> {dest}");
            }
            catch (Exception ex) { Log($"Download of {it.Name} failed: {ex.Message}"); }
        }
        UpdateConnectionUi();
    }

    private async void NewFolder_Click(object sender, RoutedEventArgs e)
    {
        if (!NeedSftp()) return;
        var name = Dialogs.Prompt(this, "New folder", "Folder name:");
        if (string.IsNullOrWhiteSpace(name)) return;
        try { await Task.Run(() => _ssh.MakeDir(Join(_remotePath, name.Trim()))); }
        catch (Exception ex) { Log("Create folder failed: " + ex.Message); }
        await LoadRemoteAsync(_remotePath);
    }

    private async void DeleteFile_Click(object sender, RoutedEventArgs e)
    {
        if (!NeedSftp()) return;
        var items = FileListView.SelectedItems.Cast<RemoteItem>().ToList();
        if (items.Count == 0) return;
        var list = string.Join("\n", items.Take(8).Select(i => i.Name)) + (items.Count > 8 ? "\n..." : "");
        if (MessageBox.Show(this, $"Delete {items.Count} item(s) from the device?\n\n{list}\n\n(Folders must be empty.)",
                "Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        foreach (var it in items)
        {
            try { await Task.Run(() => _ssh.Delete(it.FullName)); Log("Deleted " + it.FullName); }
            catch (Exception ex) { Log($"Delete of {it.Name} failed: {ex.Message}"); }
        }
        await LoadRemoteAsync(_remotePath);
    }

    // =====================================================================  console
    private void ConsoleRun_Click(object sender, RoutedEventArgs e)
    {
        if (!_ssh.IsConnected) { Out(ConsoleOut, "Not connected."); return; }
        var input = ConsoleInput.Text;
        if (string.IsNullOrWhiteSpace(input)) return;
        try
        {
            if (!_ssh.ShellStarted)
            {
                _ssh.StartShell();
                Out(ConsoleOut, "[Interactive SSH shell started]");
            }
            _ssh.WriteShell(input.Replace("\r\n", "\n").Replace("\r", "\n") + "\n");
            ConsoleInput.Clear();
            ConsoleInput.Focus();
            _shellPollTimer.Start();
        }
        catch (Exception ex) { Out(ConsoleOut, "Shell error: " + ex.Message); }
    }

    private void ShellPollTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            var output = _ssh.ReadShellOutput();
            if (!string.IsNullOrEmpty(output)) Out(ConsoleOut, output);
            if (!_ssh.IsConnected) _shellPollTimer.Stop();
        }
        catch (Exception ex)
        {
            _shellPollTimer.Stop();
            Out(ConsoleOut, "Shell read error: " + ex.Message);
        }
    }

    private void ConsoleStop_Click(object sender, RoutedEventArgs e)
    {
        try { _ssh.InterruptShell(); Out(ConsoleOut, "^C"); }
        catch (Exception ex) { Out(ConsoleOut, "Stop failed: " + ex.Message); }
    }

    private void ConsoleClear_Click(object sender, RoutedEventArgs e)
    {
        ConsoleOut.Clear();
        ConsoleOut.ScrollToHome();
    }

    private void ConsoleInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            e.Handled = true;
            ConsoleRun_Click(sender, e);
        }
    }
}



