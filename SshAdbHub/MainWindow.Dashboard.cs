using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace SshAdbHub;

// "Router dashboard" tab (live cards, Wi-Fi clients) and the config backup button.
public partial class MainWindow
{
    private readonly ObservableCollection<WifiClient> _clients = new();
    private readonly DispatcherTimer _dashTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool _dashBusy;
    private long _lastRx = -1, _lastTx = -1;
    private DateTime _lastRateTime;
    private string _lastWanDev = "";

    private static readonly Regex EssidRx = new(@"ESSID:\s*""([^""]*)""");
    private static readonly Regex ChanRx = new(@"Channel:\s*(\d+)\s*\(([\d.]+)\s*GHz\)");
    private static readonly Regex AssocRx = new(@"^\s*([0-9A-Fa-f]{2}(?::[0-9A-Fa-f]{2}){5})\s+(-?\d+)\s*dBm");
    private static readonly Regex RateRx = new(@"^\s*(RX|TX):\s*([\d.]+)\s*MBit/s");

    private void InitDashboard()
    {
        ClientGrid.ItemsSource = _clients;
        WanIfaceBox.Text = string.IsNullOrWhiteSpace(Cfg.WanInterface) ? "wan" : Cfg.WanInterface;
        _dashTimer.Tick += async (_, _) =>
        {
            if (DashAutoChk.IsChecked == true && ReferenceEquals(MainTabs.SelectedItem, DashTab) && _ssh.IsConnected)
                await RefreshDashboardAsync();
        };
        _dashTimer.Start();
        UpdateBackupLabel();
    }

    private async void DashRefresh_Click(object sender, RoutedEventArgs e) => await RefreshDashboardAsync();

