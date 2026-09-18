# Security

The plugin runs inside Hearthstone Deck Tracker, which has to be started **as administrator**.
Code with that much access should be easy to audit, so this page lists everything it does.

## What the plugin does with administrator rights

| Action | Scope |
|---|---|
| Closes a TCP connection (`SetTcpEntry`, `DELETE_TCB`) | Only connections owned by `Hearthstone.exe`, and never Battle.net (1119) or web ports (443/80). |
| Edits the hosts file | Only inside its own `# BEGIN HsReconnector` / `# END HsReconnector` block. Entries are stub names under the reserved `.invalid` TLD for public game-server ranges, at most four /22 ranges. The original file is backed up once to `hosts.hsreconnector.bak`. |
| Runs `ipconfig /flushdns` | Started by its full System32 path, right after a hosts change. |

## What it reads and writes as your user

- Reads the tail of Hearthstone's `GameNetLogger.log` / `Hearthstone.log` to find the game-server
  address.
- Reads the Hearthstone install location from HDT's settings, the running process, or the
  uninstall registry key.
- Reads and writes `%AppData%\MyReconnector\config.txt`, which holds the button position and
  on/off switches.

## What it never does

- No network requests, telemetry, auto-update, or downloads.
- No access to Battle.net credentials, HDT account data, or any file outside the list above.
- No code loaded from user-writable locations: `iphlpapi.dll` and `ipconfig.exe` are resolved
  from System32 only.

## Verifying a release

Every release is built by GitHub Actions from the tagged commit. Compilation is deterministic
(`Deterministic` + `PathMap`), so the DLL carries no machine-specific paths. Reproducing it byte
for byte also requires the same .NET SDK and HDT versions as the CI run, both shown in the
workflow log. Each release ships `SHA256SUMS.txt`:

```powershell
Get-FileHash .\MyReconnectorPlugin-vX.Y.Z.zip -Algorithm SHA256
```

To trust nothing but the source, build it yourself with `build.ps1`.

## Supported versions

Only the latest release gets fixes.

## Reporting a vulnerability

Please **do not** open a public issue. Use GitHub's private vulnerability reporting instead:
*Security → Report a vulnerability* on this repository. Include the plugin and HDT versions and
steps to reproduce. Expect an acknowledgement within a week.
