using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace valheimCLI.Extensions
{
    /// <summary>Compatibility console packs use the existing broker, validity rules and extension owner.</summary>
    public sealed class ConsoleModuleHost : IDisposable
    {
        private readonly valheimCLIPlugin _plugin;
        private readonly ExtensionRegistry _registry;
        private readonly List<ConsoleModule> _modules = new List<ConsoleModule>();
        private bool _disposed;
        internal ConsoleModuleHost(valheimCLIPlugin plugin, ExtensionRegistry registry)
        { _plugin = plugin; _registry = registry; }
        public ConsoleModule Register(string id, string version, Action register,
            Func<string, Action<string>, bool>? dispatch = null, Action? tick = null)
        {
            _registry.CheckThread();
            if (_disposed) throw new ObjectDisposedException(nameof(ConsoleModuleHost));
            ConsoleModule? module = null;
            var owner = _registry.Register(id, version, ExtensionRegistry.ApiVersion,
                new ExtensionCommand("commands", "List this pack's compatible console commands", context => Describe(module!, context), readOnly: true));
            OwnedCommandSet<Terminal.ConsoleCommand>? commands = null;
            try
            {
                commands = OwnedCommandSet<Terminal.ConsoleCommand>.Register(Terminal.commands, register);
                module = new ConsoleModule(_plugin, owner, commands, dispatch, tick);
                _modules.Add(module);
                owner.OnRetiring(() => _modules.Remove(module));
                return module;
            }
            catch
            {
                // A module that failed to construct never installed its retiring hook,
                // so the Terminal commands it registered are removed here.
                try
                {
                    if (module == null && commands != null)
                    {
                        commands.Dispose();
                        CliCommandValidity.ForgetOwnCommands(commands.Commands.Values);
                    }
                }
                finally { owner.Dispose(); }
                throw;
            }
        }
        private static IEnumerator Describe(ConsoleModule module, ExtensionContext context)
        {
            context.Succeed(new Dictionary<string, object?> { ["source"] = "console-pack", ["complete"] = true,
                ["commands"] = module.CommandNames.ToArray() });
            yield break;
        }
        internal bool TryDispatch(string command, Action<string> output)
        {
            foreach (var module in _modules.ToArray()) if (module.TryDispatch(command, output)) return true;
            return false;
        }
        internal void Tick()
        {
            foreach (var module in _modules.ToArray()) module.Tick();
        }
        public void Dispose()
        {
            if (_disposed) return; _disposed = true;
            foreach (var module in _modules.ToArray()) module.Dispose();
            _modules.Clear();
        }
    }

    public sealed class ConsoleModule : IDisposable
    {
        private readonly valheimCLIPlugin _plugin;
        private readonly OwnedCommandSet<Terminal.ConsoleCommand> _commands;
        private readonly Func<string, Action<string>, bool>? _dispatch;
        private readonly Action? _tick;
        private readonly List<Running> _running = new List<Running>();
        public ExtensionRegistration Owner { get; }
        public bool Closing => Owner.IsClosing;
        public IEnumerable<string> CommandNames => _commands.Commands.Keys;
        internal ConsoleModule(valheimCLIPlugin plugin, ExtensionRegistration owner,
            OwnedCommandSet<Terminal.ConsoleCommand> commands, Func<string, Action<string>, bool>? dispatch, Action? tick)
        {
            _plugin = plugin; Owner = owner; _commands = commands; _dispatch = dispatch; _tick = tick;
            CliCommandValidity.RecordOwnCommands(commands.Commands.Values);
            owner.OnRetiring(() =>
            {
                commands.Dispose();
                CliCommandValidity.ForgetOwnCommands(commands.Commands.Values);
            });
        }
        internal bool TryDispatch(string command, Action<string> output)
        {
            string name = command.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            if (Closing || _dispatch == null || !_commands.Owns(name)) return false;
            var entry = _commands.Commands[name];
            if (Console.instance != null ? !entry.IsValid(Console.instance, false) : entry.IsCheat || entry.IsNetwork || entry.OnlyServer)
            { output("ERROR: code=command_not_allowed message=Command's normal console restrictions refused it."); return true; }
            return _dispatch(command, output);
        }
        internal void Tick() { if (!Closing) _tick?.Invoke(); }
        public Coroutine Run(IEnumerator body)
        {
            var lease = Owner.TrackWork();
            var running = new Running(body, lease); _running.Add(running);
            try { running.Coroutine = _plugin.StartCoroutine(Track(running)); return running.Coroutine; }
            catch { Finish(running); throw; }
        }
        private IEnumerator Track(Running running)
        {
            try { while (running.Body.MoveNext()) yield return running.Body.Current; }
            finally { Finish(running); }
        }
        private void Finish(Running running)
        {
            if (!_running.Remove(running)) return;
            try { (running.Body as IDisposable)?.Dispose(); }
            catch (Exception error)
            {
                Owner.CleanupError = "Coroutine cleanup failed; restart required: " + error.Message;
                throw;
            }
            finally { running.Lease.Dispose(); }
        }
        public void Stop(Coroutine coroutine)
        {
            var running = _running.FirstOrDefault(r => ReferenceEquals(r.Coroutine, coroutine));
            if (running == null) return;
            try { _plugin.StopCoroutine(coroutine); }
            finally { Finish(running); }
        }
        public void Dispose() => Owner.Dispose();
        private sealed class Running
        {
            public readonly IEnumerator Body;
            public readonly IDisposable Lease;
            public Coroutine Coroutine = null!;
            public Running(IEnumerator body, IDisposable lease) { Body = body; Lease = lease; }
        }
    }
}