    private void MainTabs_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        // SelectionChanged of inner controls bubbles up here too - only react to the tab control itself
        if (!ReferenceEquals(e.OriginalSource, MainTabs)) return;
        if (IsLoaded && ReferenceEquals(MainTabs.SelectedItem, DashTab) && _ssh.IsConnected) _ = RefreshDashboardAsync();
    }

    // ------------------------------------------------------------ refresh + parse
    private async Task RefreshDashboardAsync()
    {
        if (_dashBusy) return;
        if (!_ssh.IsConnected) { DashStatus.Text = "Not connected - click Connect first."; return; }
        _dashBusy = true;
        try
        {
            var wan = WanIfaceBox.Text.Trim();
            if (wan.Length == 0) wan = "wan";
            if (wan != Cfg.WanInterface) { Cfg.WanInterface = wan; _store.Save(); }

            var cmd = SshService.BuildScriptCommand(DashScript.Text, "hub-dashboard", new[] { wan });
            var (_, lines) = await Task.Run(() => _ssh.RunCapture(cmd, TimeSpan.FromSeconds(20)));
            ParseDashboard(lines);
            DashStatus.Text = $"Updated {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex) { DashStatus.Text = "Error: " + ex.Message; }
        finally { _dashBusy = false; }
    }

    private static string FormatDuration(long sec)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, sec));
        if (t.TotalDays >= 1) return $"{(int)t.TotalDays}d {t.Hours}h {t.Minutes}m";
        if (t.TotalHours >= 1) return $"{t.Hours}h {t.Minutes}m";
        return $"{t.Minutes}m {t.Seconds}s";
    }

    private static string FormatRate(double bitsPerSec) =>
        bitsPerSec >= 1_000_000 ? $"{bitsPerSec / 1_000_000:0.0} Mbit/s" : $"{bitsPerSec / 1000:0} kbit/s";

    private static string BandOf(double ghz) => ghz >= 5.9 ? "6 GHz" : ghz >= 4.9 ? "5 GHz" : "2.4 GHz";

    private static string QualityOf(int dbm) => dbm >= -55 ? "Excellent" : dbm >= -67 ? "Good" : dbm >= -75 ? "Fair" : "Poor";

    private void ParseDashboard(List<string> lines)
    {
        var kv = new Dictionary<string, string>();
        var wifi = new List<string>();
        var leases = new List<string>();
        var section = "";

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd('\r');
            if (line.StartsWith("@@"))
            {
                var tag = line.Substring(2);
                if (tag.StartsWith("IF ")) { section = "WIFI"; wifi.Add(line); }
                else section = tag;
                continue;
            }
            switch (section)
            {
                case "BOARD":
                case "SYS":
                case "WAN":
                    var i = line.IndexOf('=');
                    if (i > 0) kv[section + "." + line[..i]] = line[(i + 1)..];
                    break;
                case "WIFI": wifi.Add(line); break;
                case "LEASES": leases.Add(line); break;
            }
        }
        string G(string k) => kv.TryGetValue(k, out var v) ? v.Trim() : "";

        // ---- system card
        SysModelText.Text = G("BOARD.model") is { Length: > 0 } m ? m : "(unknown model)";
        SysHostText.Text = $"{G("BOARD.hostname")}   {G("BOARD.release")}".Trim();
        SysUptimeText.Text = long.TryParse(G("SYS.uptime"), out var up) ? "Up " + FormatDuration(up) : "";
        SysLoadText.Text = G("SYS.load") is { Length: > 0 } ld ? "Load: " + ld : "";
        var mem = G("SYS.mem").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (mem.Length == 2 && double.TryParse(mem[0], out var tot) && double.TryParse(mem[1], out var avail) && tot > 0)
        {
            var used = tot - avail;
            MemBar.Value = used / tot * 100;
            MemText.Text = $"RAM {used / 1024:0} / {tot / 1024:0} MB ({used / tot * 100:0}%)";
        }

        // ---- WAN card
        if (G("WAN.missing") == "1")
        {
            WanStatusText.Text = "Not found";
            WanStatusText.Foreground = Brushes.DarkOrange;
            WanIpText.Text = "Interface '" + WanIfaceBox.Text.Trim() + "' does not exist";
            WanInfoText.Text = "";
            WanRateText.Text = "";
        }
        else
        {
            var isUp = G("WAN.up") is "1" or "true";
            WanStatusText.Text = isUp ? "Connected" : "Down";
            WanStatusText.Foreground = isUp ? Brushes.SeaGreen : Brushes.Firebrick;
            WanIpText.Text = G("WAN.ip") is { Length: > 0 } ip ? "IP " + ip : "No IPv4 address";
            var info = G("WAN.proto");
            if (long.TryParse(G("WAN.uptime"), out var wu) && isUp) info += (info.Length > 0 ? "  -  " : "") + "up " + FormatDuration(wu);
            if (G("WAN.dns").Length > 0) info += "  -  DNS " + G("WAN.dns");
            WanInfoText.Text = info;

            var dev = G("WAN.dev");
            if (long.TryParse(G("WAN.rx"), out var rx) && long.TryParse(G("WAN.tx"), out var tx))
            {
                var now = DateTime.UtcNow;
                if (_lastRx >= 0 && dev == _lastWanDev && rx >= _lastRx && tx >= _lastTx && (now - _lastRateTime).TotalSeconds > 0.5)
                {
                    var dt = (now - _lastRateTime).TotalSeconds;
                    WanRateText.Text = $"Down {FormatRate((rx - _lastRx) * 8 / dt)}   Up {FormatRate((tx - _lastTx) * 8 / dt)}";
                }
                else WanRateText.Text = "Measuring speed...";
                _lastRx = rx; _lastTx = tx; _lastRateTime = now; _lastWanDev = dev;
            }
            else WanRateText.Text = "";
        }

        // ---- Wi-Fi clients
        var leaseMap = new Dictionary<string, (string ip, string host)>();
        foreach (var l in leases)
        {
            var p = l.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length >= 3) leaseMap[p[1].ToLowerInvariant()] = (p[2], p.Length >= 4 && p[3] != "*" ? p[3] : "");
        }

        var list = new List<WifiClient>();
        string essid = "", band = "";
        WifiClient? cur = null;
        foreach (var l in wifi)
        {
            if (l.StartsWith("@@IF ")) { essid = ""; band = ""; cur = null; continue; }
            var me = EssidRx.Match(l);
            if (me.Success) essid = me.Groups[1].Value;
            var mc = ChanRx.Match(l);
            if (mc.Success) band = BandOf(double.Parse(mc.Groups[2].Value, CultureInfo.InvariantCulture));

            var ma = AssocRx.Match(l);
            if (ma.Success)
            {
                var mac = ma.Groups[1].Value;
                var dbm = int.Parse(ma.Groups[2].Value, CultureInfo.InvariantCulture);
                leaseMap.TryGetValue(mac.ToLowerInvariant(), out var lease);
                cur = new WifiClient
                {
                    Mac = mac.ToUpperInvariant(),
                    Signal = dbm,
                    SignalText = QualityOf(dbm),
                    Radio = band.Length > 0 ? $"{essid} ({band})" : essid,
                    Ip = lease.ip ?? "",
                    Hostname = string.IsNullOrEmpty(lease.host) ? "(unknown)" : lease.host
                };
                list.Add(cur);
                continue;
            }
            var mr = RateRx.Match(l);
            if (mr.Success && cur != null)
            {
                var rate = mr.Groups[2].Value + " Mbit/s";
                if (mr.Groups[1].Value == "RX") cur.RxRate = rate; else cur.TxRate = rate;
            }
        }

        _clients.Clear();
        foreach (var c in list.OrderByDescending(c => c.Signal)) _clients.Add(c);
        ClientsCountText.Text = list.Count == 1 ? "1 client" : $"{list.Count} clients";
        var perBand = list.GroupBy(c => { var i2 = c.Radio.LastIndexOf('('); return i2 >= 0 ? c.Radio[(i2 + 1)..].TrimEnd(')') : "?"; })
            .Select(g => $"{g.Key}: {g.Count()}");
        ClientsBandText.Text = string.Join("    ", perBand);
        UpdateBackupLabel();
    }

    // ------------------------------------------------------------ config backup
    private void UpdateBackupLabel()
    {
        var p = _ssh.IsConnected ? _connectedProfile : ProfileBox.SelectedItem as SshProfile;
        if (p == null) { BackupInfo.Text = ""; return; }
        if (p.LastBackup is not DateTime t)
        {
            BackupInfo.Text = "⚠ No config backup taken yet for this router. Back up before changing settings.";
            BackupInfo.Foreground = Brushes.DarkOrange;
            return;
        }
        var days = (DateTime.Now - t).TotalDays;
        BackupInfo.Text = $"Last backup: {t:yyyy-MM-dd HH:mm} ({(days < 1 ? "today" : $"{(int)days} days ago")})" +
                          (days > 30 ? "   ⚠ older than 30 days" : "");
        BackupInfo.Foreground = days > 30 ? Brushes.DarkOrange : SystemColors.ControlTextBrush;
    }

    private async void Backup_Click(object sender, RoutedEventArgs e)
    {
        if (!_ssh.IsConnected || _connectedProfile == null)
        {
            DashStatus.Text = "Connect to the router first.";
            return;
        }
        var p = _connectedProfile;
        BackupBtn.IsEnabled = false;
        try
        {
            SetStatus("Creating router backup...");
            var dir = SettingsStore.BackupDir(Cfg);
            Directory.CreateDirectory(dir);
            var safe = Regex.Replace(p.Name, @"[^A-Za-z0-9_.-]+", "_");
            var local = Path.Combine(dir, $"{safe}-backup-{DateTime.Now:yyyyMMdd-HHmmss}.tar.gz");
            const string remote = "/tmp/hub-backup.tar.gz";

            var (code, lines) = await Task.Run(() => _ssh.RunCapture($"sysupgrade -b {remote} && ls -l {remote}", TimeSpan.FromSeconds(90)));
            if (code != 0) throw new InvalidOperationException("sysupgrade -b failed: " + string.Join(" ", lines));

            await Task.Run(() =>
            {
                if (_ssh.SftpAvailable) _ssh.Download(remote, local);
                else _ssh.DownloadViaCat(remote, local);
            });
            await Task.Run(() => _ssh.RunCapture($"rm -f {remote}", TimeSpan.FromSeconds(10)));

            var size = new FileInfo(local).Length;
            if (size < 200) throw new InvalidOperationException($"The downloaded backup is only {size} bytes - something went wrong.");

            p.LastBackup = DateTime.Now;
            p.LastBackupFile = local;
            _store.Save();
            Log($"Router backup saved: {local} ({size / 1024.0:0.#} KB)");
            DashStatus.Text = "Backup saved: " + local;
        }
        catch (Exception ex)
        {
            Log("Backup failed: " + ex.Message);
            DashStatus.Text = "Backup failed: " + ex.Message;
            MessageBox.Show(this, "Backup failed:\n" + ex.Message, "Config backup", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BackupBtn.IsEnabled = true;
            UpdateBackupLabel();
            UpdateConnectionUi();
        }
    }

    private void OpenBackups_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dir = SettingsStore.BackupDir(Cfg);
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
        }
        catch (Exception ex) { Log("Cannot open backups folder: " + ex.Message); }
    }
}

