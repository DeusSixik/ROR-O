# ROR-O

Performance optimization mod for Risk of Rain 2.

`ROR-O` focuses on real hot paths found in profiler captures instead of broad "magic FPS boost" claims. The mod reduces repeated UI work, trims heavy runtime systems when they are offscreen, and moves Steam traffic off the host frame.

## What it optimizes

Current optimization areas include:

- Lobby and menu refresh paths
  - player list refreshes
  - social icon / avatar update paths
  - survivor availability checks
- Text / UI hot paths
  - repeated `TextMeshPro` text updates
  - chat, inventory, and scoreboard rebuilds that show the same result
  - UI raycasts while the pointer stays still
  - repeated text style changes
- Damage numbers
  - load shedding under extreme combat load
  - quality presets: Lossless, Performance, Potato
- Dynamic bones
  - throttles offscreen and distant rigs
  - simulates busy frames on worker threads
  - quality presets: Lossless, Performance, Potato
- Generic IK (`GenericIK.dll`)
  - throttles offscreen IK rigs
  - quality presets: Lossless, Performance, Potato
- Steam networking
  - packet send and receive off the main thread
- Spawn
  - warms monster and pickup prefabs at stage start
- Cache-based gameplay hot path cleanup
  - `ChildLocator` / transform lookup caching
  - component access caching for selected hot loops
  - survivor unlock cache
  - morgue / run history cache

## Why this mod exists

The mod was built by profiling real gameplay and fixing expensive paths one by one. During testing, the biggest wins came from:

- reducing repeated UI rebuild work
- reducing offscreen / far-away animation overhead
- keeping Steam polling off the host frame
- reducing waste from repeated menu and lobby refresh logic

It is especially aimed at:

- modded runs with lots of UI activity
- scenes with many enemies or effects
- long runs where animation systems and UI become expensive
- multiplayer, where the host was spending frame time on Steam

## Important notes

- This mod does **not** promise to fix every stutter source.
- Risk of Rain 2 still has expensive systems such as Addressables loading, spawning, and audio registration that can create spikes on their own.
- Warming a prefab does not draw it, so the first time that monster appears on screen can still hitch.

## Installation

### Thunderstore / Mod Manager

Install `ROR-O` into a profile that already has BepInEx. BepInExPack is not required.

### Manual installation

Place `ROR-O.dll` into:

```text
BepInEx/plugins/ror-o/
```

## Configuration

The config file is created here:

```text
BepInEx/config/net.sixik.plugin.roro.cfg
```

Main sections currently exposed:

- `[Damage Numbers]`
- `[Dynamic Bones]`
- `[Generic IK]`
- `[Network]`
- `[Spawns]`
- `[Profiler]`

`[Profiler]` includes an optional Hitch Watch and a switch for the separately installed Mono profiler (Dev builds only).

## Integrations

- Looking Glass
- Risk of Options
- Mono Profiler (Dev builds only)

## Development

Close the game before building. From the repository root:

```text
dotnet build ROR-O/ROR-O.csproj -c Debug
```

The build finds the Steam install and profiles from r2modman, Thunderstore Mod Manager, or Gale. The first success writes gitignored `ROR-O/roro.user.props` and later builds reuse it. The profile used to compile must contain `LookingGlass.dll`. Players do not need that mod.

If several profiles are found:

```text
dotnet build ROR-O/ROR-O.csproj -c Debug -p:ProfileDir="D:\profiles\my-profile"
```

A unique profile name is enough. Profiles outside AppData need `ProfileRoot`, or a copy of `ROR-O/roro.user.props.example`. Interactive choice: `powershell -File ROR-O/resolve-paths.ps1 -Ask`.

Debug copies `ROR-O.dll` into the profile. Release also writes `ROR-O/bin/Release/netstandard2.1/ROR_O-v` plus the version. The canonical version is `mod_version` in `version.properties`; the build uses it for the plugin metadata, assembly metadata, ZIP name, and manifest inside the ZIP. Use `-p:DeployOnBuild=false` to skip the copy.

Changing `version.properties` on `main` runs GitHub Actions on a self-hosted Windows runner. If the matching `v` tag does not exist, the workflow builds the mod, creates the tag, and publishes a GitHub Release of that version. The runner finds the game and profile on its own machine. A release can also be started manually from the Actions tab.

## Known issues

- **SeekersPatcher.** Both mods patch the same game code. Together they can break those methods.
- **Content mods.** Added survivors, monsters, items, and UI can hit patched methods in untested ways.
- **Stage changes.** Prefab warming can freeze the game while the next stage loads.
- **Profilers.** Hitch Watch and the Mono profiler both miss part of a frame. One has no managed call trees. The other has no per-frame tail. Running both adds profiler overhead to the times.
- **Allocations in the patches.** Some patches still allocate (lists, arrays, strings) on the frames they are meant to make cheaper (report pls). 
- **Game updates and stolen bytes.** A game update may takes a run of bytes from a method and replaces them with a jump. After an update that run can end mid-instruction and pull in neighboring bytes, inside the method or from the next one. The patch then runs those bytes.

## Credits

### Code

- **DeusSixik** — main developer. Review, and guy how know the code runs)))
- **Krupter** (Ксения Мазерати) — bugs, profilers, and debugging.

### Testing

- **Дарья Литовская** — plays almost every day to help debug. Several hundred hours...
- **Solderyn** — runs the measurements
- **Arisa** — playtesting

## Contributing

Issues and merge requests are welcome. People who help that way are listed here.

- Your name. Open an issue or a merge request.

## Source

GitHub repository:

- `https://github.com/DeusSixik/ROR-O`

## License

See `LICENSE.txt`.

## Support US
- [Boosty](https://boosty.to/sixik)
