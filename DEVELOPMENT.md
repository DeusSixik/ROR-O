# Building ROR-O

Close the game when the plugin file in the profile is in use. From the repository root:

```text
dotnet build ROR-O/ROR-O.csproj -c Debug
```

The build looks for a Steam install, and for profiles under r2modman and Thunderstore Mod Manager. It also reads Gale config files for profile paths. The first success writes gitignored `ROR-O/roro.user.props`, and later builds reuse it. The profile used to compile must contain `LookingGlass.dll`. Looking Glass is optional in a player profile.

When several profiles are found:

```text
dotnet build ROR-O/ROR-O.csproj -c Debug -p:ProfileDir="D:\profiles\my-profile"
```

A unique profile name is enough. Profiles outside AppData need `ProfileRoot`, or a copy of `ROR-O/roro.user.props.example`. Interactive choice:

```text
powershell -File ROR-O/resolve-paths.ps1 -Ask
```

A Debug build copies `ROR-O.dll` into the profile. If a copy is already under `BepInEx/plugins`, that folder is updated. Otherwise the copy goes to `BepInEx/plugins/ROR-O`. A Release build also writes `ROR-O/bin/Release/netstandard2.1/ROR_O-v` plus the version. The version is `mod_version` in `version.properties`. The build uses it for the plugin, the assembly, the ZIP name, and the manifest inside the ZIP. `manifest.json` in the repository is the template. Use `-p:DeployOnBuild=false` to skip the copy.

Put the compile-time DLLs in `otherMods/game/`, `otherMods/unity/`, `otherMods/wwise/`, and `otherMods/bepinex/`, and keep `LookingGlass.dll` directly under `otherMods/`. With `RoR2.dll`, `Assembly-CSharp.dll`, `BepInEx.dll`, and `0Harmony.dll` in that bundle, the project builds from the bundle alone. If the bundle is incomplete, the build uses the Steam and profile search above.

Pushing a `version.properties` change to `main` runs GitHub Actions on a GitHub-hosted Windows runner. If the matching `v` tag does not exist, the workflow checks the DLL bundle, builds the mod, creates the tag, and publishes a GitHub Release. A release can also be started from the Actions tab.

## Contributing

Issues and pull requests are welcome. People who help that way are listed in the README.

- Your name. Open an issue or a pull request.
