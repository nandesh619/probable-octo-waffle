using System.Text;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace SshAdbHub;

public sealed class SshService : IDisposable
{
    private SshClient? _ssh;
    private SftpClient? _sftp;
    private ShellStream? _shell;

    public bool IsConnected => _ssh?.IsConnected == true;
    public bool SftpAvailable => _sftp?.IsConnected == true;
    public string SftpError { get; private set; } = "";

    // ------------------------------------------------------------ connect
    public async Task ConnectAsync(SshProfile p, Func<string, string, bool> confirmNewKey, Action saveProfiles)
    {
        Disconnect();
        await Task.Run(() =>
        {
            var secret = SettingsStore.Unprotect(p.SecretProtected);
            var ssh = new SshClient(BuildInfo(p, secret)) { KeepAliveInterval = TimeSpan.FromSeconds(30) };
            ssh.HostKeyReceived += (_, e) => e.CanTrust = CheckHostKey(p, e, confirmNewKey, saveProfiles);
            try { ssh.Connect(); }
            catch { ssh.Dispose(); throw; }
            _ssh = ssh;

            SftpError = "";
            try
            {
                var sftp = new SftpClient(BuildInfo(p, secret)) { KeepAliveInterval = TimeSpan.FromSeconds(30) };
                sftp.HostKeyReceived += (_, e) => e.CanTrust = CheckHostKey(p, e, confirmNewKey, saveProfiles);
                sftp.Connect();
                _sftp = sftp;
            }
            catch (Exception ex)
            {
                SftpError = ex.Message;
            }
        });
    }

    public void Disconnect()
    {
        try { _shell?.Dispose(); } catch { }
        _shell = null;
        try { _sftp?.Dispose(); } catch { }
        try { _ssh?.Dispose(); } catch { }
        _sftp = null;
        _ssh = null;
    }

    private static ConnectionInfo BuildInfo(SshProfile p, string secret)
    {
        var methods = new List<AuthenticationMethod>();
        if (!string.IsNullOrWhiteSpace(p.KeyFile))
        {
            var key = string.IsNullOrEmpty(secret) ? new PrivateKeyFile(p.KeyFile) : new PrivateKeyFile(p.KeyFile, secret);
            methods.Add(new PrivateKeyAuthenticationMethod(p.Username, key));
        }
        else
        {
            methods.Add(new PasswordAuthenticationMethod(p.Username, secret));
        }
        return new ConnectionInfo(p.Host, p.Port, p.Username, methods.ToArray()) { Timeout = TimeSpan.FromSeconds(15) };
    }

    private static bool CheckHostKey(SshProfile p, HostKeyEventArgs e, Func<string, string, bool> confirmNewKey, Action save)
    {
        var fp = e.FingerPrintSHA256;
        lock (p)
        {
            if (string.IsNullOrEmpty(p.HostKeyFingerprint))
            {
                if (!confirmNewKey(p.Host, fp)) return false;
                p.HostKeyFingerprint = fp;
                save();
                return true;
            }
            return p.HostKeyFingerprint == fp;
        }
    }

    // ------------------------------------------------------------ SFTP
    private SftpClient Sftp => _sftp is { IsConnected: true } s
        ? s
        : throw new InvalidOperationException("SFTP is not available. On OpenWrt install it with: opkg install openssh-sftp-server");

    public List<RemoteItem> List(string path) =>
        Sftp.ListDirectory(path)
            .Where(f => f.Name != "." && f.Name != "..")
            .OrderByDescending(f => f.IsDirectory)
            .ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .Select(f => new RemoteItem
            {
                Name = f.Name,
                FullName = f.FullName,
                IsDirectory = f.IsDirectory,
                IsLink = f.Attributes.IsSymbolicLink,
                SizeText = f.IsDirectory ? "" : FormatSize(f.Length),
                ModifiedText = f.LastWriteTime.ToString("yyyy-MM-dd HH:mm")
            }).ToList();

    public void Download(string remote, string local)
    {
        using var fs = File.Create(local);
        Sftp.DownloadFile(remote, fs);
    }

    public void Upload(string local, string remote)
    {
        using var fs = File.OpenRead(local);
        Sftp.UploadFile(fs, remote, true);
    }

    public void Delete(string path) => Sftp.Delete(path);
    public void MakeDir(string path) => Sftp.CreateDirectory(path);

