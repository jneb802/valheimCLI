using System.Collections;
using BepInEx;
using valheimCLI.Extensions;
namespace valheimCLI
{
    [BepInPlugin("valheimCLI.reflection", "CLI Reflection", "0.1.0")]
    [BepInDependency("valheimCLI.valheimCLI", "1.1.0")]
    public sealed class ReflectionPack : BaseUnityPlugin
    {
        private ConsoleModule? _module;
        private IEnumerator Start()
        {
            float deadline = UnityEngine.Time.realtimeSinceStartup + 30;
            while (valheimCLIPlugin.Instance?.Modules == null)
            { if (UnityEngine.Time.realtimeSinceStartup > deadline) { Logger.LogError("CLI core 1.1 is not ready."); yield break; } yield return null; }
            var core = valheimCLIPlugin.Instance!;
            while (System.Linq.Enumerable.Any(core.Extensions!.Registrations, r => r.Id == "cli.reflection"))
            { if (UnityEngine.Time.realtimeSinceStartup > deadline) { Logger.LogError("Previous Reflection pack failed cleanup or remains active."); yield break; } yield return null; }
            _module = core.Modules.Register("cli.reflection", "0.1.0", CallCommands.Register);
        }
        private void OnDestroy() => _module?.Dispose();
    }
}
