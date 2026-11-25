using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace SshAdbHub;

public class SettingsStore
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SshAdbHub");
    private static readonly string FilePath = Path.Combine(Dir, "settings.json");
    private static readonly string HistoryPath = Path.Combine(Dir, "history.json");

    public AppSettings Settings { get; private set; } = new();

    public void Load()
    {
        try
        {
            if (File.Exists(FilePath))
                Settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch
        {
            Settings = new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* ignore disk errors */ }
    }

    // ---- run history ----
    public static List<RunRecord> LoadHistory()
    {
        try
        {
            if (File.Exists(HistoryPath))
                return JsonSerializer.Deserialize<List<RunRecord>>(File.ReadAllText(HistoryPath)) ?? new List<RunRecord>();
        }
        catch { }
        return new List<RunRecord>();
    }

    public static void SaveHistory(List<RunRecord> history)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(HistoryPath, JsonSerializer.Serialize(history));
        }
        catch { }
    }

    // ---- backups folder ----
    public static string BackupDir(AppSettings s) =>
        string.IsNullOrWhiteSpace(s.BackupFolder)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SshAdbHub", "backups")
            : s.BackupFolder;

    // ---- secrets (Windows DPAPI, current user) ----
    public static string Protect(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return "";
        var data = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(data);
    }

    public static string Unprotect(string b64)
    {
        if (string.IsNullOrEmpty(b64)) return "";
        try
        {
            var data = ProtectedData.Unprotect(Convert.FromBase64String(b64), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(data);
        }
        catch { return ""; }
    }

    // ---- start with Windows ----
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "SshAdbHub";

    public static bool GetStartWithWindows()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue(AppName) != null;
    }

    public static void SetStartWithWindows(bool on, bool minimized)
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey, true);
        if (k == null) return;
        if (on) k.SetValue(AppName, "\"" + Environment.ProcessPath + "\"" + (minimized ? " --minimized" : ""));
        else k.DeleteValue(AppName, false);
    }
}
