#!/system/bin/sh
# target: android
# description: Force-stops an app on the selected device(s). Example package: com.netflix.ninja
am force-stop {{package|com.netflix.ninja}}
echo "stopped {{package|com.netflix.ninja}}"
