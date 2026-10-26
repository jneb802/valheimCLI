# Performance snapshots and connection cleanup

Validated on Valdev with both Valnet Linux clients using the Praetoris Season 8
8.0.49 mod set and candidate Endgame 0.5.5 on 2026-10-21.

`cli_performance_snapshot` returned scene-object, renderer, particle, light,
character, memory, and display counters on both clients. Full scene scans took
about two seconds with this mod set. Exclude those scans and a settling period
from frame-rate measurements. The command does not force garbage collection.

During an extended encounter test, client 1 exhausted its 1,024 open-file limit
and crashed after more than 800 commands. The command server was waiting for
`DataAvailable` while relying on the stale `TcpClient.Connected` flag. It never
read the orderly disconnection, so sockets and polling threads accumulated.

The handler now detects a readable socket with zero available bytes and leaves
the loop. Its existing disposal and subscriber-removal paths then run.

After restarting with the fix, `scripts/check-connection-cleanup.py` opened,
pinged, and closed 1,000 connections on each client:

| Client | File handles before / after | Threads before / after |
| --- | --- | --- |
| Valnet 01 | 183 / 183 | 110 / 110 |
| Valnet 02 | 185 / 185 | 116 / 116 |

Run this Linux check only after game loading finishes; asset loading changes
file counts independently. An initial check during asset loading was discarded
and repeated after loading finished. The corrected build has zero warnings and
zero errors. Its SHA-256 is
`e66ac8d2b2bcc3484431c2178db8ce3e18550cc3e013425534c9a5e9c43e57d3`.

The preliminary encounter measurements were invalidated. Fresh two-encounter
measurements are tracked in jneb802/ValheimEndgame#87.

## Controlled scene comparisons

`cli_profile_start` records main-thread time, render-thread time, draw calls,
batches, SetPass calls, triangles, and vertices where the runtime exposes those
counters. `cli_profile_stop` reports sample count, mean, 95th percentile, and
maximum, then disposes the recorders. Capacity is 4,096 frames per counter.

`cli_scene_counts <exact-root-name>` reports each matching root's position,
object and component counts, and enabled script types. It includes inactive
children and must run outside the measured frame window.

For an empty test room, `cli_scene_presentation <exact-root-name> <x> <y> <z>
<radius> false` temporarily disables its renderers, lights, animators, and
scripts and pauses playing particles and audio. Colliders and root objects stay
active. Repeat with `true` to restore the original enabled and playing state.
This is an operator-controlled comparison tool, not automatic game behavior.
Use the same camera position for enabled/disabled/enabled comparisons. Restore
every changed room before leaving the test.

The profiling commands were validated on both Valnet clients. A controlled
enabled/disabled/enabled comparison in a four-room Endgame encounter changed
client 1 from 32.41 to 36.29 to 31.11 FPS. Main-thread means changed from
31.100 to 27.207 to 31.806 ms. Client 2 was an unchanged control. All manually
suspended components were restored before the candidate mod was installed.
Per-room counts also verified that the candidate suspended empty rooms while
keeping 5,855 active colliders per room and restored presentation on re-entry.
