using System;
using System.Collections;

namespace valheimCLI
{
    // The loop is shared by cli_save and the typed capability; only this seam touches Unity.
    internal interface ISessionSave
    {
        bool SameWorld { get; }
        bool IsSaving { get; }
        string SkipReason { get; }
        uint SaveNumber { get; }
        bool Started { get; }
        bool Writing { get; }
        void Start();
    }

    internal static class SessionSave
    {
        internal static IEnumerator Run(ISessionSave host, SaveOutcome outcome, Func<double> seconds,
            Func<bool> cancelled, Action<Func<bool>> retainUntil, Action<SaveOutcome> done, Action<string, string> fail)
        {
            while (host.SameWorld && host.IsSaving && seconds() < outcome.TimeoutSeconds && !cancelled())
                yield return null;
            if (cancelled()) { fail("cancelled", "No new save was started."); yield break; }
            if (!host.SameWorld) { fail("world_changed", "World changed before saving."); yield break; }
            if (host.IsSaving) { fail("save_timeout", "An earlier save is still writing; no new save was started."); yield break; }
            outcome.Skipped = host.SkipReason;
            if (outcome.Skipped.Length > 0) { done(outcome); yield break; }
            outcome.SaveNumberBefore = host.SaveNumber;
            // Register BEFORE issuing the effect, including when Start throws after starting a write.
            retainUntil(() => !host.Writing);
            host.Start();
            outcome.Started = host.Started;
            while (host.Writing && seconds() < outcome.TimeoutSeconds && !cancelled())
                yield return null;
            outcome.Finished = host.Started && !host.Writing;
            outcome.Milliseconds = (long)(seconds() * 1000);
            if (!host.SameWorld) { fail("world_changed", "World changed during saving; completion is unproven."); yield break; }
            if (cancelled()) { fail("cancelled", "Any issued save continues; do not retry until it settles."); yield break; }
            outcome.SaveNumberAfter = host.SaveNumber;
            done(outcome);
        }
    }
}
