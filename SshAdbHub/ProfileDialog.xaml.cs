using System.Windows;
using Microsoft.Win32;

namespace SshAdbHub;

public partial class ProfileDialog : Window
{
    private readonly SshProfile _p;

    public ProfileDialog(SshProfile profile)
    {
        InitializeComponent();
        _p = profile;
        NameBox.Text = _p.Name;
        HostBox.Text = _p.Host;
        PortBox.Text = _p.Port.ToString();
        UserBox.Text = _p.Username;
        KeyBox.Text = _p.KeyFile;
        AutoChk.IsChecked = _p.AutoConnect;
        UpdateHint();
    }

    private void UpdateHint() =>
        SecretHint.Text = string.IsNullOrEmpty(_p.SecretProtected) ? "No secret saved." : "A secret is saved (encrypted for your Windows account).";

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "Select private key", Filter = "All files|*.*" };
        if (dlg.ShowDialog(this) == true) KeyBox.Text = dlg.FileName;
    }

    private void ClearSecret_Click(object sender, RoutedEventArgs e)
    {
        _p.SecretProtected = "";
        PassBox.Clear();
        UpdateHint();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(HostBox.Text) || string.IsNullOrWhiteSpace(UserBox.Text))
        {
            MessageBox.Show(this, "Host and username are required.", "SSH profile");
            return;
        }
        if (!int.TryParse(PortBox.Text, out var port) || port is < 1 or > 65535)
        {
            MessageBox.Show(this, "Port must be a number between 1 and 65535.", "SSH profile");
            return;
        }
        // Changing host/port invalidates the pinned host key.
        if (!string.Equals(_p.Host, HostBox.Text.Trim(), StringComparison.OrdinalIgnoreCase) || _p.Port != port)
            _p.HostKeyFingerprint = "";

        _p.Name = string.IsNullOrWhiteSpace(NameBox.Text) ? HostBox.Text.Trim() : NameBox.Text.Trim();
        _p.Host = HostBox.Text.Trim();
        _p.Port = port;
        _p.Username = UserBox.Text.Trim();
        _p.KeyFile = KeyBox.Text.Trim();
        _p.AutoConnect = AutoChk.IsChecked == true;
        if (!string.IsNullOrEmpty(PassBox.Password))
            _p.SecretProtected = SettingsStore.Protect(PassBox.Password);
        DialogResult = true;
    }
}
