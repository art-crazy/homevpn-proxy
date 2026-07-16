#!/bin/sh
# Fully removes homevpn-proxy from this OpenWrt router.
# Does NOT touch ZeroBlock, Check Point, or any existing router config -
# this only cleans up what install.sh created.

/etc/init.d/homevpn-proxy stop 2>/dev/null || true
/etc/init.d/homevpn-proxy disable 2>/dev/null || true

ip netns delete homevpn 2>/dev/null || true
ip link delete veth-hv0 2>/dev/null || true

rm -f /etc/init.d/homevpn-proxy
rm -rf /etc/homevpn-proxy
rm -rf /etc/netns/homevpn

echo "homevpn-proxy removed."
