# ROR-O

ROR-O is a performance-focused mod for Risk of Rain 2. It reduces unnecessary work across the UI, damage numbers, character animation, effects, gameplay systems, and multiplayer networking.

The mod focuses on avoiding repeated updates, reusing existing data, and skipping work that is not currently needed.

## UI and Menus

- Reduces unnecessary refreshes of lobby lists, player icons, and survivor availability checks.
- Reuses downloaded Steam avatars instead of repeatedly rebuilding their textures.
- Avoids rebuilding chat history and inventory displays when their contents have not changed.
- Reduces repeated setup work when updating scoreboard entries.
- With Looking Glass installed, reuses previously generated item descriptions and avoids rewriting buff timer text when its displayed value has not changed.

## Damage Numbers

In crowded fights, ROR-O adaptively reduces the number of ordinary damage numbers being spawned.

Important damage indicators are preserved by default, including critical hits, healing, weak-point hits, sniper hits, luminous hits, and knockback hits. The most aggressive profile can also reduce the number of critical damage indicators.

Already spawned damage numbers continue animating normally.

## Animation and IK

- Dynamic Bone simulation runs less frequently for distant or off-screen characters.
- Recently off-screen characters retain normal update frequency briefly before reduced updates begin.
- Some Dynamic Bone simulation work can be processed through background jobs.
- Generic IK updates less frequently while its character is off-screen and returns to normal updates when visible again.

These optimizations preserve normal update behavior for characters that are visible and close to the camera.

## Gameplay and Effects

- Monster assets, including boss assets, are prepared ahead of time across multiple frames instead of doing all preparation at once.
- Distant or off-screen effects can have their secondary particles and optional lights disabled.
- The main effect continues running; only its optional visual components are affected.
- Several effect-spawning operations reuse existing data and references to avoid unnecessary repeated work.

Effect prewarming and additional pool-retention options are available but are not enabled in a fresh configuration by default.

## Networking

ROR-O reduces repeated work when preparing character and projectile position updates. Network snapshots are prepared when an update is due instead of being rebuilt unnecessarily on every physics step.

For client-side Steam networking, packet sending and receiving can run in a background thread. Packet processing and game-side handling remain on the main thread. The Steam game-server connection continues to use the main thread.

If the background thread cannot be started or stops working, client Steam networking falls back to the main thread.

## Additional Optimizations

ROR-O also reduces repeated component lookups, hierarchy searches, and UI updates across various gameplay systems.

Previously retrieved references and unchanged data can be reused instead of being searched for or rebuilt repeatedly.

## Performance

<!-- Future frame-time performance graphs will be added here. -->

## Compatibility

Mods that add or modify survivors, monsters, items, effects, or UI elements may interact with ROR-O's optimizations in ways that have not been tested.

## Installation

Install ROR-O with a mod manager into a profile that already has BepInEx.

BepInExPack is not required.

For manual installation, place `ROR-O.dll` in:

```text
BepInEx/plugins/ror-o/
```

## Configuration

Settings are saved in:

`BepInEx/config/net.sixik.plugin.roro.cfg`

Configuration sections include General, Damage Numbers, Dynamic Bones, Generic IK, Spawns, Effects, Network, and Profiler.

Several optimization categories provide presets:

- **Lossless** — prioritizes preserving the original behavior.
- **Performance** — balanced performance-oriented settings.
- **Potato** — more aggressive optimizations, which may reduce visual updates.
- **Custom** — allows individual settings to be configured.

A fresh configuration uses Performance for Damage Numbers, Dynamic Bones, Generic IK, and Spawns. Effects uses Custom, with secondary effect culling enabled and effect prewarming and pool-retention options disabled.

Some additional options are experimental and disabled by default.

If Risk of Options is installed, ROR-O settings are available through its configuration menu.

## Credits

### Code
- **DeusSixik** — main developer. Review, and guy how know the code runs)))
- **Krupter** (Ксения Мазерати) — bugs, profilers, and debugging.

### Testing
- **Дарья Литовская** — plays almost every day to help debug. Several hundred hours...
- **Solderyn** — runs the measurements
- **Arisa** — playtesting

### Art
- **Samuel** — Aboba-art

## Contributing

Issues and merge requests are welcome. People who help that way are listed here.

- Your name. Open an issue or a merge request.

## Source

[GitHub Repository](https://github.com/DeusSixik/ROR-O)

Build instructions are available in [DEVELOPMENT.md](https://github.com/DeusSixik/ROR-O/blob/main/DEVELOPMENT.md).

## License

GNU General Public License v3. See `LICENSE.txt`.

## Support

[Boosty](https://boosty.to/sixik)