## Environment

- .NET 8.0.32 (.NET 8.0.32), build: Release, TieredCompilation: off
- GC: ServerGC=False, LatencyMode=Interactive, concurrent=default
- ProcessorCount=16, OS=Ubuntu 24.04.4 LTS

### 1440 steps = 3.0 min game time (+200 warm-up steps, as fast as possible)

Checksum: state=0xcea17643ad29505a events=332364 eventHash=0x3689df867d964ef7 -> all variants/trials identical: **YES** (b0 compared on state+count only)

| metric | a1 immutable-builder | a2 immutable-naive | b SoA+log+snap/8 | b SoA+log+snap/64 | b0 SoA floor |
|---|---|---|---|---|---|
| trials | 5 | 3 | 5 | 5 | 5 |
| mean us/step (median; min-max) | 42.9 (41.4-72.5) | 551.4 (551.3-555.6) | 3.3 (3.2-3.5) | 3.6 (3.5-3.6) | 3.2 (3.1-3.2) |
| p50 us/step | 26.3 | 370.0 | 3.1 | 3.1 | 3.1 |
| p99 us/step | 54.9 | 1857.0 | 4.3 | 15.6 | 3.3 |
| max us/step | 12814.8 | 6041.1 | 29.8 | 29.9 | 20.0 |
| alloc bytes/step | 72,227 | 10,383,566 | 5,136 | 4,225 | 0 |
| GC gen0/gen1/gen2 (median) | 2/1/0 | 298/5/0 | 0/0/0 | 0/0/0 | 0/0/0 |
| GC pause total ms | 20.9 | 241.1 | 0.0 | 0.0 | 0.0 |
| GC heap size at end MB | 15.2 | 29.7 | 1.1 | 1.0 | 0.3 |
| events/step | 202.7 | 202.7 | 202.7 | 202.7 | 202.7 |
| retained growth MB (live, measured) | 13.48 | 24.67 | 7.06 | 5.81 | 0.01 |
| bags/125ms @100% (p50) | 4,747 | 338 | 39,860 | 39,987 | 40,245 |
| bags/125ms @10% (p50) | 475 | 34 | 3,986 | 3,999 | 4,024 |
| bags/125ms @100% (p99) | 2,277 | 67 | 29,288 | 7,997 | 37,925 |
| bags/125ms @10% (p99) | 228 | 7 | 2,929 | 800 | 3,792 |

### 4800 steps = 10.0 min game time (+200 warm-up steps, as fast as possible)

Checksum: state=0xb39898b3adde29ce events=1015788 eventHash=0xaaa9b22a8ec57151 -> all variants/trials identical: **YES** (b0 compared on state+count only)

| metric | a1 immutable-builder | a2 immutable-naive | b SoA+log+snap/8 | b SoA+log+snap/64 | b0 SoA floor |
|---|---|---|---|---|---|
| trials | 5 | 3 | 5 | 5 | 5 |
| mean us/step (median; min-max) | 42.6 (42.1-46.2) | 569.0 (560.8-576.8) | 3.3 (3.3-3.5) | 3.3 (3.2-3.5) | 3.2 (3.1-3.3) |
| p50 us/step | 26.1 | 374.0 | 3.1 | 3.1 | 3.1 |
| p99 us/step | 53.1 | 1816.1 | 5.3 | 13.6 | 3.3 |
| max us/step | 12347.9 | 16380.3 | 56.9 | 51.1 | 33.7 |
| alloc bytes/step | 72,225 | 10,383,237 | 5,102 | 4,194 | 0 |
| GC gen0/gen1/gen2 (median) | 7/6/1 | 995/14/1 | 0/0/0 | 0/0/0 | 0/0/0 |
| GC pause total ms | 65.5 | 827.2 | 0.0 | 0.0 | 0.0 |
| GC heap size at end MB | 43.4 | 87.1 | 1.1 | 1.0 | 0.2 |
| events/step | 203.2 | 203.2 | 203.2 | 203.2 | 203.2 |
| retained growth MB (live, measured) | 44.92 | 82.17 | 23.36 | 19.22 | 0.04 |
| bags/125ms @100% (p50) | 4,791 | 334 | 39,860 | 40,116 | 40,245 |
| bags/125ms @10% (p50) | 479 | 33 | 3,986 | 4,012 | 4,024 |
| bags/125ms @100% (p99) | 2,353 | 69 | 23,810 | 9,167 | 37,810 |
| bags/125ms @10% (p99) | 235 | 7 | 2,381 | 917 | 3,781 |

### Paced run: 50 bags, 60 s wall, true 8 Hz (125 ms period)

| metric | a1 immutable-builder | b SoA+log+snap/8 |
|---|---|---|
| ticks | 480 | 480 |
| missed deadlines | 0 | 0 |
| work ms/tick p50 | 2.04 | 0.21 |
| work ms/tick p99 | 20.70 | 1.01 |
| work ms/tick max | 25.44 | 18.28 |
| ticks with GC pause >1 ms | 34 | 2 |
| max GC pause in a tick ms | 17.71 | 17.90 |
| GC gen0/gen1/gen2 | 37/36/3 | 3/3/1 |
| total GC pause ms | 531.0 | 24.0 |

## Notes
- Times are Stopwatch ticks per Step() call; the per-step timestamp overhead (~25 ns) is included in every variant.
- Retained growth = live heap after forced GC minus live heap before the timed window (event log / snapshots / retained event objects). Immutable variants retain every event as a heap object (SimEventObj) in a per-step log; a1/a2 do NOT retain old states (only the current one).
- SimEvent (20 B struct) vs SimEventObj (record class, ~40 B + 8 B array slot): this alone changes the log footprint ~2.4x.
- a2 ran 3 trials (full mode) because it is slow; snapshots in b: ImmutableArray<int> x2 (plaque,item) = 8 KB each.
- BagSim.Step(version, intents) is the pure-looking facade (version, intents) -> (version', event range); intents are accepted but unused in this workload.
- Paced run: 60 s wall, 50 bags/variant, work measured per 125 ms tick; GC pauses attributed per tick via GC.GetTotalPauseDuration deltas.


