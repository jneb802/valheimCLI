using System;
using valheimCLI.Extensions;

namespace valheimCLI.Observe
{
    internal sealed class ObservePack : IDisposable
    {
        private ExtensionRegistration? _registration;

        public void Register(valheimCLIPlugin core)
        {
            var registry = core.Extensions!;
            _registration = registry.Register("valheim.observe", "0.1.0", ExtensionRegistry.ApiVersion,
                new ExtensionCommand("content-census", "List registered items, recipes, pieces, status effects and prefabs by prefix: <owner-guid> <prefix> [<prefix> ...]", ContentCensus.Run, readOnly: true, needsWorld: true),
                new ExtensionCommand("harmony", "List applied Harmony patches: [owner]", HarmonyCensus.Run, readOnly: true),
                new ExtensionCommand("dungeon-rooms", "Read saved dungeon rooms near a position: <x> <z> [radius]", DungeonRooms.Run, readOnly: true, role: ExtensionRole.Server, needsWorld: true),
                new ExtensionCommand("unresolved-prefabs", "List unresolved prefab hashes near the player: [radius]", UnresolvedPrefabs.Run, readOnly: true, role: ExtensionRole.Client, needsWorld: true),
                new ExtensionCommand("zones", "Report zone and area readiness: <x,z> [<x,z> ...] (zone coordinates)", ZonePresence.Run, readOnly: true, needsWorld: true),
                new ExtensionCommand("globalkeys", "List this process's global keys", GlobalKeyCommands.ListKeys, readOnly: true, needsWorld: true),
                new ExtensionCommand("config", "Read a plugin's live config entry: <guid> <section> <key>, percent-encoded", ConfigEntryCommand.Run, readOnly: true),
                new ExtensionCommand("custom-data", "Read the local player's custom data and its profile: [key-prefix]", PlayerCustomData.Run, readOnly: true, role: ExtensionRole.Client, needsWorld: true),
                new ExtensionCommand("review-begin", "Snapshot the owned client's review state: <run-id>", ReviewState.Begin, readOnly: true, role: ExtensionRole.Client, needsWorld: true),
                new ExtensionCommand("review-restore", "Restore the owned client's review state: <run-id>", ReviewState.Restore, role: ExtensionRole.Client, needsWorld: true),
                new ExtensionCommand("review-mist-off", "Hide mist for this review capture and remember each volume: <run-id>", ReviewState.MistOff, role: ExtensionRole.Client, needsWorld: true),
                new ExtensionCommand("review-clutter-off", "Hide ground clutter for this review capture and remember each entry: <run-id>", ReviewState.ClutterOff, role: ExtensionRole.Client, needsWorld: true),
                new ExtensionCommand("review-clip-frames", "Render bounded world-only PNG frames: <review-id> <new-absolute-directory> <width> <height> <fps> <frames>", ReviewClipFrames.Capture, role: ExtensionRole.Client, needsWorld: true));
        }

        public void Dispose()
        {
            ReviewClipFrames.AbortOnUnload();
            ReviewState.RestoreOnUnload();
            _registration?.Dispose();
        }

        public void Tick() => ReviewState.RetireChangedWorld();
    }
}
