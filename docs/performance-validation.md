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
