using System;
using valheimCLI.Observe;

namespace valheimCLI
{
    /// <summary>
    /// The first-party command groups are logical owners in the default plugin.
    /// Third-party extensions still use the same registry and lifecycle API.
    /// </summary>
    internal sealed class BuiltInCommands : IDisposable
    {
        private readonly StandardPack _standard = new();
        private readonly WorldToolsPack _worldTools = new();
        private readonly ReflectionPack _reflection = new();
        private readonly CapturePack _capture = new();
        private readonly ObservePack _observe = new();
        private bool _disposed;

        public void Register(valheimCLIPlugin core)
        {
            try
            {
                _standard.Register(core);
                _worldTools.Register(core);
                _reflection.Register(core);
                _capture.Register(core);
                _observe.Register(core);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Tick() => _observe.Tick();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (IDisposable group in new IDisposable[] { _observe, _capture, _reflection, _worldTools, _standard })
                try { group.Dispose(); }
                catch (Exception error) { valheimCLIPlugin.Log.LogError("A built-in ValheimCLI command group failed cleanup: " + error); }
        }
    }
}
