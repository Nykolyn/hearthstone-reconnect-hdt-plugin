# History across reconnects

HDT treats the returning game's `CREATE_GAME` as a game start. Its `GameEventHandler.HandleGameStart`
calls `GameV2.Reset()` and then `GameEvents.OnGameStart`, even when this is the same match.
The installed HDT 1.59.3 assembly and a local HDT log both confirm this sequence. HDT subsequently
logs `Joined after mulligan, assuming reconnect.`; that detection happens after history was cleared.

## Other implementations

- [haoruan/HDT-Reconnector: BattlegroundsBoardStat](https://github.com/haoruan/HDT-Reconnector/blob/main/HDT-Reconnector/BattlegroundsBoardStat.cs)
  copies the last-known-board dictionary before disconnecting and restores it afterwards.
  [ReconnectPanel](https://github.com/haoruan/HDT-Reconnector/blob/main/HDT-Reconnector/ReconnectPanel.xaml.cs)
  coordinates capture and restoration using the game start time and connection state.
  The older code uses string board keys; installed HDT 1.59.3 uses integer player IDs,
  so copying that implementation unchanged would not work.
- [Vaiz/HsReconnectTool](https://github.com/Vaiz/HsReconnectTool/blob/main/HsReconnectTool/DeckTrackerReconnectPlugin/PluginImpl.cs)
  provides a floating reconnect UI. The plugin integration examined contains no HDT board
  snapshot/restore logic.
- Current upstream HDT
  [GameEventHandler](https://github.com/HearthSim/Hearthstone-Deck-Tracker/blob/master/Hearthstone%20Deck%20Tracker/GameEventHandler.cs),
  [GameV2](https://github.com/HearthSim/Hearthstone-Deck-Tracker/blob/master/Hearthstone%20Deck%20Tracker/Hearthstone/GameV2.cs)
  and [BattlegroundsBoardState](https://github.com/HearthSim/Hearthstone-Deck-Tracker/blob/master/Hearthstone%20Deck%20Tracker/Hearthstone/BattlegroundsBoardState.cs)
  show the reset and board-cache behavior. The public tag `v1.59.3` was unavailable during
  research, so compatibility was checked against the locally installed assembly as well.
  During verification HDT updated to 1.59.4.9016; both plugins were also rebuilt and the
  history checks passed against that installation.

## What this fix preserves

My Reconnector 1.0.1 captures opponent board snapshots before its disconnect worker starts.
After HDT's game-start event, it restores missing snapshots if server address, port and game
handle still match. It keeps any boards HDT has already refreshed. Every entry point uses the
same path: overlay button, Ctrl+F12 and menu, including with the overlay disabled.

The snapshot is held only in memory. Failure, game end, menu entry, plugin unload, unknown or
different match identity, and captures older than two minutes prevent restoration. No HDT
game entities, power logs or game-start events are suppressed or replaced.

BG Build Advisor 0.1.3 separately recognizes repeated starts of the same match and retains its
manual build and automatic-selection memory. It still clears transient UI/board data, then reads
fresh entities from HDT. A real game end or different/unknown match clears the selection.
Other plugins need their own reconnect-aware lifecycle; a board cache cannot restore their
private state.

## Validation

Run the executable checks against your installed or portable HDT:

```powershell
dotnet build tests/ReconnectHistory.Tests/ReconnectHistory.Tests.csproj -c Release -p:HdtPath="<HDT directory>"
.\tests\ReconnectHistory.Tests\bin\Release\net48\ReconnectHistory.Tests.exe "<HDT directory>"
```

The tests use HDT's real board-state class without starting HDT or touching a game connection.
They cover reset/restore, consecutive reconnects, preserving refreshed boards, match mismatch,
unknown identity, expiry and unload. The advisor's existing test suite covers its session lifecycle.

Live acceptance check: record an opponent board, pin a build, reconnect twice through different
entry points, and check both survive. Finish the match and enter a new one: neither old board
history nor the old pinned build should carry over. Recheck after HDT updates: importing board
history uses a private HDT member because HDT has no public import API.
