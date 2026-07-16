# homevpn-proxy

Forces ChatGPT / Claude / Claude Code / Codex CLI / browser traffic to go
through the home router's VPN (managed by ZeroBlock/sing-box on OpenWrt),
even while the corporate Check Point Endpoint VPN is connected on the PC -
without touching Check Point settings, and without depending on which VPN
ZeroBlock currently has active.

## The problem

The router (OpenWrt 24.10.4 + ZeroBlock 0.6.2 + sing-box) already does
domain-based VPN routing: it transparently intercepts LAN traffic
(TPROXY), sniffs the TLS SNI, and if the domain is in ZeroBlock's list
(`claude.ai`, `anthropic.com`, etc. under the `opera` profile), routes it
through the VPN. This works great for normal LAN traffic.

The catch: when the corporate Check Point VPN is active on the PC, it
intercepts traffic to certain destination IPs (e.g. Cloudflare IPs used by
chatgpt.com/claude.ai) **at the Windows driver level, before it ever
reaches the router**. The router's filter never sees this traffic, so it
can't route it anywhere.

The one thing Check Point does *not* intercept: connections to addresses
inside the home LAN (192.168.2.0/24) - confirmed via `tracert 192.168.2.1`
staying at 1 hop even with the corporate VPN connected.

## The fix

Instead of fighting Check Point, route around it: point apps at a proxy
living on the LAN. Check Point lets that connection through (it's local),
the router receives it and handles VPN routing itself, same as it always
has.

Concretely: `install.sh` creates a network namespace on the router with a
veth pair bridged into `br-lan`, giving it its own address,
`192.168.2.250`. From the router's/ZeroBlock's point of view this looks
like an ordinary extra device on the LAN. A tiny sing-box instance (SOCKS5
+ HTTP CONNECT, "mixed" inbound) runs inside that namespace on port
`2080`. Because it's indistinguishable from a real LAN client, its
outbound connections ride the *existing* ZeroBlock TPROXY/domain-routing
pipeline automatically - no VPN credentials are copied into this repo, no
duplicated config to keep in sync. Whatever VPN ZeroBlock currently routes
`claude.ai`/`anthropic.com` through is what this proxy uses too, including
after you switch VPN servers on the router.

Verified end-to-end on the router itself: a request through
`socks5://192.168.2.250:2080` to `claude.ai` shows up in ZeroBlock's own
connection log (`clash_api`, port 9090) as
`"chains":["opera"], "host":"claude.ai", "sourceIP":"192.168.2.250"` -
i.e. actually routed through the VPN outbound, not direct.

## What this does NOT change

- ZeroBlock config/domains - untouched (unless you explicitly add more
  domains, see "Extending the domain list" below).
- Check Point - untouched, not even inspected beyond checking it's
  running.
- Any existing router service - this is a new, separate, additive systemd
  init service (`/etc/init.d/homevpn-proxy`).

## Install

```sh
scp -r router/ root@192.168.2.1:/tmp/homevpn-proxy-install
ssh root@192.168.2.1 'sh /tmp/homevpn-proxy-install/install.sh'
```

Requires the `kmod-veth` package on the router (installed automatically
via `opkg` the first time if missing - needs internet access on the
router at install time).

After install, the proxy listens at:
- `socks5://192.168.2.250:2080`
- `http://192.168.2.250:2080` (HTTP CONNECT)

Same port serves both protocols (sing-box "mixed" inbound auto-detects).

## Uninstall (full rollback)

```sh
ssh root@192.168.2.1 'sh /tmp/homevpn-proxy-install/uninstall.sh'
```

Stops the service, deletes the network namespace/veth, removes
`/etc/init.d/homevpn-proxy`, `/etc/homevpn-proxy/`, `/etc/netns/homevpn/`.
Nothing else on the router is touched. `kmod-veth` is left installed
(harmless, ~30 KB).

## Windows setup

### Claude Code / Codex CLI / other CLI tools

Point them at the proxy via the standard env vars. Two ways:

**Session-scoped (safer - only affects the current terminal):**
```powershell
$env:HTTP_PROXY  = "http://192.168.2.250:2080"
$env:HTTPS_PROXY = "http://192.168.2.250:2080"
$env:ALL_PROXY   = "socks5://192.168.2.250:2080"
```

**Permanent (all new processes for your user pick it up):**
```powershell
windows/set-proxy.ps1     # sets it
windows/unset-proxy.ps1   # removes it
```

Note the permanent option affects *every* process that respects these
env vars (git, npm, curl, etc.), not just AI tools - since only
`claude.ai`/`anthropic.com` are currently in ZeroBlock's tunneled domain
list, everything else through this proxy just goes direct anyway, so in
practice this is low-risk. Still, session-scoped is the more surgical
option if you want to be strict about only routing AI-tool traffic this
way.

### Browser

Uses a PAC (Proxy Auto-Config) script instead of a blanket system-wide
proxy, so only `claude.ai`/`anthropic.com`/`claude.com` traffic goes
through the LAN proxy - everything else (including corporate web apps)
goes direct.

The PAC file (`windows/homevpn-proxy.pac`) is hosted on the router itself
at `http://192.168.2.1/homevpn-proxy.pac` (served by uhttpd, already
deployed there) so it's reachable regardless of Check Point state and can
be updated in one place.

```powershell
windows/set-pac.ps1     # points Windows' automatic proxy config at it
windows/unset-pac.ps1   # rollback
```

Chrome/Edge pick this up via Windows' system proxy settings
automatically. If you add more domains later (e.g. `chatgpt.com`), edit
`windows/homevpn-proxy.pac` and re-upload it to `/www/homevpn-proxy.pac`
on the router - no client-side changes needed.

## Extending the domain list

Right now only `claude.ai`/`anthropic.com`-family domains are in
ZeroBlock's `opera` profile domain list, so only those get tunneled -
everything else through this proxy goes out directly (still bypasses
Check Point, just doesn't get VPN'd). To also tunnel ChatGPT, add
`chatgpt.com`, `openai.com` (and friends) to the `opera` profile's domain
list via ZeroBlock's LuCI page - a normal, persisted UI change, not
something this repo needs to manage.

## Maintenance

None, by design. Since the proxy rides ZeroBlock's live routing rather
than a copied VPN config, switching VPN servers/profiles in ZeroBlock's
UI does not require touching anything here.
