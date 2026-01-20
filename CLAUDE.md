# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a Valheim CLI mod that enables controlling Valheim from an external terminal. It consists of two components:
1. **BepInEx mod** (`valheimCLI.dll`) - Runs inside Valheim, provides a TCP server
2. **CLI tool** (`valheim-cli`) - External .NET 8 console app that connects to the game

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

- .NET SDK 8.0+
- Valheim installed via Steam with BepInEx
- Publicized assemblies in `Valheim.app/Contents/Resources/Data/Managed/publicized_assemblies/`

## Architecture

```
CLI (valheim-cli)  <--TCP:5555-->  Mod (valheimCLI.dll)  -->  Console.TryRunCommand()
```

**Mod Components** (`Source/`):
- `Plugin.cs` - BepInEx plugin entry point, processes commands in Unity Update loop
- `CommandServer.cs` - TCP server that accepts CLI connections, queues commands

**CLI Tool** (`CLI/`):
- `Program.cs` - TCP client with interactive REPL and single-command modes

**Key Valheim APIs used**:
- `Console.instance.TryRunCommand(text, silentFail, skipAllowedCheck)` - executes console commands
- `Terminal.commands` - Dictionary of all registered commands
- `Terminal.AddString()` - patched via Harmony to capture output

## Testing

1. Build both projects
2. Copy `bin/Debug/valheimCLI.dll` to `Valheim/BepInEx/plugins/`
3. Launch Valheim
4. Run `./CLI/bin/Debug/net8.0/valheim-cli`
5. Try commands like `help`, `pos`, `spawn Boar 5`

## Configuration

Config file: `BepInEx/config/valheimCLI.valheimCLI.cfg`
- `Server.Enabled` - Enable/disable the TCP server
- `Server.Port` - Port number (default: 5555)
