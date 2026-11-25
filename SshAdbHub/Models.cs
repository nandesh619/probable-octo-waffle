using System.ComponentModel;
using System.Text.Json.Serialization;

namespace SshAdbHub;

public class SshProfile
{
    public string Name { get; set; } = "New profile";
    public string Host { get; set; } = "192.168.1.1";
    public int Port { get; set; } = 22;
    public string Username { get; set; } = "root";
    /// <summary>Optional path to an OpenSSH-format private key.</summary>
    public string KeyFile { get; set; } = "";
    /// <summary>Password (or key passphrase), encrypted with Windows DPAPI (current user only).</summary>
    public string SecretProtected { get; set; } = "";
    public bool AutoConnect { get; set; }
    /// <summary>SHA256 host key fingerprint, pinned on first connect (trust on first use).</summary>
    public string HostKeyFingerprint { get; set; } = "";
    /// <summary>When the last config backup (sysupgrade -b) was taken from this router.</summary>
    public DateTime? LastBackup { get; set; }
    public string LastBackupFile { get; set; } = "";

    public SshProfile Clone() => (SshProfile)MemberwiseClone();
    public override string ToString() => $"{Name}  ({Username}@{Host}:{Port})";
}

public class SavedDevice
{
    /// <summary>adb serial, e.g. 192.168.1.57:5555 or a USB serial.</summary>
    public string Address { get; set; } = "";
    public string Name { get; set; } = "";
    public string Notes { get; set; } = "";
}

public class AppSettings
{
    public List<SshProfile> Profiles { get; set; } = new();
    public string AdbPath { get; set; } = "";
    public string ScriptsFolder { get; set; } = "";
    /// <summary>Legacy (v0.1) list of addresses; migrated into Devices on start.</summary>
    public List<string> SavedAdbDevices { get; set; } = new();
    public List<SavedDevice> Devices { get; set; } = new();
    public bool AutoReconnect { get; set; } = true;
    public bool StartMinimized { get; set; }
    public bool CloseToTray { get; set; } = true;
    public string BackupFolder { get; set; } = "";
    public string WanInterface { get; set; } = "wan";
    /// <summary>Last used values for {{placeholders}}, per script file name.</summary>
    public Dictionary<string, Dictionary<string, string>> ScriptParams { get; set; } = new();
}

public class AdbDevice
{
    public bool Selected { get; set; }
    public string Serial { get; set; } = "";
    public string State { get; set; } = "";
    public string Description { get; set; } = "";
    public string Name { get; set; } = "";
    public string Notes { get; set; } = "";
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Serial : Name;
    public string Label => string.IsNullOrWhiteSpace(Name) ? Serial : $"{Name}  ({Serial})";
}

public class AppPackage : INotifyPropertyChanged
{
    private bool _selected;
    public bool Selected
    {
        get => _selected;
        set { _selected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected))); }
    }
    public string Package { get; set; } = "";
    /// <summary>"user" or "system"</summary>
    public string Kind { get; set; } = "";
    /// <summary>"enabled", "disabled" or "removed"</summary>
    public string State { get; set; } = "";
    public event PropertyChangedEventHandler? PropertyChanged;
}

public class WifiClient
{
    public string Hostname { get; set; } = "";
    public string Ip { get; set; } = "";
    public string Mac { get; set; } = "";
    public string Radio { get; set; } = "";
    public int Signal { get; set; }
    public string SignalText { get; set; } = "";
    public string RxRate { get; set; } = "";
    public string TxRate { get; set; } = "";
}

public class RunRecord
{
    public DateTime Time { get; set; }
    public string Script { get; set; } = "";
    public string Target { get; set; } = "";
    public string Args { get; set; } = "";
    public string ExitText { get; set; } = "";
    public double DurationSec { get; set; }
    public string Output { get; set; } = "";

    [JsonIgnore] public string TimeText => Time.ToString("yyyy-MM-dd HH:mm:ss");
    [JsonIgnore] public string DurationText => $"{DurationSec:0.0}s";
}

public class RemoteItem
{
    public string Name { get; set; } = "";
    public string FullName { get; set; } = "";
    public bool IsDirectory { get; set; }
    public bool IsLink { get; set; }
    public string SizeText { get; set; } = "";
    public string ModifiedText { get; set; } = "";
    public string Display => (IsDirectory ? "📁 " : IsLink ? "🔗 " : "📄 ") + Name;
}
