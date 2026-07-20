# Performance benchmark

How launch time and memory are measured, what the budgets are, and how to reproduce a run.
This closes the `docs/OPTIMIZATION.md` backlog item asking for measured budgets for cold start
and steady-state memory.

## What is measured, and why those metrics

The metrics follow the Windows app performance guidance: **time to first frame** for launch and
**working set / private bytes** for memory, taken from a Release build of the real shipping
binary.

| Metric | Where it comes from |
|---|---|
| Cold / warm start | Process creation → first composited frame, measured in-app |
| Launch phase breakdown | `managedEntry`, `hostStarted`, `windowCreated`, `windowActivated`, `firstFrame` |
| Idle working set / private bytes | Sampled externally from the process, 15 s after first frame |
| Managed heap | `GC.GetTotalMemory` at first frame (only observable in-process) |
| Handles / threads | Sampled with the memory, as a leak tripwire |
| Graceful shutdown | Tray Exit command → process exit |

**Launch time is measured from `Process.StartTime`, not from a stopwatch started in managed
code.** On a cold start most of the wall clock is CLR + Windows App SDK bootstrap that runs
*before* the `App` constructor; a stopwatch started there reports a number no user experiences.
The gap before `managedEntry` in the report is exactly that bootstrap cost.

## Budgets

Ratified against the 2026-07-20 baseline below. `Target` is what we hold; `Fail` is the line
that makes a build unacceptable — the launch thresholds sit under the Store certification
requirement that an app be responsive within 5 s.

| Metric | Target | Fail |
|---|---:|---:|
| Cold start to first frame | 2000 ms | 5000 ms |
| Warm start to first frame | 1200 ms | 2500 ms |
| Idle working set | 200 MB | 350 MB |
| Idle private bytes | 180 MB | 320 MB |
| Managed heap at first frame | 40 MB | 80 MB |
| Graceful shutdown | 1500 ms | 4000 ms |

Budgets live in the `$Budgets` table at the top of `scripts/Measure-Performance.ps1`. Re-ratify
them when a run establishes a new baseline; don't quietly loosen one to make a run pass.

## Running it

```powershell
# Release + unpackaged: the plain build has no Windows App SDK bootstrapper and dies on launch.
dotnet build ConnectOnion.WinUIClient\ConnectOnion.WinUIClient.csproj -c Release -p:Platform=x64 -p:RunUnpackaged=true

powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Measure-Performance.ps1
```

Useful switches:

- `-Iterations 10` — more samples (default 5).
- `-ClearStandbyList` — purge the OS standby list before each launch, making **every** iteration
  a true cold start. Needs an elevated shell; without it only iteration 1 is cold-ish and the
  report says so.
- `-EnforceBudgets` — exit non-zero when a median breaches its `Fail` threshold. Off by default.
- `-UseRealDataRoot` — measure against `%AppData%\ConnectOnion` instead of a throwaway data root.
- `-AllowDebugBuild` — required to benchmark a Debug build, whose numbers are not budgets.

