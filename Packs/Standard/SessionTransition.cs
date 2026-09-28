using System;
using System.Collections;
using System.Collections.Generic;
using valheimCLI.Extensions;

namespace valheimCLI
{
    internal static class SessionTransition
    {
        // null is pending, empty is successful, any other value is a terminal failure code.
        internal static IEnumerator Run(ExtensionContext context, string action, Action issue,
            Func<string?> completion, Func<double> seconds, double timeout)
        {
            if (context.Cancelled) { context.Fail("cancelled", "No transition requested."); yield break; }
            bool issued = false;
            string? settled = null;
            string? Check() => settled ?? (settled = completion());
            context.WaitForQuiescence(() => !issued || Check() != null);
            issued = true;
            issue(); // If issuing throws, retain ownership until the native transition is known to have settled.
            string? result;
            while ((result = Check()) == null && seconds() < timeout && !context.Cancelled)
                yield return null;
            if (context.Cancelled) { context.Fail("cancelled", "Transition may continue; inspect state before another action."); yield break; }
            if (result == null) { context.Fail("transition_timeout", "Transition is unproven and may continue; inspect state before another action."); yield break; }
            if (result.Length != 0) { context.Fail(result, "The game refused the session transition."); yield break; }
            context.Succeed(new Dictionary<string, object?> { ["source"] = "session-" + action, ["complete"] = true, ["action"] = action });
        }
    }
}
