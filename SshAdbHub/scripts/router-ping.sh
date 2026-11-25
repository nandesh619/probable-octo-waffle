#!/bin/sh
# target: router
# description: Pings a host from the router. Parameters are asked in a small form before running.
ping -c {{count|4}} "{{host|1.1.1.1}}"