Each run writes `TestResults\perf\<timestamp>\` containing `report.md`, `results.json`, and the
per-iteration `startup-N.json`.

Two things the harness has to get right, both of which cost a wrong measurement first:

- **The app is single-instance.** A second launch redirects to the running one and exits
  immediately, which records as an absurdly fast startup — so every iteration kills leftovers
  first.
- **`WM_CLOSE` is not the exit path.** `MainWindow.Tray.cs` intercepts it and shows a modal
  "Minimize to tray / Exit" dialog, so `Process.CloseMainWindow()` leaves the app alive waiting
  on a human and the measured "shutdown" is really the harness's own timeout. The harness posts
  `WM_COMMAND(1002)` to the hidden `ConnectOnionTrayHelper` message window — the same thing the
  tray menu's Exit item does — which routes to `MainWindow.ExitApplication`.

## Baseline — 2026-07-20

Release x64 unpackaged, Windows 11 Pro 10.0.26200, 32 cores, 31.6 GB RAM, 5 iterations, memory
sampled 15 s after first frame, standby list not purged.

| Metric | Median | Min | Max |
|---|---:|---:|---:|
| Cold start (n=1) | 850 ms | — | — |
| Warm start (n=4) | 801 ms | 798 ms | 812 ms |
| Idle working set | 190.2 MB | 189.0 MB | 191.1 MB |
| Idle private bytes | 119.2 MB | 116.8 MB | 119.9 MB |
| Managed heap | 3.1 MB | — | — |
| Handles / threads | 1337 / 74 | — | — |
| Graceful shutdown | 153 ms | 150 ms | 158 ms |

Phase breakdown of a representative launch (ms since process start):

| managedEntry | hostStarted | windowCreated | windowActivated | firstFrame |
|---:|---:|---:|---:|---:|
| 109 | 441 (+332) | 698 (+257) | 715 (+17) | 798 (+83) |

Reading of the baseline:

- **The two costs that matter are host start (~330 ms) and window construction (~260 ms)**,
  together ~75% of launch. Managed entry is ~110 ms and activation → first frame ~85 ms; neither
  is worth optimizing before the other two.
- **Working set ~190 MB against a ~3 MB managed heap.** Almost none of the footprint is app
  objects — it is the WinUI 3 / Windows App SDK runtime and its native surfaces. Optimizing C#
  allocations will not move this number; reducing what is loaded at startup might.
- **Shutdown is ~150 ms**, comfortably inside the app's own 3 s + 1 s drain budgets. Note this
  measures an idle app; the ~1 s close recorded in earlier investigation involves in-flight
  presence probes.
- **A first-ever launch of a freshly built binary took 10.8 s** in an earlier trial (cold disk,
  first JIT, first SQLite create). That is not representative of a user's cold start and is not
  what the cold budget is written against, but it is the number to remember when someone reports
  "the first launch after install is slow".

## Optimizations tried and rejected

Both were measured with the harness above and **neither reduced time to first frame**. They are
recorded here so the next person spends their time somewhere else.

**Deferring the modal overlays (`x:Load="False"`).** `MainWindow.xaml` builds three full-window
modals eagerly — settings, keyboard shortcuts, About — and `SettingsOverlay` alone hosts the
entire `SettingsPage` and resolves its view model from the container. Deferring them cut
`MainWindow.InitializeComponent` from 189 ms to 110 ms and idle working set from ~190 MB to
~181 MB, but **time to first frame did not move** (the saving reappeared elsewhere, and total
launch stayed inside run-to-run noise). It also did not work: `FindName` returns null for these
elements from both `RootGrid` and the direct parent panel, so the generated field stayed null and
clicking Settings silently did nothing. Reverted — a ~9 MB working-set win did not justify
replacing declarative XAML with hand-rolled realization, and the latency win was zero.

**Starting the Generic Host concurrently with window construction.** `OnLaunched` awaits
`_host.StartAsync()` before building the window, and host start is ~110 ms of SQLite reads plus
OS notification registration that the window does not depend on. Overlapping them changed
nothing measurable, and the phase report showed why: `hostStarted` and `windowCreated` came out
with **identical** timestamps on every iteration, because each hosted service's `StartAsync`
completes synchronously on the calling thread (the SQLite reads return inline), so the returned
task is already complete and there is nothing to overlap. Reverted — it moved `MainWindow` and
`Closed` assignment after an await and let the window exist before its services had loaded, for
no gain. Making it real would require pushing host start onto a background thread, which OS
notification registration is not obviously safe for.

**What the numbers say about where time actually goes.** Warm launch is ~800 ms: ~120 ms CLR +
Windows App SDK bootstrap before any app code runs, ~330 ms of App constructor and host start,
~260 ms of window construction, ~85 ms from activation to first frame. The two big middle
segments are UI-thread-bound and serialized; nothing above shortened them. Memory is ~190 MB
working set against a ~3 MB managed heap, so the footprint is the WinUI 3 runtime, not app
objects — allocation tuning cannot move it.

## Limitations

- Without `-ClearStandbyList` (elevated), only the first iteration is meaningfully cold.
- Steady-state memory is sampled at idle with no conversation loaded. Restoring a large
  conversation, scrolling, and searching are **not** covered — those still want an ETW capture
  (see `docs/OPTIMIZATION.md`) and remain on the backlog as budgets.
- Not run in CI: it needs a real desktop session, like the FlaUI smoke test.
