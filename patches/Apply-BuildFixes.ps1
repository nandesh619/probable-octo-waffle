$ErrorActionPreference = "Stop"

$project = "SshAdbHub"
$scriptsFile = Join-Path $project "MainWindow.Scripts.cs"
$projectFile = Join-Path $project "SshAdbHub.csproj"

if (-not (Test-Path $scriptsFile)) { throw "Missing source file: $scriptsFile" }
if (-not (Test-Path $projectFile)) { throw "Missing project file: $projectFile" }

# Make the extension variable's type explicit for the WPF build.
$source = Get-Content -LiteralPath $scriptsFile -Raw
$old = 'var ext = fi.Extension.ToLowerInvariant();'
$new = 'string ext = System.IO.Path.GetExtension(fi.FullName).ToLowerInvariant();'
$count = ([regex]::Matches($source, [regex]::Escape($old))).Count
if ($count -ne 2) { throw "Expected two script-extension declarations; found $count" }
$source = $source.Replace($old, $new)
Set-Content -LiteralPath $scriptsFile -Value $source -Encoding utf8 -NoNewline


# Avoid hiding FrameworkElement.Tag in the ADB logging helper.
$adbFile = Join-Path $project "MainWindow.Adb.cs"
$adbSource = Get-Content -LiteralPath $adbFile -Raw
$adbSource = $adbSource.Replace("Tag(AdbDevice d)", "DeviceLogTag(AdbDevice d)")
$adbSource = [regex]::Replace($adbSource, '\bTag\(d\)', 'DeviceLogTag(d)')
Set-Content -LiteralPath $adbFile -Value $adbSource -Encoding utf8 -NoNewline

# Move off the SSH.NET release flagged by NuGet's vulnerability audit.
$projectXml = Get-Content -LiteralPath $projectFile -Raw
$oldPackage = 'SSH.NET" Version="2024.2.0"'
$newPackage = 'SSH.NET" Version="2026.0.1"'
if (-not $projectXml.Contains($oldPackage) -and -not $projectXml.Contains($newPackage)) {
    throw "SSH.NET package reference did not match the expected version."
}
$projectXml = $projectXml.Replace($oldPackage, $newPackage)
Set-Content -LiteralPath $projectFile -Value $projectXml -Encoding utf8 -NoNewline

Write-Host "Applied source fixes. SSH.NET package reference:"
Select-String -Path $projectFile -Pattern 'SSH.NET'
