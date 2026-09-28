using System;
using System.Collections;
using System.Diagnostics;
namespace valheimCLI
{
    public static class AsyncExecution
    {
        public static readonly OperationGate Gate = new OperationGate();
        private static long s_consoleIds = -1;
        public sealed class Context
        {
            public long Id;
            public string Name = "";
            public AsyncHandle? Handle;
            public Action<string> Output = _ => { };
            public Func<bool> OwnerClosing = () => false;
            public bool Cancelled => OwnerClosing() || (Handle != null && Handle.Abandoned);
        }

        public static void Start(string name, Action<string> console, Func<Context, IEnumerator> body, bool gated, Extensions.ConsoleModule? owner = null)
        {
            AsyncHandle? handle = valheimCLIPlugin.BeginAsync();
            valheimCLIPlugin? plugin = valheimCLIPlugin.Instance;
            if (plugin == null)
            {
                console("ERROR: plugin not available");
                handle?.Complete();
                return;
            }
            Context ctx = new Context
            {
                Id = handle?.Id ?? s_consoleIds--,
                Name = name,
                Handle = handle,
                Output = handle != null ? handle.Output : console,
                OwnerClosing = () => owner?.Closing == true
            };
            if (owner != null) owner.Run(Run(ctx, body, gated));
            else plugin.StartCoroutine(Run(ctx, body, gated));
        }

        private static IEnumerator Run(Context ctx, Func<Context, IEnumerator> bodyFactory, bool gated)
        {
            bool owns = false;
            bool finished = false;
            IEnumerator? body = null;
            try
            {
                if (gated)
                {
                    // Wait for the player/camera to be free. A request that times out while
                    // waiting is abandoned by the server and gives up here without acting.
                    Stopwatch waited = Stopwatch.StartNew();
                    while (!Gate.TryAcquire(ctx.Id, ctx.Name))
                    {
                        if (ctx.Cancelled || waited.Elapsed.TotalSeconds > 60)
                        {
                            ctx.Output($"ERROR: code=busy message={ctx.Name} waited {waited.ElapsedMilliseconds} ms for {Gate.OwnerName} (request #{Gate.Owner}) and gave up");
                            finished = true;
                            yield break;
                        }
                        yield return null;
                    }
                    owns = true;
                }

                if (ctx.Cancelled) { ctx.Output("ERROR: code=extension_unloaded message=Command owner retired before starting."); finished = true; yield break; }
                body = bodyFactory(ctx);
                while (true)
                {
                    object? current;
                    try
                    {
                        if (!body.MoveNext()) break;
                        current = body.Current;
                    }
                    catch (Exception ex)
                    {
                        ctx.Output($"ERROR: code=unexpected_exception message={ex.Message}");
                        valheimCLIPlugin.Log.LogError($"Async {ctx.Name} #{ctx.Id}: {ex}");
                        break;
                    }
                    yield return current;
                }
                finished = true;
            }
            finally
            {
                // Not finished: the coroutine was stopped from outside, which
                // happens when the plugin is destroyed (a live reload, cli_self_unload).
                try { (body as IDisposable)?.Dispose(); }
                finally
                {
                if (!finished)
                    ctx.Output(RequestBroker.UnloadedLine);
                if (owns)
                    Gate.Release(ctx.Id);
                ctx.Handle?.Complete();
                }
            }
        }

    }
}
