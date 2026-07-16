#!/bin/sh
# Installs homevpn-proxy on this OpenWrt router.
# Run FROM the router, with this script sitting next to config.json and
# init.d.homevpn-proxy (e.g. after scp'ing the whole router/ folder here).
set -e

SELF_DIR="$(cd "$(dirname "$0")" && pwd)"
NS="homevpn"

mkdir -p /etc/homevpn-proxy
mkdir -p "/etc/netns/${NS}"

cp "$SELF_DIR/config.json" /etc/homevpn-proxy/config.json
cp "$SELF_DIR/init.d.homevpn-proxy" /etc/init.d/homevpn-proxy
chmod +x /etc/init.d/homevpn-proxy

[ -f "/etc/netns/${NS}/resolv.conf" ] || echo "nameserver 192.168.2.1" > "/etc/netns/${NS}/resolv.conf"

/etc/init.d/homevpn-proxy enable
/etc/init.d/homevpn-proxy start

sleep 1
echo "--- status ---"
/etc/init.d/homevpn-proxy status || true
ip netns list
echo "--------------"
echo "Installed. Proxy should be reachable at:"
echo "  socks5://192.168.2.250:2080"
echo "  http://192.168.2.250:2080"
