# ROR-O

Performance optimization mod for Risk of Rain 2.

`ROR-O` focuses on real hot paths found in Mono profiler captures instead of broad "magic FPS boost" claims. The mod reduces repeated UI work, trims heavy runtime systems when they are offscreen, and smooths out several expensive gameplay update loops.

## What it optimizes

Current optimization areas include:

- Lobby and menu refresh paths
  - player list refreshes
  - social icon / avatar update paths
  - survivor availability checks
- Text / UI hot paths
  - repeated `TextMeshPro` text updates
  - repeated `font`, `fontSize`, `alignment`, `alpha`, `maxVisibleCharacters`
  - repeated `SetVerticesDirty`, `SetLayoutDirty`, `SetMaterialDirty`
  - repeated `UISkinData.TextStyle.Apply(...)`
- Damage numbers
  - load shedding under extreme combat load
  - keeps important hits more visible through crit/color/peak-damage rules
- Dynamic bones
  - throttles offscreen rigs
  - throttles far visible rigs more conservatively
- Generic IK (`GenericIK.dll`)
  - throttles offscreen IK rigs with grace frames
- Cache-based gameplay hot path cleanup
  - `ChildLocator` / transform lookup caching
  - component access caching for selected hot loops
  - survivor unlock cache
  - morgue / run history cache

## Why this mod exists

The mod was built by profiling real gameplay and fixing expensive paths one by one. During testing, the biggest wins came from:

- reducing repeated UI rebuild work
- reducing offscreen / far-away animation overhead
- reducing waste from repeated menu and lobby refresh logic

It is especially aimed at:

- modded runs with lots of UI activity
- scenes with many enemies or effects
- long runs where animation systems and UI become expensive

## Important notes

- This mod does **not** promise to fix every stutter source.
- Risk of Rain 2 still has expensive systems such as networking, Addressables loading, spawning, and audio registration that can create spikes on their own.
- Enemy spawning itself does **not** appear to use a universal reusable entity pool, so occasional spawn hitches can still happen.

## Installation

### Thunderstore / Mod Manager

Install with:

- `BepInExPack`
- then install `ROR-O`

### Manual installation

1. Install BepInEx for Risk of Rain 2
2. Build or download `ROR-O.dll`
3. Place it into:

```text
BepInEx/plugins/
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

## Damage Numbers presets

### Balance

```ini
[Damage Numbers]
Enable Load Shedding = true
Soft Particle Cap = 112
Hard Particle Cap = 176
Absolute Particle Cap = 240
Soft Spawns Per Frame = 20
Hard Spawns Per Frame = 34
Absolute Spawns Per Frame = 48
Soft Peak Damage Fraction = 0.025
Hard Peak Damage Fraction = 0.07
Absolute Peak Damage Fraction = 0.14
Soft Minimum Damage = 1
Hard Minimum Damage = 3
Absolute Minimum Damage = 6
Peak Damage Half Life Seconds = 1.75
Preserve Critical Hits = true
Preserve Important Colors = true
```

### Performance

```ini
[Damage Numbers]
Enable Load Shedding = true
Soft Particle Cap = 80
Hard Particle Cap = 128
Absolute Particle Cap = 176
Soft Spawns Per Frame = 12
Hard Spawns Per Frame = 22
Absolute Spawns Per Frame = 32
Soft Peak Damage Fraction = 0.04
Hard Peak Damage Fraction = 0.10
Absolute Peak Damage Fraction = 0.20
Soft Minimum Damage = 2
Hard Minimum Damage = 5
Absolute Minimum Damage = 10
Peak Damage Half Life Seconds = 1.25
Preserve Critical Hits = true
Preserve Important Colors = true
```

## Dynamic Bones defaults

```ini
[Dynamic Bones]
Enable Invisible Update Throttling = true
Invisible Update Interval = 3
Recently Visible Grace Frames = 12
Visible Mid Distance = 30
Visible Far Distance = 55
Visible Mid Update Interval = 2
Visible Far Update Interval = 3
```

## Generic IK defaults

```ini
[Generic IK]
Enable Invisible Update Throttling = true
Invisible Update Interval = 2
Recently Visible Grace Frames = 8
```

## Compatibility

- Built for BepInEx
- Harmony-based runtime patches
- Includes compatibility-focused optimization for `LookingGlass`
- Designed to coexist with other gameplay mods, but conflicts are still possible if another mod patches the same hot methods aggressively

## Development approach

The optimization work was added incrementally across commits such as:

- setup and initial patching infrastructure
- LookingGlass compatibility optimization
- hot path caching passes
- menu / lobby / text optimizations
- damage number optimization
- Dynamic Bone optimization
- Generic IK optimization

This means the mod is intentionally a collection of targeted fixes rather than one giant rewrite.

## Known limitations

- Loading spikes related to Addressables can still happen
- Spawn spikes can still happen when the game instantiates new enemies or world objects
- Network-heavy runs can still be limited by Steam / UNet message handling
- Audio-heavy scenes can still be limited by `AkGameObj` / Wwise costs

## Source

GitHub repository:

- `https://github.com/DeusSixik/ROR-O`

## License

See `LICENSE.txt`.
