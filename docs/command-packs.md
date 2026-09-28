# Core and optional command packs

The game-side additions are **command packs** (extensions), not AI skills. Core
stays loaded once; ordinary BepInEx or ScriptEngine loads the optional packs.
There is no second DLL loader or runtime compiler.

| Install | Owns | Console commands |
|---|---|---:|
| `valheimCLI.dll` core 1.1 | Socket/broker, main-thread dispatch, permissions, expectations, build/reload diagnostics, async completion, shared operation gate and extension ownership | 8 |
| `Valheim.Cli.Standard.dll` | Characters/joins, player/item/actor actions, building/carts, routes, screenshots/map exports, async waits/save and existing general gameplay helpers | 106 |
| `Valheim.Cli.WorldTools.dll` | World/ZDO/container census, terrain/rock inspection and actions, structured terrain/collider/player-support observations | 14 |
| `Valheim.Cli.Reflection.dll` | Optional `cli_call` reflection over game and mod members | 1 |
| `Valheim.Cli.Capture.dll` | Reversible grass/clutter visibility override | 1 |

All 130 existing console command names are preserved, exactly once. Core alone
intentionally does not expose gameplay commands. The Standard pack is large
because session, actor and capture actions share existing gameplay helpers;
these remain one optional assembly to avoid cross-pack static dependencies.
Further splitting it should follow actual independent consumers, not duplicate
helpers or introduce dependencies between ScriptEngine-loaded assemblies.

MWL port commands stay in MWL's optional adapter; Roads test commands stay in
Roads' adapter. ValheimTesting is an external library/repository that uses the
ValheimCLI transport package. None is a core or ordinary production-mod dependency.

## Install or upgrade

Stop the test game. Keep one core DLL in `BepInEx/plugins`. To retain the complete
prior command set, install all four pack DLLs beside it. Copy only the named
DLLs, not build output directories containing references. Embedded symbols are
included. Packs require ValheimCLI core 1.1 or newer; do not mix them with the old
monolithic core, whose commands would collide.

For development, put a pack in an owned `BepInEx/scripts` directory instead of
plugins and let ScriptEngine replace it. Never put the same pack in both places.
ScriptEngine reloads **all** scripts in that directory when any file changes.
Keep the core outside scripts and restart to replace core/API itself. Replacing
core underneath active packs is unsupported; no managed assembly unloading or
memory reclamation is promised.

Update strict expectations to include the actual installed pack GUIDs and hashes:
`valheimCLI.standard`, `valheimCLI.worldtools`, `valheimCLI.capture`, `valheimCLI.reflection`. No expectations
are disabled automatically. Old scripts using a missing pack should fail setup
by checking its capability, rather than relying on an unknown command's text.

`cli_extensions` lists owners, fresh instance tokens, closing state, active work
and cleanup errors. `cli_extension cli.standard/commands` (likewise
`cli.worldtools/commands`, `cli.capture/commands` and `cli.reflection/commands`) lists compatible aliases.
`valheim.world/terrain`, `terrain-surface`, and `player-support` now require World
Tools. Their names and result schemas remain unchanged.

## Ownership and access

Console packs register through `ConsoleModuleHost` and the same
`ExtensionRegistry` as typed adapters. Registration is synchronous on the game
thread. A command collision or thrown registration rolls back the whole console
registration, preserving earlier owners. Unload removes only command objects
this pack owns; a later replacement under the same name is not removed or granted
its predecessor's client permission. Name prefixes never establish authority.

Main-menu compatibility dispatch now checks the registered command's normal
console validity before calling its handler. It cannot bypass permissions by
calling a retained delegate after an alias was replaced. Existing handlers retain
their documented role/argument/world checks. The trusted-console entry point is
unchanged. Native achievement/cheat confirmation remains a separate game gate.

Retiring a pack removes aliases immediately. Tracked asynchronous work retains
its owner until it settles; old and new instances cannot overlap under that ID.
A request waiting to start sees cancellation, while an already-issued teleport or
screenshot can settle through the existing async logic before releasing the shared
gate. Route controls stop on retirement; Capture restores its original clutter
flags. A thrown cleanup blocks replacement and is visible in discovery. Arbitrary
spawn/save/terrain effects cannot be rolled back. Some existing fire/walk actions
may finish their bounded routines while draining; removal is not undo.

New structured adapters should use `ExtensionCommand` / `ExtensionContext`.
`ConsoleModuleHost` is the compatibility layer for established console handlers,
not a second transport or permission system. Pack coroutines must be started
through their owner, not on the plugin instance directly.

## Build and local checks

```sh
dotnet test Tests/RequestBroker.Tests/RequestBroker.Tests.csproj -c Release
python3 scripts/check-command-packs.py
# These use YOUR existing game assembly references in Environment.props:
dotnet build Packs/Standard/Valheim.Cli.Standard.csproj -c Release
dotnet build Packs/WorldTools/Valheim.Cli.WorldTools.csproj -c Release
dotnet build Packs/Capture/Valheim.Cli.Capture.csproj -c Release
dotnet build Packs/Reflection/Valheim.Cli.Reflection.csproj -c Release
```

