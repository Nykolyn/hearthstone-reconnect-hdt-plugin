# How the Hearthstone Reconnect plugin works

This page covers the technical details behind the plugin: how it picks the connection to close,
how it gets past HDT's plugin blocklist, and why a naive reconnect takes 15 seconds.

- [The disconnect](#the-disconnect)
- [Never close the Battle.net connection (port 1119)](#never-close-the-battlenet-connection-port-1119)
- [HDT actively blocks reconnect plugins](#hdt-actively-blocks-reconnect-plugins)
- [The 15-second reconnect delay (reverse DNS)](#the-15-second-reconnect-delay-reverse-dns)
- [Why the button is its own window](#why-the-button-is-its-own-window)
- [Source layout](#source-layout)

## The disconnect

1. Find the `Hearthstone.exe` process(es).
2. List all established IPv4 TCP connections they own (`GetExtendedTcpTable`).
3. Pick **only the game-server connection**, in this order:
   1. the exact address from the last `Network.GotoGameServe()` line in
      `Logs\Hearthstone_<timestamp>\GameNetLogger.log`, if that connection is still open;
   2. any connection on the game-server port **3724**;
   3. any remaining connection that is not Battle.net or a web service.

   If none of these exist, the client is not in a match and nothing is closed.
4. Close the chosen connection by setting its state to `DELETE_TCB` via `SetTcpEntry`. That call
   needs administrator rights, which is why HDT has to run elevated.

The game shows *Reconnecting…* and rejoins the match in progress. Dropping the connection itself
takes about 30 ms.

The log line looks like this. The spelling is Blizzard's own: `GotoGameServe`, with no *r*:

```
I 22:38:26.8852240 Network.GotoGameServe() - address= 37.244.26.45:3724, game=7500, ...
```

The address lookup is cached and refreshed on a background timer, so pressing the button costs
no disk I/O. Reading the log on the click path would add its latency straight onto the reconnect.

## Never close the Battle.net connection (port 1119)

Hearthstone keeps several sockets open at once:

| Port | What | Closed? |
|---|---|---|
| 3724 | Game server | **yes**, this is the one |
| 1119 | Battle.net (Aurora) session | never |
| 443 / 80 | Shop, telemetry, CDN | never |

Killing 1119 does **not** make the rejoin faster. It logs the client out, so Hearthstone has to
redo the entire Battle.net login before it can even begin rejoining the match. The reconnect
looks slow and then usually dies on *reconnect failed*, with nothing but an Exit button. That is
why ports 1119, 443 and 80 are hard-excluded in `Reconnect.Disconnect`. Widening that list
brings the bug back.

## HDT actively blocks reconnect plugins

Starting with HDT 1.53.x, the `PluginManager` scans every plugin for prohibited native imports.
If it finds a static `DllImport` of `iphlpapi` (or `lovepapi`), it **refuses to load the entire
plugin list**. Every plugin disappears from *Options → Tracker → Plugins*, not just the
reconnector. It also refuses plugins whose name exactly matches `Reconnector`,
`HDT-Reconnector`, `HSReconnector`, `ReconnectPlugin`, and similar names.

This is why third-party reconnect plugins keep breaking after HDT updates. Version binding plays
a part, but the main cause is a deliberate blocklist that HDT extends over time. This plugin
works around it in two ways:

- **No `[DllImport("iphlpapi")]`.** `GetExtendedTcpTable` and `SetTcpEntry` are resolved at
  runtime with `LoadLibrary`/`GetProcAddress` (see `src/Shared/ReconnectCore.cs`). The only
  native modules in the assembly metadata are `kernel32` and `user32`, and neither is on the
  blocklist. The name `iphlpapi` appears only as a runtime string, never as an import. `build.ps1`
  fails the build if a static import ever sneaks back in.
- **Plugin name "My Reconnector"**, which is not an exact match on the name blocklist.

In HDT 1.56.x the `Refusing to load plugin:` path still exists, but the shipped binary no longer
contains the `iphlpapi`/`lovepapi` literals. The blocklist seems to have changed form or been
dropped. The workarounds cost nothing, so they stay.

If a future HDT update adds a new check (for example, blocking `LoadLibrary`, scanning for
`SetTcpEntry` by name, or adding "My Reconnector" to the list), the plugin will stop loading.
Look for `Refusing to load plugin` or an empty plugin list in
`%AppData%\HearthstoneDeckTracker\Logs\hdt_log.txt`, then adjust `ReconnectCore.cs` or the
plugin name. The current blocklist lives in HDT's `PluginManager.cs` on GitHub.

Beyond the blocklist, the plugin uses only a tiny, stable slice of the HDT API (`IPlugin`,
`Core.Game.IsRunning/IsInMenu`, `Config.Instance.HearthstoneDirectory`, `Log`), so API breaks
are rare. HDT's assembly is not strong-named, so a build against one HDT version loads in later
versions as long as those members still exist.

## The 15-second reconnect delay (reverse DNS)

If a reconnect still takes about 15 seconds after the disconnect, the time is spent **inside
Hearthstone**, and the cause is DNS:

```
I 18:29:46.870  RpcController.OnSocketError - Receive,ConnectionReset      <- we closed it
I 18:29:46.902  Network.GotoGameServe() - address= 37.244.26.83 reconnecting=True
I 18:29:46.902  TcpConnection - possible ip address: 37.244.26.83
W 18:30:02.497  Network.ProcessNetwork not called for 15s 600ms            <- stalled here
I 18:30:02.561  Network.OnGameServerConnectEvent() - Connected  ERROR_OK
```

The client resolves the server IP back to a name and blocks its own network pump until the lookup
finishes. Blizzard's game-server ranges have no reverse-DNS records, and resolvers commonly never
answer the query at all, so the client waits out the full Windows resolver timeout. Measured on
one test machine:

| Query | Time |
|---|---|
| Forward lookup, `www.google.com` | 265 ms |
| PTR for `8.8.8.8` / `1.1.1.1` | 33 ms |
| PTR for `37.244.26.83` (game server) | **15,624 ms** |

15,624 ms matches the client's own logged stall of 15.6 s, so it is the same wait. Two details
make it worse than it looks. **Windows does not cache a timed-out lookup**, so every connect
pays the full timeout again. And the same stall hits normal match joins too:
`reconnecting=False` connects measured 15.7 s.

`ReverseDnsPrimer` fixes this by adding stub entries to the Windows hosts file. The DNS client
answers reverse lookups from the hosts file without sending any query. The plugin does this
automatically the first time it sees a new range and writes a single marked block:

```
# BEGIN HsReconnector - reverse-DNS stubs
# range 37.244.24.0/22
37.244.24.0	hs-gs-37-244-24-0.hsreconnector.invalid
...
# END HsReconnector
```

The names live under `.invalid`, a TLD reserved so it can never resolve on the internet
([RFC 2606](https://www.rfc-editor.org/rfc/rfc2606)), so they cannot redirect any real domain.
The block holds at most four ranges and drops the oldest first. Only public unicast addresses
are accepted, so a tampered log file cannot make the plugin write entries for loopback or LAN
ranges.

### Seed whole /22s, not /24s

The fix works all-or-nothing per range, which shows up as "sometimes instant, sometimes 15 s".
Measured across one week of logs, after seeding two /24s:

| Server range | Seeded? | Connect time |
|---|---|---|
| `37.244.26.x` | yes | 0.06 – 0.09 s |
| `5.42.177.x` | yes | 0.08 – 0.13 s |
| `5.42.176.x` | **no** | **15.6 – 16.8 s** |

Blizzard draws each match's server from a block wider than a /24. The same account saw servers
in both `5.42.176.x` *and* `5.42.177.x`, so seeding only a /24 left the neighboring range
stalling. Ranges are therefore seeded a **/22** at a time (1024 entries), which also covers
addresses not seen yet. That matters because the client resolves a new server's address before
the plugin can react to it, so only a range that is already covered is fast on the very first
connect.

To apply or undo the fix without HDT, run PowerShell as administrator:

```powershell
powershell -ExecutionPolicy Bypass -File tools\prime-dns.ps1
powershell -ExecutionPolicy Bypass -File tools\prime-dns.ps1 -Remove
```

Antivirus products often block hosts-file writes. When that happens the plugin still reconnects
correctly, just slowly, and says so in HDT's log.

## Why the button is its own window

HDT's overlay window is click-through by design, because HDT manages its own mouse hook. A
control parented to `Core.OverlayCanvas` renders correctly but never receives hover or click
events. The button therefore lives in its own always-on-top window with two extended styles:

- `WS_EX_NOACTIVATE`: clicking does not steal focus from Hearthstone;
- `WS_EX_TOOLWINDOW`: keeps it out of Alt+Tab and the taskbar.

This also means changes to HDT's overlay between versions cannot silently break the button.

## Source layout

```
src/MyReconnectorPlugin/ReconnectorPlugin.cs   IPlugin entry point, HDT menu, cooldown
src/MyReconnectorPlugin/ReconnectWindow.cs     The RECONNECT button window
src/MyReconnectorPlugin/GlobalHotkey.cs        System-wide Ctrl+F12
src/MyReconnectorPlugin/PluginConfig.cs        %AppData%\MyReconnector\config.txt
src/Shared/ReconnectCore.cs                    TCP enumeration and the disconnect
src/Shared/GameServerLocator.cs                Finds the game server in Hearthstone's log
src/Shared/ReverseDnsPrimer.cs                 Removes the 15 s reverse-DNS stall
tools/prime-dns.ps1                            Apply or undo the hosts-file fix by hand
build.ps1                                      Build, check, package, install
```

`src/Shared` is also compiled into the standalone app,
[HS Reconnector](https://github.com/Nykolyn/hearthstone-reconnect-tool), so keep it free of HDT
dependencies and keep the two copies in sync.
