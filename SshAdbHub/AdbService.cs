using System.Text.RegularExpressions;

namespace SshAdbHub;

public class AdbService
{
    public string AdbPath { get; private set; } = "adb";

    /// <summary>Order: configured path -> platform-tools\adb.exe next to the app -> "adb" on PATH.</summary>
    public void Configure(string configured)
    {
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) { AdbPath = configured; return; }
        var bundled = Path.Combine(AppContext.BaseDirectory, "platform-tools", "adb.exe");
        AdbPath = File.Exists(bundled) ? bundled : "adb";
    }

    public Task<int> RunAsync(IEnumerable<string> args, Action<string> onLine, CancellationToken ct = default) =>
        ProcRun.RunAsync(AdbPath, args, onLine, ct);

    public async Task<List<AdbDevice>> ListDevicesAsync()
    {
        var lines = new List<string>();
        var code = await RunAsync(new[] { "devices", "-l" }, l => { lock (lines) lines.Add(l); });
        if (code != 0) throw new InvalidOperationException(lines.FirstOrDefault() ?? $"adb exited with code {code}");

        var result = new List<AdbDevice>();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("List of") || line.StartsWith("*")) continue;
            var parts = Regex.Split(line, @"\s+");
            if (parts.Length < 2) continue;
            result.Add(new AdbDevice
            {
                Serial = parts[0],
                State = parts[1],
                Description = string.Join(" ", parts.Skip(2))
            });
        }
        return result;
    }
}
