# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

ValheimCLI enables controlling Valheim from an external terminal. It consists of two components:
1. **BepInEx mod** (`valheimCLI.dll`) - Runs inside Valheim, provides a TCP server
2. **ValheimCLI tool** (`valheim-cli`) - External .NET 9 console app that connects to the game

## Build Commands

```bash
# Build the mod (output: bin/Debug/valheimCLI.dll)
dotnet build

# Build the CLI tool
cd CLI && dotnet build

# Build release versions
dotnet build -c Release
cd CLI && dotnet build -c Release
```

## Prerequisites

- .NET 10 SDK for the current test suite (external ValheimCLI targets .NET 9)
- Valheim installed via Steam with BepInEx
- Publicized assemblies in `Valheim.app/Contents/Resources/Data/Managed/publicized_assemblies/`

## Architecture

```
CLI (valheim-cli)  <--TCP:5555-->  Mod (valheimCLI.dll)  -->  Console.TryRunCommand()
```

**Mod Components** (`Source/`):
- `Plugin.cs` - BepInEx plugin entry point, processes commands in Unity Update loop
- `CommandServer.cs` - TCP server that accepts ValheimCLI connections, queues commands

**ValheimCLI Tool** (`CLI/`):
- `Program.cs` - TCP client with interactive REPL and single-command modes

**Key Valheim APIs used**:
- `Console.instance.TryRunCommand(text, silentFail, skipAllowedCheck)` - executes console commands
- `Terminal.commands` - Dictionary of all registered commands
- `Terminal.AddString()` - patched via Harmony to capture output

## Testing

Start with local coverage: `dotnet test Tests/RequestBroker.Tests/RequestBroker.Tests.csproj`, which includes the command-pack inventory checks (`CommandPackInventoryTests`). The developer-loop scripts (`dev-loop`, `pin-mods`, `log-summary`) and their tests live in [ValheimTesting's `tools/dev-loop`](https://github.com/tvongaza/ValheimTesting/tree/main/tools/dev-loop), not here. See [AGENTS.md](AGENTS.md) for the shared test pyramid and examples. Game-side builds need the local compile references; external library tests do not need Valheim.

For an authorized disposable native fixture, use matching core and [optional command packs](docs/command-packs.md). Core alone no longer exposes general gameplay commands. Keep core in plugins, use ScriptEngine only for optional packs/adapters, verify strict pins and world readiness, and never test against a server or save that people play on. The external ValheimCLI output is under `CLI/bin/Debug/net9.0/`.

## Configuration

Config file: `BepInEx/config/valheimCLI.valheimCLI.cfg`
- `Server.Enabled` - Enable/disable the TCP server
- `Server.Port` - Port number (default: 5555)

## Code Style

- Never use `var` - always write explicit types

## Testing-framework agent guidance

Read [AGENTS.md](AGENTS.md) for test-layer ownership, current package/pack setup and the shared agent workflow. Existing project-specific guidance above remains applicable.
