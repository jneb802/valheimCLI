using System;
using valheimCLI.Extensions;
namespace valheimCLI
{
    internal sealed class ReflectionPack : IDisposable
    {
        private ConsoleModule? _module;
        public void Register(valheimCLIPlugin core)
        {
            _module = core.Modules.Register("cli.reflection", "0.1.0", CallCommands.Register);
        }
        public void Dispose() => _module?.Dispose();
    }
}
