using System.Windows;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace SshAdbHub;

// System tray icon, "start minimized" and "close hides to tray".
public partial class MainWindow
{
    private WinForms.NotifyIcon? _tray;
    private bool _exiting;
    private bool _trayTipShown;

    private void InitTray()
    {
        Drawing.Icon icon;
        try { icon = Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? Drawing.SystemIcons.Application; }
        catch { icon = Drawing.SystemIcons.Application; }

        var open = new WinForms.ToolStripMenuItem("Open SSH + ADB Hub");
        open.Click += (_, _) => ShowFromTray();

        var connect = new WinForms.ToolStripMenuItem("Connect");
        connect.Click += (_, _) => Connect_Click(this, new RoutedEventArgs());

        var exit = new WinForms.ToolStripMenuItem("Exit");
        exit.Click += (_, _) => { _exiting = true; Close(); };

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add(open);
        menu.Items.Add(connect);
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add(exit);
        menu.Opening += (_, _) => connect.Text = _ssh.IsConnected ? "Disconnect" : "Connect";

        _tray = new WinForms.NotifyIcon
        {
            Icon = icon,
            Visible = true,
            Text = "SSH + ADB Hub",
            ContextMenuStrip = menu
        };
        _tray.DoubleClick += (_, _) => ShowFromTray();
    }

    private void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private void ShowTrayTip()
    {
        if (_tray == null || _trayTipShown) return;
        _tray.ShowBalloonTip(3000, "SSH + ADB Hub", "Running in the tray. Double-click the icon to open it.", WinForms.ToolTipIcon.Info);
        _trayTipShown = true;
    }

    private void UpdateTrayText()
    {
        if (_tray == null) return;
        var t = _ssh.IsConnected ? "SSH + ADB Hub - connected: " + _connectedLabel : "SSH + ADB Hub - disconnected";
        _tray.Text = t.Length > 63 ? t[..63] : t;
    }

    private void StartMinChk_Click(object sender, RoutedEventArgs e)
    {
        Cfg.StartMinimized = StartMinChk.IsChecked == true;
        _store.Save();
        // refresh the Windows startup entry so it carries (or drops) --minimized
        if (StartupChk.IsChecked == true)
        {
            try { SettingsStore.SetStartWithWindows(true, Cfg.StartMinimized); }
            catch (Exception ex) { Log("Could not update startup entry: " + ex.Message); }
        }
    }

    private void CloseTrayChk_Click(object sender, RoutedEventArgs e)
    {
        Cfg.CloseToTray = CloseTrayChk.IsChecked == true;
        _store.Save();
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_exiting && Cfg.CloseToTray && _tray != null)
        {
            e.Cancel = true;
            Hide();
            ShowTrayTip();
            return;
        }
        _store.Save();
        _ssh.Dispose();
        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }
    }
}
