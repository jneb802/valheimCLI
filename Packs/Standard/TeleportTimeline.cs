namespace valheimCLI
{
    /// <summary>Frame observations become one timestamp per teleport transition. Missing phases remain -1.</summary>
    internal sealed class TeleportTimeline
    {
        internal long RequestedMs { get; private set; } = -1;
        internal long MovedMs { get; private set; } = -1;
        internal long AreaReadyMs { get; private set; } = -1;
        internal long FloorReadyMs { get; private set; } = -1;
        internal long DoneMs { get; private set; } = -1;
        internal bool Started => RequestedMs >= 0;
        internal bool Finished => DoneMs >= 0;

        internal void Observe(long milliseconds, bool teleporting, bool moved, bool areaReady, bool floorReady)
        {
            if (Finished) return;
            if (!Started)
            {
                if (!teleporting) return;
                RequestedMs = milliseconds;
            }
            if (moved && MovedMs < 0) MovedMs = milliseconds;
            if (areaReady && AreaReadyMs < 0) AreaReadyMs = milliseconds;
            if (floorReady && FloorReadyMs < 0) FloorReadyMs = milliseconds;
            if (!teleporting) DoneMs = milliseconds;
        }
    }
}
