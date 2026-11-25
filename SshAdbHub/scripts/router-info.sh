#!/bin/sh
# target: router
# description: Prints model, uptime, load, memory, WAN status and Wi-Fi clients of the OpenWrt router.
echo "--- board ---"
ubus call system board 2>/dev/null | grep -E '"(model|hostname|description)"'
echo "--- uptime / load ---"
uptime
echo "--- memory ---"
free | head -2
echo "--- WAN ---"
ifstatus wan 2>/dev/null | grep -E '"(up|ipv4-address|address)"' | head -5
echo "--- Wi-Fi clients ---"
for dev in $(iwinfo | awk '/ESSID/ {print $1}'); do
  echo "[$dev]"
  iwinfo "$dev" assoclist 2>/dev/null
done
