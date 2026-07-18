#!/bin/sh
# Runs from cron every few minutes. Catches the "procd says running but
# not actually proxying" failure mode: the service can survive a
# ZeroBlock/nftables reload in a state where the port is still open but
# sing-box can't complete a TLS handshake through it (CONNECT succeeds,
# ClientHello gets reset). A plain TCP-connect check to the port would
# NOT catch this - has to be an actual request through the proxy.

LOGTAG="homevpn-proxy-healthcheck"
# Has to be a domain that's actually in ZeroBlock's tunneled list and goes
# through the VPN outbound - testing an unrelated direct-out domain
# (e.g. example.com) means a transient hiccup on the *direct* internet
# path reads as "proxy is broken" even though the VPN path everything
# actually cares about (Claude/ChatGPT) is fine. A lightweight static
# asset, not the main page, to go easy on bot-detection/rate-limiting.
TEST_URL="https://chatgpt.com/favicon.ico"
# cron runs in the router's main network namespace, not "homevpn" - its
# own 127.0.0.1 is a *different* loopback than the one sing-box listens
# on inside that namespace. Has to be the LAN-visible veth address.
PROXY="http://192.168.2.250:2080"

code=$(curl -s -o /dev/null -w '%{http_code}' --max-time 6 -x "$PROXY" "$TEST_URL" 2>/dev/null)

# Any real HTTP response (even a 4xx/5xx from the far end) proves the
# tunnel actually carried a TLS/HTTP exchange end to end. "000" means
# curl couldn't complete the request at all - connection refused/reset/
# timeout - which is the actual failure signature seen in this incident.
if [ "$code" = "000" ] || [ -z "$code" ]; then
	# Log the specific service-state symptoms this has actually been
	# observed failing with (veth knocked off br-lan, its ARP entry gone
	# stale, the sing-box process itself missing) before restarting, so
	# there's something to look at in `logread` afterwards instead of
	# just "it failed, then it was restarted".
	proc_alive=$(pgrep -f 'homevpn-proxy/config.json' >/dev/null 2>&1 && echo yes || echo no)
	netns_exists=$(ip netns list 2>/dev/null | awk '{print $1}' | grep -qx homevpn && echo yes || echo no)
	veth_in_bridge=$(ip link show veth-hv0 2>/dev/null | grep -q 'master br-lan' && echo yes || echo no)
	arp_state=$(ip neigh show 2>/dev/null | grep 192.168.2.250 | awk '{print $NF}')
	logger -t "$LOGTAG" "proxy check failed (curl exit/code='$code'); process=$proc_alive netns=$netns_exists veth_in_bridge=$veth_in_bridge arp=${arp_state:-none}; restarting homevpn-proxy"
	/etc/init.d/homevpn-proxy restart
else
	logger -t "$LOGTAG" -p daemon.debug "proxy check ok (http $code)"
fi
