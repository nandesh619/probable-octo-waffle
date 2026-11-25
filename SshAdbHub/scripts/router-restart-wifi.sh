#!/bin/sh
# target: router
# description: Reloads the Wi-Fi configuration (brief Wi-Fi drop). Safe to run over SSH from a wired PC.
echo "Reloading Wi-Fi..."
wifi reload
echo "Done."
