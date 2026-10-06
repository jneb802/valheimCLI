using System;
using System.Collections;
using BepInEx;
using valheimCLI.Extensions;

namespace valheimCLI
{
    [BepInPlugin("valheimCLI.standard", "CLI Standard Commands", "0.1.0")]
    [BepInDependency("valheimCLI.valheimCLI", "1.1.0")]
    public sealed class StandardPack : BaseUnityPlugin
    {
        public static ConsoleModule Module { get; private set; } = null!;
        private ConsoleModule? _own;
        private ExtensionRegistration? _session;
        private IEnumerator Start()
        {
            float deadline = UnityEngine.Time.realtimeSinceStartup + 30;
            while (valheimCLIPlugin.Instance?.Modules == null)
            { if (UnityEngine.Time.realtimeSinceStartup > deadline) { Logger.LogError("CLI core 1.1 is not ready."); yield break; } yield return null; }
            // A replacement waits while the previous owner's issued effects settle.
            var core = valheimCLIPlugin.Instance!;
            while (System.Linq.Enumerable.Any(core.Extensions!.Registrations, r => r.Id == "cli.standard" || r.Id == "valheim.session"))
            { if (UnityEngine.Time.realtimeSinceStartup > deadline) { Logger.LogError("Previous Standard pack is active/draining; replacement refused."); yield break; } yield return null; }
            try
            {
            _session = SessionCapabilities.Register(core.Extensions!);
            _own = core.Modules.Register("cli.standard", "0.1.0", () => { CustomCommands.Register(); SessionControlCommands.Register(); }, StandardDispatch.Execute, StandardSession.Tick);
            Module = _own;
            _own.Owner.OnRetiring(() => RouteController.Stop(_ => { }));
            StandardSession.Initialize();
            // Without the patch the pack still works; cli_set_player_safety then reports ghostReplicated=False.
            try { GhostReplication.Patch(); }
            catch (Exception ex) { GhostReplication.Unpatch(); Logger.LogError("Ghost mode is not replicated to other peers: " + ex); }
            Logger.LogInfo("Standard commands ready; owner=" + _own.Owner.Instance);
            }
            catch { _own?.Dispose(); _session?.Dispose(); throw; }
        }
        private void OnDestroy() { GhostReplication.Unpatch(); TestTeleportTiming.Set(false, out _); _own?.Dispose(); _session?.Dispose(); }
    }
}
