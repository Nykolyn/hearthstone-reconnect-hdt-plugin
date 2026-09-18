# Hearthstone Reconnect Plugin for HDT (Hearthstone Deck Tracker)

[![Build](https://github.com/Nykolyn/hearthstone-reconnect-hdt-plugin/actions/workflows/build.yml/badge.svg)](https://github.com/Nykolyn/hearthstone-reconnect-hdt-plugin/actions/workflows/build.yml)
![HDT](https://img.shields.io/badge/HDT-1.57-orange)
![Platform](https://img.shields.io/badge/platform-Windows-blue)
![License](https://img.shields.io/badge/license-MIT-green)

A **Hearthstone Deck Tracker plugin** that adds a **RECONNECT button** and a **Ctrl+F12 hotkey**.
One click drops Hearthstone's connection to the game server. The client immediately rejoins the
match in progress, which makes it the quickest way to **skip Battlegrounds combat animations**.

Unlike older reconnector plugins, this one:

- **closes only the game-server connection.** It never touches the Battle.net session, so you
  don't get the slow re-login and the "reconnect failed" screen;
- **removes the ~15-second reconnect delay** caused by a reverse-DNS lookup (details in
  [How it works](docs/how-it-works.md#the-15-second-reconnect-delay-reverse-dns));
- **loads on current HDT versions.** HDT refuses to load plugins that import `iphlpapi`
  directly, and this plugin avoids that import.

<!-- Screenshot: add docs/images/overlay-button.png and uncomment
![RECONNECT button over Hearthstone Battlegrounds](docs/images/overlay-button.png)
-->

## Features

| | |
|---|---|
| **RECONNECT overlay button** | Floats over the game during a match and hides in menus. Draggable; its position is saved relative to the game window, so it survives resolution changes. |
| **Global hotkey Ctrl+F12** | Works while Hearthstone has focus. You can turn it off. |
| **HDT menu** | *Plugins → My Reconnector*: show or hide the button, unlock it to drag, toggle the hotkey, *Reconnect now*. |
| **Game-server targeting** | Reads the current game server from Hearthstone's own network log. Battle.net (1119) and web ports (443/80) are never closed. |
| **Fast rejoin** | Adds reverse-DNS stubs to the hosts file automatically, so a reconnect takes well under a second instead of about 15 seconds. |
| **Crash guard** | One shared 4-second cooldown across the button, the hotkey and the menu. Reconnecting twice in quick succession can crash the game. |

## Requirements

- Windows 10 or 11
- [Hearthstone Deck Tracker](https://hsreplay.net/downloads/) 1.56 or newer (built against 1.57.12)
- HDT started **as administrator**. Windows only lets an elevated process close another
  process's TCP connection.

## Installation

1. Download `MyReconnectorPlugin-vX.Y.Z.zip` from the
   [latest release](https://github.com/Nykolyn/hearthstone-reconnect-hdt-plugin/releases/latest).
2. In HDT open **Options → Tracker → Plugins → Plugins Folder**, and extract the zip there. You
   end up with `Plugins\MyReconnector\MyReconnectorPlugin.dll`.
3. Restart HDT **as administrator**. To make that permanent: right-click the HDT shortcut →
   *Properties → Compatibility → Run this program as an administrator*.
4. Go to **Options → Tracker → Plugins** and enable **My Reconnector**.

Start a match. The **RECONNECT** button appears over the game, and **Ctrl+F12** works as well.

> To verify the download, compare its hash with `SHA256SUMS.txt` from the same release:
> `Get-FileHash .\MyReconnectorPlugin-vX.Y.Z.zip`

## Usage

- **Skip a Battlegrounds fight:** once combat starts, press the button or Ctrl+F12. The game shows
  *Reconnecting…* and comes back at the end of combat.
- **Move the button:** *Plugins → My Reconnector → Unlock button position*, drag it, then untick
  the option.
- **Log:** everything the plugin does is written to HDT's log with the prefix `My Reconnector:`
  (`%AppData%\HearthstoneDeckTracker\Logs\hdt_log.txt`).

## Troubleshooting

| Symptom | Fix |
|---|---|
| Button shows **RUN HDT AS ADMIN** | Restart HDT as administrator (see Installation, step 3). |
| Plugin missing from the plugin list, or the whole list is empty | HDT's plugin blocklist changed. Look for `Refusing to load plugin` in `hdt_log.txt` and [open an issue](../../issues). |
| Hotkey shows *(unavailable, in use)* | Another program already registered Ctrl+F12, often the standalone reconnect app. Close it, or use the button. |
| Reconnect works but takes ~15 s | The hosts-file write was blocked, usually by an antivirus. HDT's log says so. Allow it, or run [`tools/prime-dns.ps1`](tools/prime-dns.ps1) as administrator. |
| **No game-server connection, are you in a match?** | You pressed it outside a match. Nothing was closed. |
| Game crashes after several reconnects in one match | A Hearthstone bug. Give it a few seconds between reconnects. |

## How it works

In short, the plugin:

1. finds Hearthstone's game-server address in `GameNetLogger.log`;
2. closes that one TCP connection through the Windows `SetTcpEntry` API (`DELETE_TCB`);
3. lets Hearthstone rejoin the match on its own.

The full write-up covers HDT's plugin blocklist, why port 1119 must never be closed, and where
the 15-second delay comes from: **[docs/how-it-works.md](docs/how-it-works.md)**.

## What it changes on your PC

The plugin runs with administrator rights, so here is everything it touches:

- **TCP connections**: only the game-server connection owned by `Hearthstone.exe`.
- **hosts file**: a single block marked `# BEGIN HsReconnector` / `# END HsReconnector` with
  stub names under the reserved `.invalid` domain. The original file is backed up once to
  `hosts.hsreconnector.bak`. Undo it with `tools/prime-dns.ps1 -Remove`, or delete the block.
- **Settings**: `%AppData%\MyReconnector\config.txt`, which holds the button position and toggles.

It sends nothing over the network, has no telemetry, and reads nothing beyond Hearthstone's own
log files. See [SECURITY.md](SECURITY.md).

## Building from source

Requires the [.NET SDK](https://dotnet.microsoft.com/download) (8 or newer) and an installed HDT.

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

`build.ps1` compiles the plugin against the newest HDT in `%LocalAppData%\HearthstoneDeckTracker`,
checks the binary, and installs it into HDT's Plugins folder. Options:

| Flag | Effect |
|---|---|
| `-HdtPath <dir>` | Build against a specific HDT folder, for example an extracted portable HDT. |
| `-SkipInstall` | Build only. |
| `-Package` | Also create the release zip and `SHA256SUMS.txt` in `artifacts\`. |

After an HDT update breaks the plugin, rebuilding with this command is usually all it takes.

## Versioning and releases

The project follows [Semantic Versioning](https://semver.org/). The version is defined once, in
[`Directory.Build.props`](Directory.Build.props). Changes are listed in [CHANGELOG.md](CHANGELOG.md).

To publish a release:

1. Bump `<Version>` in `Directory.Build.props` and move the *Unreleased* notes in `CHANGELOG.md`
   under the new version.
2. Commit, then tag: `git tag v1.2.3 && git push origin v1.2.3`.
3. CI builds against the latest HDT release. It checks that the tag matches `<Version>` and
   publishes a GitHub Release with the DLL, the zip and the SHA-256 checksums.

## Related

- [HS Reconnector](https://github.com/Nykolyn/hearthstone-reconnect-tool): the same reconnect as a
  standalone Windows app, no HDT needed.
- [HDT-Reconnector](https://github.com/haoruan/HDT-Reconnector) and
  [HsReconnectTool](https://github.com/Vaiz/HsReconnectTool) use the same `SetTcpEntry` technique.
- [Hearthstone Deck Tracker](https://github.com/HearthSim/Hearthstone-Deck-Tracker)

## Disclaimer

Use at your own risk. Forcing a disconnect sits in a gray area of Blizzard's Terms of Service.
This project is not affiliated with or endorsed by Blizzard Entertainment or HearthSim.
Hearthstone® is a trademark of Blizzard Entertainment, Inc.

## License

[MIT](LICENSE)
