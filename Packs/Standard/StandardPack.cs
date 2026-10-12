using System;
using valheimCLI.Extensions;

namespace valheimCLI
{
    // A logical command owner inside the one default ValheimCLI plugin.
    internal sealed class StandardPack : IDisposable
    {
        public static ConsoleModule Module { get; private set; } = null!;
        private ConsoleModule? _own;
        private ExtensionRegistration? _session;
        public void Register(valheimCLIPlugin core)
        {
            try
            {
                _session = SessionCapabilities.Register(core.Extensions!);
                _own = core.Modules.Register("cli.standard", "0.1.0", () => { CustomCommands.Register(); SessionControlCommands.Register(); }, StandardDispatch.Execute, StandardSession.Tick);
                Module = _own;
                _own.Owner.OnRetiring(() => RouteController.Stop(_ => { }));
                StandardSession.Initialize();
                // Without the patch cli_set_player_safety reports ghostReplicated=False.
                try { GhostReplication.Patch(); }
                catch (Exception ex) { GhostReplication.Unpatch(); valheimCLIPlugin.Log.LogError("Ghost mode is not replicated to other peers: " + ex); }
                valheimCLIPlugin.Log.LogInfo("Standard commands ready; owner=" + _own.Owner.Instance);
            }
            catch { Dispose(); throw; }
        }
        public void Dispose() { GhostReplication.Unpatch(); TestTeleportTiming.Set(false, out _); _own?.Dispose(); _session?.Dispose(); }
    }
}