    private static string FormatSize(long b)
    {
        string[] u = { "B", "KB", "MB", "GB", "TB" };
        double v = b; int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return i == 0 ? $"{b} B" : $"{v:0.#} {u[i]}";
    }

    // ------------------------------------------------------------ commands
    /// <summary>Runs a command, streaming merged stdout/stderr line by line. Returns the exit status.</summary>
    public int RunStreaming(string command, Action<string> onLine, CancellationToken ct, TimeSpan timeout)
    {
        if (_ssh is not { IsConnected: true }) throw new InvalidOperationException("Not connected.");
        var cmd = _ssh.CreateCommand(command + " 2>&1");
        cmd.CommandTimeout = timeout;
        var ar = cmd.BeginExecute();
        using var reg = ct.Register(() => { try { cmd.CancelAsync(); } catch { } });
        using (var reader = new StreamReader(cmd.OutputStream))
        {
            string? line;
            while ((line = reader.ReadLine()) != null) onLine(line);
        }
        try { cmd.EndExecute(ar); } catch { }
        int? exit = cmd.ExitStatus;
        return exit ?? -1;
    }

    /// <summary>Builds: sh -c '&lt;script&gt;' name arg1 arg2 ... (no upload needed, works even without SFTP).</summary>
    public static string BuildScriptCommand(string script, string name, IEnumerable<string> args) =>
        "sh -c " + ProcRun.ShellQuote(script) + " " + ProcRun.ShellQuote(name) +
        string.Concat(args.Select(a => " " + ProcRun.ShellQuote(a)));

    /// <summary>Runs a command and returns exit code plus all output lines (stdout+stderr merged).</summary>
    public (int code, List<string> lines) RunCapture(string command, TimeSpan timeout)
    {
        var lines = new List<string>();
        var code = RunStreaming(command, l => { lock (lines) lines.Add(l); }, CancellationToken.None, timeout);
        return (code, lines);
    }

    /// <summary>Downloads a (binary) file with "cat" over the SSH channel - works even when SFTP is not installed.</summary>
    public void DownloadViaCat(string remote, string local)
    {
        if (_ssh is not { IsConnected: true }) throw new InvalidOperationException("Not connected.");
        var cmd = _ssh.CreateCommand("cat " + ProcRun.ShellQuote(remote));
        var ar = cmd.BeginExecute();
        using (var fs = File.Create(local)) cmd.OutputStream.CopyTo(fs);
        cmd.EndExecute(ar);
    }

    /// <summary>Recursively uploads a local folder.</summary>
    public void UploadDirectory(string localDir, string remoteDir)
    {
        var s = Sftp;
        if (!s.Exists(remoteDir)) s.CreateDirectory(remoteDir);
        foreach (var f in Directory.GetFiles(localDir))
        {
            using var fs = File.OpenRead(f);
            s.UploadFile(fs, remoteDir.TrimEnd('/') + "/" + Path.GetFileName(f), true);
        }
        foreach (var d in Directory.GetDirectories(localDir))
            UploadDirectory(d, remoteDir.TrimEnd('/') + "/" + Path.GetFileName(d));
    }

    // Persistent interactive shell. State (cwd, variables, aliases) survives commands.
    public bool ShellStarted => _shell != null && _shell.CanRead && _shell.CanWrite;

    public void StartShell()
    {
        if (_ssh is not { IsConnected: true }) throw new InvalidOperationException("Not connected.");
        if (ShellStarted) return;
        _shell?.Dispose();
        _shell = _ssh.CreateShellStream("xterm", 120, 40, 1200, 800, 8192);
        Thread.Sleep(200);
        // The UI poller drains the shell output stream.
    }

    public void WriteShell(string input)
    {
        if (!ShellStarted) throw new InvalidOperationException("The interactive shell is not running.");
        _shell!.Write(input);
        _shell.Flush();
    }

    public string ReadShellOutput()
    {
        if (!ShellStarted || !_shell!.DataAvailable) return "";
        var buffer = new byte[8192];
        var count = _shell.Read(buffer, 0, buffer.Length);
        return count > 0 ? Encoding.UTF8.GetString(buffer, 0, count) : "";
    }

    public void InterruptShell()
    {
        if (ShellStarted) { _shell!.Write("\u0003"); _shell.Flush(); }
    }
    public void Dispose() => Disconnect();
}

