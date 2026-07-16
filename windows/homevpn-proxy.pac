// PAC (Proxy Auto-Config) for homevpn-proxy.
// Sends only Claude/Anthropic/OpenAI traffic through the LAN proxy on the
// router (which rides ZeroBlock's existing VPN routing); everything else
// goes direct, so corporate/other browsing is untouched.
//
// Keep this in sync with the domain list in ZeroBlock's "opera" profile
// (LuCI > ZeroBlock > opera > user_domains_text on the router) - that list
// is what actually determines which domains get tunneled; this file just
// tells the browser to reach those domains via the dedicated LAN proxy
// instead of directly, so Check Point never sees the connection attempt.
//
// Hosted on the router itself at http://192.168.2.1/homevpn-proxy.pac so
// it's reachable from the LAN regardless of Check Point state, and can be
// updated in one place if the domain list changes.

function FindProxyForURL(url, host) {
	host = host.toLowerCase();

	if (dnsDomainIs(host, "claude.ai") ||
	    dnsDomainIs(host, "anthropic.com") ||
	    dnsDomainIs(host, "claude.com") ||
	    dnsDomainIs(host, "chatgpt.com") ||
	    dnsDomainIs(host, "openai.com") ||
	    dnsDomainIs(host, "oaistatic.com") ||
	    dnsDomainIs(host, "oaiusercontent.com")) {
		return "PROXY 192.168.2.250:2080; DIRECT";
	}

	return "DIRECT";
}