/// <summary>Shell script (POSIX sh / BusyBox ash) that gathers the dashboard data on OpenWrt. $1 = WAN interface name.</summary>
internal static class DashScript
{
    public const string Text = """
W="${1:-wan}"
echo "@@BOARD"
B=$(ubus call system board 2>/dev/null)
echo "model=$(echo "$B" | jsonfilter -e '@.model' 2>/dev/null)"
echo "hostname=$(echo "$B" | jsonfilter -e '@.hostname' 2>/dev/null)"
echo "release=$(echo "$B" | jsonfilter -e '@.release.description' 2>/dev/null)"
echo "@@SYS"
echo "uptime=$(cut -d. -f1 /proc/uptime)"
echo "load=$(cut -d' ' -f1-3 /proc/loadavg)"
awk '/^MemTotal:/{t=$2} /^MemAvailable:/{a=$2} /^MemFree:/{f=$2} END{if(a=="")a=f; print "mem=" t " " a}' /proc/meminfo
echo "@@WAN"
J=$(ifstatus "$W" 2>/dev/null)
if [ -n "$J" ]; then
  echo "up=$(echo "$J" | jsonfilter -e '@.up' 2>/dev/null)"
  echo "ip=$(echo "$J" | jsonfilter -e '@["ipv4-address"][0].address' 2>/dev/null)"
  echo "proto=$(echo "$J" | jsonfilter -e '@.proto' 2>/dev/null)"
  echo "uptime=$(echo "$J" | jsonfilter -e '@.uptime' 2>/dev/null)"
  echo "dns=$(echo "$J" | jsonfilter -e '@["dns-server"][0]' 2>/dev/null)"
  D=$(echo "$J" | jsonfilter -e '@.l3_device' 2>/dev/null)
  echo "dev=$D"
  if [ -n "$D" ] && [ -d "/sys/class/net/$D" ]; then
    echo "rx=$(cat /sys/class/net/$D/statistics/rx_bytes)"
    echo "tx=$(cat /sys/class/net/$D/statistics/tx_bytes)"
  fi
else
  echo "missing=1"
fi
echo "@@WIFI"
for d in $(iwinfo 2>/dev/null | awk '/ESSID/{print $1}'); do
  echo "@@IF $d"
  iwinfo "$d" info 2>/dev/null | grep -E 'ESSID|Channel'
  iwinfo "$d" assoclist 2>/dev/null
done
echo "@@LEASES"
cat /tmp/dhcp.leases 2>/dev/null
echo "@@END"
""";
}
