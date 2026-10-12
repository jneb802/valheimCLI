# ValheimCLI extension example

`ReloadProbe` is a game-side extension example and remains with the ValheimCLI API it exercises.

The external test drivers (`ReloadCheck`, `TerrainCheck`, `ClientSurfaceCheck`, `GameObserve`, `NoGameTerrain`) moved to [ValheimTesting](https://github.com/tvongaza/ValheimTesting). They consume the ValheimCLI package and do not belong in ordinary game plugin installations.

## dev-loop, pin-mods and log-summary moved to ValheimTesting

The developer-loop scripts (`dev-loop.sh`/`dev-loop.ps1`, `pin-mods.sh`/`pin-mods.ps1` and `log-summary.sh`) moved to [ValheimTesting's `tools/dev-loop`](https://github.com/tvongaza/ValheimTesting/tree/main/tools/dev-loop) on 28 September 2026, with their tests. ValheimCLI keeps the `valheim-cli` executable they drive and `smoke-plan.yaml`, a plan to start from. The scripts that remain in this folder are bash only.
