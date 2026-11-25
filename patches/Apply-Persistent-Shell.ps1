$ErrorActionPreference = 'Stop'
$servicePath = 'SshAdbHub/SshService.cs'
$windowPath = 'SshAdbHub/MainWindow.xaml.cs'
$service = Get-Content $servicePath -Raw
if ($service -notmatch 'using System.Text;') { $service = 'using System.Text;' + [Environment]::NewLine + $service }
if ($service -notmatch 'using System.Text.RegularExpressions;') { $service = 'using System.Text.RegularExpressions;' + [Environment]::NewLine + $service }
if ($service -notmatch 'private ShellStream\? _shell') {
    $service = $service.Replace('    private SftpClient? _sftp;', '    private SftpClient? _sftp;' + [Environment]::NewLine + '    private ShellStream? _shell;')
}
if ($service -notmatch 'public void StartShell\(') {
    $insert = @'
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

    private static readonly Regex AnsiRx = new(@"\x1B(?:\][^\x07\x1B]*(?:\x07|\x1B\\)|\[[0-9;?]*[ -/]*[@-~])", RegexOptions.Compiled);

    public string ReadShellOutput()
    {
        if (!ShellStarted || !_shell!.DataAvailable) return "";
        var buffer = new byte[8192];
        var count = _shell.Read(buffer, 0, buffer.Length);
        if (count <= 0) return "";
        var raw = Encoding.UTF8.GetString(buffer, 0, count);
        return AnsiRx.Replace(raw, "");
    }

    public void InterruptShell()
    {
        if (ShellStarted) { _shell!.Write("\u0003"); _shell.Flush(); }
    }

'@
    $service = $service.Replace('    public void Dispose() => Disconnect();', $insert + '    public void Dispose() => Disconnect();')
}
$service = $service.Replace('        try { _sftp?.Dispose(); } catch { }', '        try { _shell?.Dispose(); } catch { }' + [Environment]::NewLine + '        _shell = null;' + [Environment]::NewLine + '        try { _sftp?.Dispose(); } catch { }')
Set-Content $servicePath $service -Encoding UTF8

$code = Get-Content $windowPath -Raw
if ($code -notmatch 'private readonly DispatcherTimer _shellPollTimer') {
    $code = $code.Replace('    private CancellationTokenSource? _consoleCts, _scriptCts;',
        '    private CancellationTokenSource? _scriptCts;' + [Environment]::NewLine +
        '    private readonly DispatcherTimer _shellPollTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };')
    $code = $code.Replace('        InitializeComponent();',
        '        InitializeComponent();' + [Environment]::NewLine +
        '        _shellPollTimer.Tick += ShellPollTimer_Tick;')
}
$start = $code.IndexOf('    private async void ConsoleRun_Click')
if ($start -lt 0) { throw 'ConsoleRun_Click not found in MainWindow.xaml.cs' }
$end = $code.LastIndexOf('}')
if ($end -lt $start) { throw 'Could not find end of console handlers.' }
$newHandlers = @'
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
'@
$code = $code.Substring(0, $start) + $newHandlers + [Environment]::NewLine + $code.Substring($end)
Set-Content $windowPath $code -Encoding UTF8
Write-Host 'Persistent interactive SSH shell implemented.'
