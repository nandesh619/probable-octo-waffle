using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace SshAdbHub;

// Scripts tab (targets, {{placeholders}}, safety checks) and run history.
public partial class MainWindow
{
    private static readonly Regex TargetRx = new(@"^\s*(?:#|REM|::)\s*target:\s*(router|android|pc)", RegexOptions.IgnoreCase | RegexOptions.Multiline);
    private static readonly Regex DescRx = new(@"^\s*(?:#|REM|::)\s*description:\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Multiline);
    private static readonly Regex PlaceholderRx = new(@"\{\{\s*([A-Za-z_][A-Za-z0-9_]*)\s*(?:\|([^}]*))?\}\}");
    private static readonly string[] DangerWords =
        { "rm -rf", "sysupgrade", "firstboot", "reboot", "poweroff", "mkfs", "dd if=", "mtd ", "factory", "wipe" };

    private readonly List<RunRecord> _history = new();
    private readonly ObservableCollection<RunRecord> _historyView = new();

    // ------------------------------------------------------------ script list
    private string ScriptsDir =>
        string.IsNullOrWhiteSpace(Cfg.ScriptsFolder) ? Path.Combine(AppContext.BaseDirectory, "scripts") : Cfg.ScriptsFolder;

    private void RefreshScripts()
    {
        ScriptsFolderBox.Text = ScriptsDir;
        try { Directory.CreateDirectory(ScriptsDir); } catch { }
        var exts = new[] { ".sh", ".ps1", ".bat", ".cmd" };
        ScriptList.ItemsSource = Directory.Exists(ScriptsDir)
            ? new DirectoryInfo(ScriptsDir).GetFiles()
                .Where(f => exts.Contains(f.Extension.ToLowerInvariant()))
                .OrderBy(f => f.Name).ToList()
            : new List<FileInfo>();
    }

    private void ScriptsReload_Click(object sender, RoutedEventArgs e) => RefreshScripts();

    private void ScriptsOpen_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(ScriptsDir) { UseShellExecute = true }); }
        catch (Exception ex) { Log("Cannot open folder: " + ex.Message); }
    }

    private void ScriptsFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Scripts folder" };
        if (dlg.ShowDialog(this) != true) return;
        Cfg.ScriptsFolder = dlg.FolderName;
        _store.Save();
        RefreshScripts();
    }

    private void ScriptList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ScriptList.SelectedItem is not FileInfo fi) { ScriptDesc.Text = ""; return; }
        string head;
        try { head = File.ReadAllText(fi.FullName); } catch { return; }
        if (head.Length > 2000) head = head[..2000];

        var t = TargetRx.Match(head);
        if (t.Success)
            ScriptTarget.SelectedIndex = t.Groups[1].Value.ToLowerInvariant() switch { "router" => 0, "android" => 1, _ => 2 };
        else
        {
            string ext = System.IO.Path.GetExtension(fi.FullName).ToLowerInvariant();
            if (ext == ".ps1" || ext == ".bat" || ext == ".cmd") ScriptTarget.SelectedIndex = 2;
        }

        var d = DescRx.Match(head);
        var desc = d.Success ? d.Groups[1].Value.Trim() : "";
        var names = PlaceholderRx.Matches(head).Select(m => m.Groups[1].Value).Distinct().ToList();
        if (names.Count > 0) desc += (desc.Length > 0 ? "   " : "") + "Parameters: " + string.Join(", ", names);
        ScriptDesc.Text = desc;
    }

    private void ScriptEdit_Click(object sender, RoutedEventArgs e)
    {
        if (ScriptList.SelectedItem is not FileInfo fi) return;
        try { Process.Start(new ProcessStartInfo(fi.FullName) { UseShellExecute = true, Verb = "edit" }); }
        catch
        {
            try { Process.Start(new ProcessStartInfo("notepad.exe", "\"" + fi.FullName + "\"") { UseShellExecute = true }); }
            catch (Exception ex) { Log("Cannot open editor: " + ex.Message); }
        }
    }

    private void ScriptStop_Click(object sender, RoutedEventArgs e) => _scriptCts?.Cancel();
    private void ScriptClear_Click(object sender, RoutedEventArgs e) => ScriptOut.Clear();

    // ------------------------------------------------------------ placeholders
    /// <summary>Replaces {{name}} / {{name|default}} with values typed into a small form. Returns null if cancelled.</summary>
    private string? ApplyParams(string scriptName, string text)
    {
        var fields = new List<(string name, string def)>();
        foreach (Match m in PlaceholderRx.Matches(text))
        {
            var n = m.Groups[1].Value;
            if (fields.All(f => f.name != n)) fields.Add((n, m.Groups[2].Success ? m.Groups[2].Value.Trim() : ""));
        }
        if (fields.Count == 0) return text;

        Cfg.ScriptParams.TryGetValue(scriptName, out var saved);
        var prefilled = new List<(string name, string def)>();
        foreach (var f in fields)
        {
            string val = f.def;
            if (saved != null && saved.TryGetValue(f.name, out var last)) val = last;
            prefilled.Add((f.name, val));
        }

        var result = Dialogs.PromptParams(this, "Script parameters: " + scriptName, prefilled);
        if (result == null) return null;
        Cfg.ScriptParams[scriptName] = result;
        _store.Save();
        return PlaceholderRx.Replace(text, m => result.TryGetValue(m.Groups[1].Value, out var v) ? v : "");
    }

    // ------------------------------------------------------------ run
    private async void ScriptRun_Click(object sender, RoutedEventArgs e)
    {
        if (ScriptList.SelectedItem is not FileInfo fi) { Out(ScriptOut, "Select a script first."); return; }

        string original;
        try { original = File.ReadAllText(fi.FullName).Replace("\r\n", "\n"); }
        catch (Exception ex) { Out(ScriptOut, "Cannot read script: " + ex.Message); return; }

        var target = ScriptTarget.SelectedIndex;
        var targetName = ((ComboBoxItem)ScriptTarget.SelectedItem).Content?.ToString() ?? "";
        string ext = System.IO.Path.GetExtension(fi.FullName).ToLowerInvariant();
        var devs = new List<AdbDevice>();

        if (target == 0 && !_ssh.IsConnected) { Out(ScriptOut, "Not connected to the router. Click Connect first."); return; }
        if (target == 1)
        {
            devs = SelectedDevices();
            if (devs.Count == 0) { Out(ScriptOut, "No ready Android device selected (ADB tab -> tick 'Use')."); return; }
        }
        if (target == 2 && ext != ".ps1" && ext != ".bat" && ext != ".cmd")
        {
            Out(ScriptOut, "The 'This PC' target runs .ps1, .bat and .cmd files.");
            return;
        }

        var hadParams = PlaceholderRx.IsMatch(original);
        var text = ApplyParams(fi.Name, original);
        if (text == null) return;

        var hits = DangerWords.Where(w => text.Contains(w, StringComparison.OrdinalIgnoreCase)).ToList();
        if (hits.Count > 0)
        {
            var msg = "This script contains potentially destructive commands:\n\n" + string.Join(", ", hits);
            if (target == 0 && _connectedProfile?.LastBackup == null)
                msg += "\n\nâš  No config backup of this router has been taken yet (Router dashboard -> Back up config now).";
            msg += "\n\nRun it anyway?";
            if (MessageBox.Show(this, msg, "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        }

        var args = ProcRun.SplitArgs(ScriptArgs.Text);
        _scriptCts = new CancellationTokenSource();
        var ct = _scriptCts.Token;
        ScriptRunBtn.IsEnabled = false;

        var rec = new RunRecord { Time = DateTime.Now, Script = fi.Name, Target = targetName, Args = ScriptArgs.Text };
        var sb = new StringBuilder();
        void Emit(string l)
        {
            lock (sb) { if (sb.Length < 30000) sb.AppendLine(l); }
            Out(ScriptOut, l);
        }
        var exits = new List<string>();
        var sw = Stopwatch.StartNew();
        Emit($"=== {fi.Name}  ->  {targetName} ===");

        try
        {
            if (target == 0)
            {
                var cmd = SshService.BuildScriptCommand(text, fi.Name, args);
                var code = await Task.Run(() => _ssh.RunStreaming(cmd, Emit, ct, TimeSpan.FromMinutes(10)));
                Emit($"[exit {code}]");
                exits.Add(code.ToString());
            }
            else if (target == 1)
            {
                var codes = await Task.WhenAll(devs.Select(d => RunAndroidScriptAsync(d, text, args, Emit, ct)));
                exits.AddRange(codes.Select(c => c.ToString()));
            }
            else
            {
                var env = new Dictionary<string, string> { ["HUB_ADB"] = _adb.AdbPath };
                string runPath = fi.FullName;
                string? tmp = null;
                try
                {
                    if (hadParams)
                    {
                        tmp = Path.Combine(Path.GetTempPath(), "hub_" + Guid.NewGuid().ToString("N") + ext);
                        File.WriteAllText(tmp, text.Replace("\n", "\r\n"), new UTF8Encoding(ext == ".ps1"));
                        runPath = tmp;
                    }
                    int code;
                    if (ext == ".ps1")
                        code = await ProcRun.RunAsync("powershell.exe",
                            new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", runPath }.Concat(args), Emit, ct, env);
                    else
                        code = await ProcRun.RunAsync("cmd.exe", new[] { "/c", runPath }.Concat(args), Emit, ct, env);
                    Emit($"[exit {code}]");
                    exits.Add(code.ToString());
                }
                finally
                {
                    if (tmp != null) { try { File.Delete(tmp); } catch { } }
                }
            }
        }
        catch (Exception ex)
        {
            Emit("Error: " + ex.Message);
            exits.Add("error");
        }
        finally
        {
            ScriptRunBtn.IsEnabled = true;
            rec.ExitText = string.Join(",", exits);
            rec.DurationSec = sw.Elapsed.TotalSeconds;
            rec.Output = sb.ToString();
            AddHistory(rec);
        }
    }

    private async Task<int> RunAndroidScriptAsync(AdbDevice d, string text, List<string> args, Action<string> emit, CancellationToken ct)
    {
        void P(string l) => emit($"[{d.DisplayName}] {l}");
        var tmp = Path.Combine(Path.GetTempPath(), "hub_" + Guid.NewGuid().ToString("N") + ".sh");
        const string remote = "/data/local/tmp/hub_script.sh";
        await File.WriteAllTextAsync(tmp, text, new UTF8Encoding(false));
        try
        {
            var push = await _adb.RunAsync(new[] { "-s", d.Serial, "push", tmp, remote }, P, ct);
            if (push != 0) { P("push failed"); return push; }
            var cmd = "sh " + remote + string.Concat(args.Select(a => " " + ProcRun.ShellQuote(a)));
            var code = await _adb.RunAsync(new[] { "-s", d.Serial, "shell", cmd }, P, ct);
            P($"[exit {code}]");
            return code;
        }
        finally
        {
            try { File.Delete(tmp); } catch { }
            try { await _adb.RunAsync(new[] { "-s", d.Serial, "shell", "rm", "-f", remote }, _ => { }); } catch { }
        }
    }

    // ------------------------------------------------------------ history
    private void InitHistory()
    {
        _history.AddRange(SettingsStore.LoadHistory());
        foreach (var r in _history) _historyView.Add(r);
        HistoryGrid.ItemsSource = _historyView;
    }

    private void AddHistory(RunRecord r)
    {
        _history.Insert(0, r);
        _historyView.Insert(0, r);
        while (_history.Count > 200)
        {
            _history.RemoveAt(_history.Count - 1);
            _historyView.RemoveAt(_historyView.Count - 1);
        }
        SettingsStore.SaveHistory(_history);
    }

    private void HistoryGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HistoryGrid.SelectedItem is not RunRecord r) { HistoryOut.Text = ""; return; }
        HistoryOut.Text = $"{r.TimeText}   {r.Script}  ->  {r.Target}\nArguments: {(r.Args.Length > 0 ? r.Args : "(none)")}\nExit: {r.ExitText}    Duration: {r.DurationText}\n\n{r.Output}";
    }

    private void HistoryClear_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Delete the whole run history?", "History", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _history.Clear();
        _historyView.Clear();
        HistoryOut.Text = "";
        SettingsStore.SaveHistory(_history);
    }

    private void HistoryCopy_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryOut.Text.Length > 0) Clipboard.SetText(HistoryOut.Text);
    }
}
