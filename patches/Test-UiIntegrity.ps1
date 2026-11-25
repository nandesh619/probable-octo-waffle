$ErrorActionPreference = 'Stop'
$project = 'SshAdbHub'
$xamlPath = Join-Path $project 'MainWindow.xaml'
if (-not (Test-Path $xamlPath)) { throw "Missing $xamlPath" }
[xml]$xml = Get-Content $xamlPath -Raw
$xaml = Get-Content $xamlPath -Raw
$code = (Get-ChildItem $project -Filter '*.cs' -File | ForEach-Object { Get-Content $_.FullName -Raw }) -join "`n"
$events = @('Click','Loaded','KeyDown','KeyUp','TextChanged','SelectionChanged','Checked','Unchecked','Drop','DragOver','Closing','Closed','MouseDoubleClick','PreviewKeyDown')
$missing = [System.Collections.Generic.List[string]]::new()
foreach ($eventName in $events) {
  foreach ($match in [regex]::Matches($xaml, '\b' + $eventName + '="([A-Za-z_][A-Za-z0-9_]*)"')) {
    $handler = $match.Groups[1].Value
    if ($code -notmatch ('\b' + [regex]::Escape($handler) + '\s*\(')) { $missing.Add("$eventName=$handler") }
  }
}
if ($missing.Count) { throw ('XAML event handlers missing from code: ' + ($missing -join ', ')) }
$buttonStyles = [regex]::Matches($xaml, '<Style\s+TargetType="(?:\{x:Type\s+)?Button\}?"').Count
if ($buttonStyles -ne 1) { throw "Expected exactly one implicit Button style; found $buttonStyles" }
if ($xaml -notmatch 'x:Name="ConsoleOut"[^>]*Grid.Row="2"[^>]*Grid.ColumnSpan="99"') { throw 'Console output is not configured to stretch across the workspace.' }
if ($xaml -notmatch 'x:Name="ConsoleInput"[^>]*AcceptsReturn="True"') { throw 'Console input is not multiline.' }
if ($xaml -notmatch '<TabControl x:Name="MainTabs"[^>]*HorizontalContentAlignment="Stretch"[^>]*VerticalContentAlignment="Stretch"') { throw 'MainTabs content is not configured to stretch.' }
Write-Host 'PASS: XAML parses, event handlers scanned, no duplicate implicit styles, full-width console and multiline input verified.'
