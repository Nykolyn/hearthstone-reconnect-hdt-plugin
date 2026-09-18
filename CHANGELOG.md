# Changelog

All notable changes to this project are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project
uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.0.0] - 2026-09-19

First release as a standalone repository.

### Added
- **RECONNECT button** in its own always-on-top window over Hearthstone. It is clickable, doesn't
  steal focus, hides outside matches, and can be dragged; its position is stored relative to the
  game window.
- **Global Ctrl+F12 hotkey** that works while Hearthstone has focus.
- HDT menu (*Plugins → My Reconnector*): toggle the button, unlock or drag it, toggle the
  hotkey, *Reconnect now*.
- **Game-server-only disconnect**: the target is read from `GameNetLogger.log`, falling back
  to port 3724. Battle.net (1119) and web ports (443/80) are never closed.
- **Automatic reverse-DNS priming** in the hosts file (/22 ranges, `.invalid` stub names). It
  removes the ~15 s stall on every reconnect and match join.
- One 4-second cooldown shared by every trigger, guarding against the double-reconnect crash.
- Compatible with HDT's reconnect-plugin blocklist: no static `iphlpapi` import.
- `tools/prime-dns.ps1` to apply or remove the hosts-file fix without HDT.
- `build.ps1` checks each binary for a static `iphlpapi` import and embedded local paths, and
  can package a release zip with SHA-256 checksums.
- CI: every push is built against the latest HDT release, and `v*` tags publish a GitHub Release.

### Security
- `iphlpapi.dll` and `ipconfig.exe` are loaded only by their full System32 path. A bare name
  would be searched in HDT's user-writable folder first, which is a privilege-escalation risk for
  code running as administrator.
- Hosts-file priming accepts only public unicast ranges, so a tampered Hearthstone log cannot
  steer it into loopback or LAN ranges.
- Reproducible builds: `PathMap` and deterministic compilation keep the build machine's paths
  out of the DLL.

### Fixed
- A disconnect could report "no active connections" when the TCP table grew between the size
  query and the read. The read is now retried.
- Process handles are released after each lookup.

[Unreleased]: https://github.com/Nykolyn/hearthstone-reconnect-hdt-plugin/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/Nykolyn/hearthstone-reconnect-hdt-plugin/releases/tag/v1.0.0
