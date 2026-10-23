using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using HarmonyLib;

namespace valheimCLI;

internal static class MethodProfilingCommands
{
    private static readonly Harmony patches = new("valheimCLI.method-profiling");
    private static readonly ConcurrentDictionary<MethodBase, Timing> measurements = new();
    private static long started;

    private sealed class Timing
    {
        public long Calls;
        public long Ticks;
        public long Maximum;
    }

    private readonly struct Sample
    {
        public readonly Timing? Timing;
        public readonly long Start;
        public Sample(Timing timing) { Timing = timing; Start = Stopwatch.GetTimestamp(); }
    }

    internal static void Register()
    {
        new Terminal.ConsoleCommand("cli_method_profile_add", "Time managed methods, including nested calls: <full-type-name> <method,method>. Adds all named overloads. Instrumentation affects timing; use cli_method_profile_reset after setup and cli_method_profile_stop to remove patches.", args =>
        {
            if (args.Length != 3) throw new ArgumentException("Expected full-type-name method,method");
            Type type = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType(args[1], false)).FirstOrDefault(candidate => candidate != null)
                ?? throw new ArgumentException("Type is not loaded.");
            if (type.Assembly == typeof(MethodProfilingCommands).Assembly) throw new ArgumentException("Cannot instrument the profiler assembly.");
            string[] names = args[2].Split(',');
            MethodInfo[] methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(method => names.Contains(method.Name) && !method.ContainsGenericParameters && !method.IsAbstract && method.GetMethodBody() != null).ToArray();
            if (methods.Length == 0) throw new ArgumentException("No matching managed methods.");
            foreach (MethodInfo method in methods)
            {
                if (!measurements.TryAdd(method, new Timing())) continue;
                try
                {
                    patches.Patch(method, prefix: new HarmonyMethod(typeof(MethodProfilingCommands), nameof(Begin)) { priority = Priority.First },
                        finalizer: new HarmonyMethod(typeof(MethodProfilingCommands), nameof(End)) { priority = Priority.Last });
                }
                catch { measurements.TryRemove(method, out _); throw; }
                args.Context.AddString("METHOD added=" + method.DeclaringType!.FullName + "." + method);
            }
            started = Stopwatch.GetTimestamp();
        }, isCheat: true);
        new Terminal.ConsoleCommand("cli_method_profile_reset", "Reset accumulated method timings after setup.", args =>
        {
            foreach (Timing timing in measurements.Values)
            {
                Interlocked.Exchange(ref timing.Calls, 0);
                Interlocked.Exchange(ref timing.Ticks, 0);
                Interlocked.Exchange(ref timing.Maximum, 0);
            }
            started = Stopwatch.GetTimestamp();
            args.Context.AddString("METHOD reset count=" + measurements.Count);
        }, isCheat: true);
        new Terminal.ConsoleCommand("cli_method_profile_stop", "Report inclusive method times and remove all method profiling patches.", args =>
        {
            double elapsed = (Stopwatch.GetTimestamp() - started) / (double)Stopwatch.Frequency;
            try
            {
                patches.UnpatchSelf();
                args.Context.AddString("METHOD elapsedSeconds=" + elapsed.ToString("F3", CultureInfo.InvariantCulture) + " timings=inclusive");
                foreach (System.Collections.Generic.KeyValuePair<MethodBase, Timing> entry in measurements.OrderByDescending(pair => pair.Value.Ticks))
                {
                    Timing timing = entry.Value;
                    double total = timing.Ticks * 1000d / Stopwatch.Frequency;
                    args.Context.AddString(FormattableString.Invariant($"METHOD name={entry.Key.DeclaringType!.FullName}.{entry.Key} calls={timing.Calls} totalMs={total:F3} meanMs={(timing.Calls == 0 ? 0 : total / timing.Calls):F6} maxMs={timing.Maximum * 1000d / Stopwatch.Frequency:F3}"));
                }
            }
            finally { Dispose(); }
        }, isCheat: true);
    }

    private static void Begin(MethodBase __originalMethod, out Sample __state)
    {
        __state = measurements.TryGetValue(__originalMethod, out Timing timing) ? new Sample(timing) : default;
    }

    private static void End(Sample __state)
    {
        Timing? timing = __state.Timing;
        if (timing == null) return;
        long elapsed = Stopwatch.GetTimestamp() - __state.Start;
        Interlocked.Increment(ref timing.Calls);
        Interlocked.Add(ref timing.Ticks, elapsed);
        long maximum;
        do { maximum = Interlocked.Read(ref timing.Maximum); }
        while (elapsed > maximum && Interlocked.CompareExchange(ref timing.Maximum, elapsed, maximum) != maximum);
    }

    internal static void Dispose()
    {
        patches.UnpatchSelf();
        measurements.Clear();
    }
}
