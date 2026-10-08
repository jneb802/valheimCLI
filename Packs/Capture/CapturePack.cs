using System.Collections;
using BepInEx;
using valheimCLI.Extensions;
namespace valheimCLI
{
    [BepInPlugin("valheimCLI.capture", "CLI Capture Controls", "0.1.0")]
    [BepInDependency("valheimCLI.valheimCLI", "1.1.0")]
    public sealed class CapturePack : BaseUnityPlugin
    {
        private ConsoleModule? _module;
        private IEnumerator Start()
        {
            float deadline = UnityEngine.Time.realtimeSinceStartup + 30;
            while (valheimCLIPlugin.Instance?.Modules == null)
            { if (UnityEngine.Time.realtimeSinceStartup > deadline) { Logger.LogError("CLI core 1.1 is not ready."); yield break; } yield return null; }
            var core = valheimCLIPlugin.Instance!;
            while (System.Linq.Enumerable.Any(core.Extensions!.Registrations, r => r.Id == "cli.capture"))
            { if (UnityEngine.Time.realtimeSinceStartup > deadline) { Logger.LogError("Previous Capture pack failed cleanup or remains active."); yield break; } yield return null; }
            _module = core.Modules.Register("cli.capture", "0.1.0", CaptureCommands.Register);
            _module.Owner.OnDispose(CaptureCommands.RestoreAll);
        }
        private void OnDestroy() => _module?.Dispose();
    }
}
