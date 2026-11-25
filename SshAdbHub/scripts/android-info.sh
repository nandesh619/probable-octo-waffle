#!/system/bin/sh
# target: android
# description: Prints model, Android version, battery level and storage of the selected Android device(s).
echo "Model:   $(getprop ro.product.model)"
echo "Android: $(getprop ro.build.version.release)  (SDK $(getprop ro.build.version.sdk))"
echo "Battery: $(dumpsys battery | grep -m1 level)"
echo "Storage:"
df -h /data | tail -1
