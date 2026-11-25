using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace SshAdbHub;

public static class ProcRun
{
    /// <summary>Runs a process, streaming stdout+stderr line by line. Returns the exit code (-1 if it could not start).</summary>
    public static async Task<int> RunAsync(string file, IEnumerable<string> args, Action<string> onLine,
        CancellationToken ct = default, IDictionary<string, string>? env = null)
    {
        var psi = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        if (env != null) foreach (var kv in env) psi.Environment[kv.Key] = kv.Value;

        using var proc = new Process { StartInfo = psi };
        proc.OutputDataReceived += (_, e) => { if (e.Data != null) onLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) onLine(e.Data); };

        try { proc.Start(); }
        catch (Win32Exception ex)
        {
            onLine($"Could not start '{file}': {ex.Message}");
            return -1;
        }

        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        using var reg = ct.Register(() => { try { if (!proc.HasExited) proc.Kill(true); } catch { } });
        await proc.WaitForExitAsync();
        return proc.ExitCode;
    }

    /// <summary>Quotes a string for a POSIX shell (BusyBox ash on OpenWrt, Android sh).</summary>
    public static string ShellQuote(string s) => "'" + s.Replace("'", "'\\''") + "'";

    /// <summary>Splits an argument string, honouring double quotes.</summary>
    public static List<string> SplitArgs(string s)
    {
        var res = new List<string>();
        var cur = new StringBuilder();
        bool inQ = false, has = false;
        foreach (var c in s)
        {
            if (c == '"') { inQ = !inQ; has = true; }
            else if (char.IsWhiteSpace(c) && !inQ)
            {
                if (has || cur.Length > 0) { res.Add(cur.ToString()); cur.Clear(); has = false; }
            }
            else cur.Append(c);
        }
        if (has || cur.Length > 0) res.Add(cur.ToString());
        return res;
    }
}
