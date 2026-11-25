# target: pc
# description: Runs on this PC. Lists ADB devices using the adb.exe the app is configured with ($env:HUB_ADB).
Write-Host "Using adb: $env:HUB_ADB"
& $env:HUB_ADB devices -l
