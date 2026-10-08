using System;
using System.Collections.Generic;
using System.Linq;

namespace valheimCLI.Extensions
{
    /// <summary>Main-thread registration transaction. Unload uses object identity, never just a name.</summary>
    public sealed class OwnedCommandSet<T> : IDisposable where T : class
    {
        private readonly IDictionary<string, T> _table;
        private readonly Dictionary<string, T> _owned;
        private bool _disposed;
        public IReadOnlyDictionary<string, T> Commands => _owned;
        private OwnedCommandSet(IDictionary<string, T> table, Dictionary<string, T> owned)
        { _table = table; _owned = owned; }
        public static OwnedCommandSet<T> Register(IDictionary<string, T> table, Action register)
        {
            var before = new Dictionary<string, T>(table, StringComparer.OrdinalIgnoreCase);
            try
            {
                register();
                foreach (var entry in before)
                    if (!table.TryGetValue(entry.Key, out var current) || !ReferenceEquals(current, entry.Value))
                        throw new InvalidOperationException("Command collision or removal: " + entry.Key);
                var added = table.Where(e => !before.ContainsKey(e.Key)).ToDictionary(e => e.Key, e => e.Value, StringComparer.OrdinalIgnoreCase);
                return new OwnedCommandSet<T>(table, added);
            }
            catch
            {
                // Registration is synchronous on the main thread; no other owner can interleave.
                foreach (string name in table.Keys.ToArray()) if (!before.ContainsKey(name)) table.Remove(name);
                foreach (var entry in before) table[entry.Key] = entry.Value;
                throw;
            }
        }
        public bool Owns(string name) => !_disposed && _owned.TryGetValue(name, out var owned) &&
            _table.TryGetValue(name, out var current) && ReferenceEquals(current, owned);
        public void Dispose()
        {
            if (_disposed) return;
            foreach (var entry in _owned)
                if (_table.TryGetValue(entry.Key, out var current) && ReferenceEquals(current, entry.Value)) _table.Remove(entry.Key);
            _disposed = true;
        }
    }
}
