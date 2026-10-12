using System;
using valheimCLI.Extensions;
namespace valheimCLI
{
    internal sealed class CapturePack : IDisposable
    {
        private ConsoleModule? _module;
        public void Register(valheimCLIPlugin core)
        {
            _module = core.Modules.Register("cli.capture", "0.1.0", CaptureCommands.Register);
            _module.Owner.OnDispose(CaptureCommands.RestoreAll);
        }
        public void Dispose() => _module?.Dispose();
    }
}
