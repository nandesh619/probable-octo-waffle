$ErrorActionPreference = 'Stop'
$project = 'SshAdbHub'
$xamlPath = Join-Path $project 'MainWindow.xaml'
if (-not (Test-Path $xamlPath)) { throw "Missing $xamlPath" }
[xml]$xml = Get-Content $xamlPath -Raw
$xaml = Get-Content $xamlPath -Raw
$code = (Get-ChildItem $project -Filter '*.cs' -File | ForEach-Object { Get-Content $_.FullName -Raw }) -join "`n"

# 1. Check all XAML event handlers exist in code
$events = @('Click','Loaded','KeyDown','KeyUp','TextChanged','SelectionChanged','Checked','Unchecked','Drop','DragOver','Closing','Closed','MouseDoubleClick','PreviewKeyDown')
$missing = [System.Collections.Generic.List[string]]::new()
foreach ($eventName in $events) {
  foreach ($match in [regex]::Matches($xaml, '\b' + $eventName + '="([A-Za-z_][A-Za-z0-9_]*)"')) {
    $handler = $match.Groups[1].Value
    if ($code -notmatch ('\b' + [regex]::Escape($handler) + '\s*\(')) { $missing.Add("$eventName=$handler") }
  }
}
if ($missing.Count) { throw ('XAML event handlers missing from code: ' + ($missing -join ', ')) }

# 2. Check no duplicate x:Name attributes exist in MainWindow.xaml
$names = [regex]::Matches($xaml, 'x:Name="([^"]+)"') | ForEach-Object { $_.Groups[1].Value }
$dups = $names | Group-Object | Where-Object { $_.Count -gt 1 } | ForEach-Object { $_.Name }
if ($dups) { throw ('Duplicate x:Name found in MainWindow.xaml: ' + ($dups -join ', ')) }

# 3. Check essential named controls were not clobbered
foreach ($req in @('ProfileBox','ConnectBtn','WanIfaceBox','HistoryGrid','HistoryOut','ScriptOut','ConsoleOut','ConsoleInput','AdbOut','AppOut','LogBox','HomeTab','DashTab','AdbTab','AppsTab','StatusText')) {
  if ($names -notcontains $req) { throw "Required x:Name missing from MainWindow.xaml: $req" }
}

# 4. Style & layout invariants
$buttonStyles = [regex]::Matches($xaml, '<Style\s+TargetType="(?:\{x:Type\s+)?Button\}?"').Count
if ($buttonStyles -ne 1) { throw "Expected exactly one implicit Button style; found $buttonStyles" }
$tabItemStyles = [regex]::Matches($xaml, '<Style\s+TargetType="(?:\{x:Type\s+)?TabItem\}?"').Count
if ($tabItemStyles -ne 1) { throw "Expected exactly one implicit TabItem style; found $tabItemStyles" }
if ($xaml -notmatch 'x:Name="ConsoleOut"[^>]*Grid.Row="2"[^>]*Grid.ColumnSpan="99"') { throw 'Console output is not configured to stretch across the workspace.' }
if ($xaml -notmatch 'x:Name="ConsoleInput"[^>]*AcceptsReturn="True"') { throw 'Console input is not multiline.' }
if ($xaml -notmatch '<TabControl x:Name="MainTabs"[^>]*HorizontalContentAlignment="Stretch"[^>]*VerticalContentAlignment="Stretch"') { throw 'MainTabs content is not configured to stretch.' }
if ($xaml -match '<Style TargetType="\{x:Type TabItem\}">[\s\S]*?<Setter Property="HorizontalContentAlignment" Value="Left"/>') { throw 'TabItem HorizontalContentAlignment must be Stretch so selected tab content fills the pane.' }
if ($xaml -notmatch '<GridSplitter') { throw 'Expected GridSplitter for resizable panes.' }

Write-Host 'PASS: XAML parses, zero duplicate x:Name, event handlers verified, single Button/TabItem styles, full-width layouts and GridSplitters verified.'