All packs retain `AllowUnsafeBlocks`, matching core: Mono needs the emitted verification attributes when running code compiled against publicized game references. Without it the assemblies load but private-member paths (for example character selection and save completion) fail only when invoked. The inventory check enforces this build setting. This does not change command permissions.

The integration packager puts core in `plugin/` and optional DLLs in separate
`packs/` subdirectories. The portable executable and `Valheim.Cli.Testing` package
remain ValheimCLI-owned; ValheimTesting is not bundled back into core.

Reflection is independently optional: inspection/terrain consumers need not install
`cli_call`. Its existing cheat gate, overload selection and live-assembly lookup
are unchanged. World Tools still includes terrain mutations; it is not a read-only
permission boundary. Standard stays together because its actions share session
and gameplay helpers. Further subdivision should follow those dependencies.

## Paint and walking observations

`valheim.world/terrain-paint <integer x> <integer z>` returns the loaded native
one-metre paint texel, raw normalized `r/g/b/a`, texel coordinates and heightmap
origin. It never creates a compiler or regenerates/loads terrain. Missing maps,
unreadable textures and out-of-range texels return `complete=false`; the game's
black out-of-range sentinel is not a measurement. It measures the loaded mask,
not the renderer or compiler serialization. Keep alpha separately: its meaning
varies with biome/game material. Sampling uses the game's own WorldToVertexMask.

ValheimTesting's PaintCheck consumes this capability. WalkingReview uses the
existing read-only player-support observer while a human drives. Both require independent plans and strict world/plugin pins. A bounded native campaign has now exercised Reflection removal/reload in a loaded dedicated world: one persistent ValheimCLI connection, a fresh Reflection owner after reload, and unchanged identities for all other owners. The ValheimCLI-only client joined, enabled character protection and read native terrain, collision and paint; the server confirmed its save. These checks exposed and fixed the pack verification-metadata issue described above. Human walking and general rendered appearance are separate acceptance checks.


## Examples for consumers and extension authors

Start with the external [testing-framework setup guide](https://github.com/tvongaza/ValheimTesting/blob/main/docs/getting-started.md) and [example index](https://github.com/tvongaza/ValheimTesting/blob/main/examples/README.md). NoGameTerrain runs without ValheimCLI or Valheim; GameObserve adds strict pins and a read-only connection. TerrainCheck, ClientSurfaceCheck and PaintCheck require World Tools and an already prepared fixture. WalkingReview leaves movement and usability judgement to a person.

For a new game-side extension, read the [extension API guide](testing-toolkit.md) and the working [ReloadProbe](../examples/ReloadProbe). Its paired [ReloadCheck driver](https://github.com/tvongaza/ValheimTesting/blob/main/examples/ReloadCheck/README.md) demonstrates registration, cancellation, cleanup and command replacement. Use an owned scripts directory: ScriptEngine reloads every script there. Core replacement still requires a restart.

## Bounded terrain capture

World Tools exposes `valheim.world/terrain-grid <x> <z> <spacing> <countX> <countZ> <generator|loaded-ground>`. It accepts at most 256 samples, yields every 16 samples, and refuses a world/generator change during capture. Results include world UID, generator version, game version/assembly identity, timestamps, grid coordinates and a completeness flag. Coordinates use horizontal **x,z**, in metres, with x varying fastest.

Generator samples include height, biome and river facts. Loaded-ground samples read only an existing heightmap; biome and river fields are explicitly absent. No zones are generated and there is no fallback between layers. Capture is a sequence of observations, not an atomic snapshot of mutable ground. Cancellation never returns a partial grid as complete.

[TerrainCapture](https://github.com/tvongaza/ValheimTesting/blob/main/examples/TerrainCapture/README.md) validates and saves this input for exact replay. A captured result is useful input, not an independent expected answer. Local loop/import tests and compilation against game assemblies pass; this new capability has not yet been exercised in Valheim.

## Typed session lifecycle

Standard owns `valheim.session/state`, `join`, `leave` and `save`. No extra pack or dependency between packs is introduced. `state` is read-only and exposes world UID, native world/player readiness, connection status, saving and load errors. Native readiness does not establish mod completion or loaded terrain at a destination.

`join <host:port> <existing-character> [password-environment-variable]` requires the client menu and waits for a connected local player. The optional name resolves inside the game's process; the password itself never enters the command/result. Omission clears a stale password. A previous network instance's rejection is not attributed to the new join. `leave` saves the local character and waits for the menu; it does not confirm a remote server's world save. Both use the existing permission checks and shared mutation gate.

`save [timeout-seconds]` is server-only (1–600 seconds, default 120). It shares the `cli_save` loop, checks vanilla save refusals, waits for any earlier save, issues once, then requires the new save thread to finish and the save counter to advance in the same world. This is stronger than accepting a `Saving..` message. A timeout or cancellation does not stop an issued native operation: its owner and gate remain until the operation settles. Read-only state remains available. Unprovable transitions can require a controlled restart.

The external [SessionControl example](https://github.com/tvongaza/ValheimTesting/blob/main/examples/SessionControl/README.md) demonstrates exact-once actions and mandatory repinning after a transition, including failed or ambiguous attempts. Local policy/lifecycle tests and compilation against real game references pass. Native checks of this new structured surface remain pending; older text-command smoke checks do not establish them.
