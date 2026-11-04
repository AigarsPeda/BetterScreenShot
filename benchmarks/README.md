# Performance baseline

Run benchmarks in a Release build on the same machine with other demanding apps idle. Times vary with CPU load, display size, and graphics drivers. Compare medians and repeat a run before treating a small difference as a gain.

## Recording

```powershell
dotnet run --project Tests/BetterScreenShot.RegressionTests.csproj -c Release -- --record-benchmark
```

The runner warms each workload, then records three samples. It uses a fixed patterned frame, changes one vertical line per frame, and writes the recording through the production video or GIF path. It deletes the output files after measuring them. The capture callback clones the frame buffer, so allocations include that clone. The benchmark excludes desktop capture, UI rendering, and preview playback. Wall time includes the normal 30 fps video or 8 fps GIF pacing. CPU time and allocations are the best measures for code changes in this path. Output size is a guard against accidentally changing encoding behavior or quality.

Each run writes raw samples to `artifacts/performance/recording-<timestamp>.json`. The first run on 2026-11-03, with production code at commit `2b2be4a`, is saved as [recording-baseline-2026-11-03.json](recording-baseline-2026-11-03.json). Compare a new result with:

```powershell
$latest = Get-ChildItem artifacts/performance/recording-*.json | Sort-Object LastWriteTime | Select-Object -Last 1
./scripts/Compare-RecordingBenchmarks.ps1 benchmarks/recording-baseline-2026-11-03.json $latest.FullName
```

On the baseline machine (.NET 8.0.33, Windows 10.0.26200, 16 logical processors):

| Workload | Median wall | Median CPU | Median allocated | Output |
| --- | ---: | ---: | ---: | ---: |
| High quality video, 1280×720, 60 frames | 2136 ms | 844 ms | 221.2 MB | 0.37 MB |
| GIF, 640×360, 24 frames | 3186 ms | 406 ms | 24.1 MB | 2.26 MB |

An immediate repeat on unchanged code measured 1047 ms video CPU and 547 ms GIF CPU, versus 844 ms and 406 ms in the first run. GIF wall time rose from 3186 ms to 3377 ms. Treat a single CPU-time difference of this size as inconclusive; compare several runs under similar machine load. Allocated bytes and output sizes were stable across both runs.

## Selection overlay

```powershell
dotnet run --project Tests/BetterScreenShot.RegressionTests.csproj -c Release -- --overlay-benchmark
dotnet run --project Tests/BetterScreenShot.RegressionTests.csproj -c Release -- --overlay-tests
```

The benchmark warms the overlay, then measures three 120-move area drags on the primary monitor. It changes the selected rectangle and forces each pending paint, so each sample includes region updates and rendering. It excludes physical mouse input and the recording and scrolling paths. Run it on the same display with other demanding apps idle. The regression checks the first display in area and monitor modes and tests whether a previous border remains visible after shrinking a selection.

The [2026-11-04 baseline](overlay-baseline-2026-11-04.txt) used production commit `7cf4033` on a 1536×960 primary monitor. Median update time across its three trials was 0.82–0.88 ms; all trials allocated 0.11 MB. A pixel on the old border stayed blue after the region changed, then became gray on the next paint. The [post-change samples](overlay-after-2026-11-04.txt) show that the old border becomes gray during the same drag update. Median update time across repeated runs ranged from 0.94 to 1.83 ms, with unchanged allocations. Synchronous painting removes the visible stale border but adds latency and has more variable timing on this machine.

Run `--overlay-repeat-benchmark` to measure 120 updates with four updates per coordinate. Skipping unchanged selections reduced median trial wall time from 400.5 ms to 155.0 ms and allocations from 0.11 MB to 0.05 MB in the [recorded comparison](overlay-repeat-2026-11-04.txt). This synthetic workload has 75% duplicate positions; actual savings depend on the input events. Continuous movement still performs the same region update and synchronous paint. Its final three trials measured 0.98, 1.18, and 1.02 ms median per update with 0.11 MB allocated. Desktop load varied during these measurements, so the duplicate-coordinate result does not establish a general drag speedup.

## Other paths

Existing benchmark commands, measured on the same machine on 2026-11-03:

| Command | Baseline result |
| --- | --- |
| `--scroll-benchmark` | Three 18-step 1200×800 stitches: 230 ms, 3.62 MB allocated |
| `--editor-benchmark` | 4K stroke updates with 300 annotations: 0.071 ms median, 0.124 ms p95; 4K composite: 201 ms median; PNG encode: 102 ms median |
| `--ocr-benchmark` | 1080p text: Auto 3211 ms wall / 8750 ms CPU; English 1478 ms wall / 1406 ms CPU |
| `--ocr-ui-benchmark` | 3710 ms recognition; 48.7 ms maximum and 32.6 ms p95 UI heartbeat gap |

Run these with `dotnet run --project Tests/BetterScreenShot.RegressionTests.csproj -c Release -- <command>`. OCR writes detailed samples to `obj/ocr-benchmark.json`; copy that file before a change if you need a per-stage comparison. The editor and scrolling benchmarks currently print summaries only.

## Review notes

The next recording measurement should include desktop frame capture. The current recording benchmark isolates encoding and file generation, while production `AreaRecordingService.Capture` creates a bitmap and a full pixel array for every frame. A 1280×720 pixel array is about 3.7 MB, so buffer reuse is a possible memory improvement, but the existing benchmark cannot measure it.

GIF recording retains frames until the final save. The 20-second limit at 8 fps can hold 160 frames, so a longer GIF memory benchmark should precede a change to that design. The first GIF frame previously received an extra clone; removing it kept output identical in the 24-frame fixture, but its time and managed-allocation difference was smaller than run-to-run noise.

Automatic OCR runs several language passes, which explains its high CPU time. Fewer passes could hurt recognition accuracy; compare word results as well as timing before changing that path.
