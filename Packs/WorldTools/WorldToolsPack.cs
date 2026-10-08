using System;
using System.Collections;
using BepInEx;
using valheimCLI.Extensions;

namespace valheimCLI
{
    [BepInPlugin("valheimCLI.worldtools", "CLI World Tools", "0.1.0")]
    [BepInDependency("valheimCLI.valheimCLI", "1.1.0")]
    public sealed class WorldToolsPack : BaseUnityPlugin
    {
        private ConsoleModule? _module;
        private ExtensionRegistration? _observations;
        private IEnumerator Start()
        {
            float deadline = UnityEngine.Time.realtimeSinceStartup + 30;
            while (valheimCLIPlugin.Instance?.Modules == null)
            { if (UnityEngine.Time.realtimeSinceStartup > deadline) { Logger.LogError("CLI core 1.1 is not ready."); yield break; } yield return null; }
            var core = valheimCLIPlugin.Instance!;
            while (System.Linq.Enumerable.Any(core.Extensions!.Registrations, r => r.Id == "cli.worldtools" || r.Id == "valheim.world"))
            { if (UnityEngine.Time.realtimeSinceStartup > deadline) { Logger.LogError("Previous World Tools pack is active/draining; replacement refused."); yield break; } yield return null; }
            try
            {
                _observations = WorldObservations.Register(core.Extensions!);
                _module = core.Modules.Register("cli.worldtools", "0.1.0", () =>
                { WorldInspectionCommands.Register(); TerrainInspectionCommands.Register(); RockInspectionCommands.Register(); TerrainActionCommands.Register(); });
            }
            catch { _module?.Dispose(); _observations?.Dispose(); throw; }
        }
        private void OnDestroy() { _module?.Dispose(); _observations?.Dispose(); }
    }
}
