// PAC (Proxy Auto-Config) for homevpn-proxy.
// Sends only Claude/Anthropic traffic through the LAN proxy on the router
// (which rides ZeroBlock's existing VPN routing); everything else goes
// direct, so corporate/other browsing is untouched.
//
// Hosted on the router itself at http://192.168.2.1/homevpn-proxy.pac so
// it's reachable from the LAN regardless of Check Point state, and can be
// updated in one place if the domain list changes.

function FindProxyForURL(url, host) {
	host = host.toLowerCase();

	if (dnsDomainIs(host, "claude.ai") ||
	    dnsDomainIs(host, "anthropic.com") ||
	    dnsDomainIs(host, "claude.com")) {
		return "PROXY 192.168.2.250:2080; DIRECT";
	}

	return "DIRECT";
}
