<#
.SYNOPSIS
    Launch-time and memory benchmark for the ConnectOnion desktop client.

.DESCRIPTION
    Repeatedly launches the app, reads the startup report it writes (see
    Diagnostics/StartupTelemetry.cs), samples process memory once the app has settled at idle,
    then closes it and times the shutdown. Results are aggregated (median / min / max / p95),
    compared against the budgets below, and written out as JSON + Markdown.

    Metrics follow the Windows app performance guidance: time to first frame for launch,
    working set and private bytes for memory, measured on a Release build of the real shipping
    binary. A Debug build is refused unless -AllowDebugBuild is passed, because JIT-tiering and
    disabled optimizations make its numbers meaningless as a budget.

    Each iteration runs against a throwaway data root (CONNECTONION_DATA_ROOT), so a large
    local conversation history can't skew the result and the benchmark can't touch real data.
    Pass -UseRealDataRoot to measure launch against your actual profile instead.

.PARAMETER Exe
    Path to ConnectOnion.WinUIClient.exe. Defaults to the Release x64 unpackaged build.

.PARAMETER Iterations
    Measured launches. Default 5; the first is reported separately as the cold-ish run.

.PARAMETER SettleSeconds
    Idle time after first frame before memory is sampled. Startup work (presence probes, the
    first SQLite open) is still running at first frame, so sampling immediately understates
    steady state. Default 15.

.PARAMETER ClearStandbyList
    Purge the OS standby list before each launch to approximate a true cold start. Requires an
    elevated shell; without it, iterations after the first are warm and are labelled as such.

.PARAMETER EnforceBudgets
    Exit non-zero when a median metric breaches its Fail threshold. Off by default so the first
    run can establish the baseline rather than fail against a guess.

.EXAMPLE
    pwsh scripts/Measure-Performance.ps1
.EXAMPLE
    pwsh scripts/Measure-Performance.ps1 -Iterations 10 -ClearStandbyList -EnforceBudgets
#>
[CmdletBinding()]
param(
    [string] $Exe,
    [int]    $Iterations = 5,
    [int]    $SettleSeconds = 15,
    [int]    $LaunchTimeoutSeconds = 60,
    [string] $OutDir,
    [switch] $ClearStandbyList,
    [switch] $UseRealDataRoot,
    [switch] $AllowDebugBuild,
    [switch] $EnforceBudgets
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$ProcessName = 'ConnectOnion.WinUIClient'

# Budgets, in the units each metric is reported in. "Target" is what we want to hold; "Fail" is
# the line that makes a build unacceptable. Launch numbers are anchored on the Store
# certification requirement that an app must be responsive within 5 s, with a target well under
# it; memory budgets are the current shipped behaviour rounded up, and should be re-ratified
# whenever a run establishes a new baseline (see docs/PERFORMANCE.md).
$Budgets = @(
    [pscustomobject]@{ Metric = 'ColdStartMs';      Label = 'Cold start to first frame'; Target = 2000; Fail = 5000; Unit = 'ms' }
    [pscustomobject]@{ Metric = 'WarmStartMs';      Label = 'Warm start to first frame'; Target = 1200; Fail = 2500; Unit = 'ms' }
    [pscustomobject]@{ Metric = 'IdleWorkingSetMb'; Label = 'Idle working set';          Target = 200;  Fail = 350;  Unit = 'MB' }
    [pscustomobject]@{ Metric = 'IdlePrivateMb';    Label = 'Idle private bytes';        Target = 180;  Fail = 320;  Unit = 'MB' }
    [pscustomobject]@{ Metric = 'ManagedHeapMb';    Label = 'Managed heap at first frame'; Target = 40; Fail = 80;   Unit = 'MB' }
    [pscustomobject]@{ Metric = 'ShutdownMs';       Label = 'Graceful shutdown';         Target = 1500; Fail = 4000; Unit = 'ms' }
)

# --- helpers -----------------------------------------------------------------

function Get-Median {
    param([double[]] $Values)
    if (-not $Values -or $Values.Count -eq 0) { return $null }
    # @() matters: Sort-Object unwraps a one-element pipeline to a scalar, and a scalar has no
    # .Count under StrictMode. A single cold run is the normal case, so this is not an edge case.
    $sorted = @($Values | Sort-Object)
    $mid = [int][math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2 -eq 1) { return [double]$sorted[$mid] }
    return ([double]$sorted[$mid - 1] + [double]$sorted[$mid]) / 2
}

function Get-Percentile {
    param([double[]] $Values, [double] $Percentile)
    if (-not $Values -or $Values.Count -eq 0) { return $null }
    $sorted = @($Values | Sort-Object)
    # Nearest-rank: with 5 samples p95 is simply the max, which is the honest answer at this
    # sample size — interpolating would invent precision the run doesn't have.
    $rank = [int][math]::Ceiling(($Percentile / 100.0) * $sorted.Count)
    if ($rank -lt 1) { $rank = 1 }
    return [double]$sorted[$rank - 1]
}

function Stop-RunningInstances {
    # The app is single-instance: a second launch redirects to the running one and exits
    # immediately, which would otherwise be recorded as an absurdly fast startup.
    $running = @(Get-Process -Name $ProcessName -ErrorAction SilentlyContinue)
    if ($running.Count -eq 0) { return }
    Write-Verbose "Stopping $($running.Count) running instance(s) before measuring"
    foreach ($p in $running) {
        try { $p.Kill() } catch { }
    }
    foreach ($p in $running) {
        try { $null = $p.WaitForExit(5000) } catch { }
    }
}

function Initialize-Win32 {
    if ('Perf.Win32' -as [type]) { return }
    Add-Type -Namespace Perf -Name Win32 -MemberDefinition @'
[DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr childAfter, string className, string windowName);

[DllImport("user32.dll", SetLastError = true)]
public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

[DllImport("user32.dll", SetLastError = true)]
public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
'@
}

function Request-AppExit {
    <#
        Asks the app to exit the way a user does from the tray menu.

        WM_CLOSE is NOT the exit path here: MainWindow.Tray.cs intercepts it and shows a modal
        "Minimize to tray / Exit" ContentDialog, so Process.CloseMainWindow() leaves the process
        alive waiting on a human and the measured "shutdown" is really the harness's own wait
        timeout. The tray menu's Exit item posts WM_COMMAND(1002) to the hidden message-only
        window of class ConnectOnionTrayHelper, which routes to MainWindow.ExitApplication —
        that is the real, dialog-free teardown, and it is what this measures.
    #>
    param([Diagnostics.Process] $Process)

    Initialize-Win32

    $HwndMessage = [IntPtr]::new(-3)
    $WmCommand = 0x0111
    $ExitCommandId = 1002

    # The helper window is created while MainWindow is constructed, so by first frame it exists;
    # retry anyway rather than falling back on the first miss.
    foreach ($attempt in 1..20) {
        $hwnd = [IntPtr]::Zero
        while ($true) {
            $hwnd = [Perf.Win32]::FindWindowEx($HwndMessage, $hwnd, 'ConnectOnionTrayHelper', $null)
            if ($hwnd -eq [IntPtr]::Zero) { break }

            $owner = 0
            $null = [Perf.Win32]::GetWindowThreadProcessId($hwnd, [ref] $owner)
            if ($owner -eq $Process.Id) {
                $null = [Perf.Win32]::PostMessage($hwnd, $WmCommand, [IntPtr]$ExitCommandId, [IntPtr]::Zero)
                return $true
            }
        }
        Start-Sleep -Milliseconds 100
    }

    return $false
}

$script:StandbyPurgeAvailable = $null
function Clear-StandbyList {
    # Purging the standby list is what makes a repeat launch genuinely cold: otherwise the app's
    # binaries stay in the OS file cache and every run after the first measures warm I/O.
    if ($null -eq $script:StandbyPurgeAvailable) {
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
        $principal = New-Object Security.Principal.WindowsPrincipal($identity)
        $script:StandbyPurgeAvailable = $principal.IsInRole(
            [Security.Principal.WindowsBuiltInRole]::Administrator)

        if ($script:StandbyPurgeAvailable) {
            if (-not ('Perf.MemoryList' -as [type])) {
                Add-Type -Namespace Perf -Name MemoryList -MemberDefinition @'
[DllImport("ntdll.dll")]
public static extern int NtSetSystemInformation(int InfoClass, ref int Info, int Length);
'@
            }
        } else {
            Write-Warning 'Standby-list purge needs an elevated shell; runs will be warm.'
        }
    }

    if (-not $script:StandbyPurgeAvailable) { return $false }
    try {
        # SystemMemoryListInformation (80) / MemoryPurgeStandbyList (4)
        $command = 4
        $null = [Perf.MemoryList]::NtSetSystemInformation(80, [ref] $command, 4)
        return $true
    } catch {
        $script:StandbyPurgeAvailable = $false
        Write-Warning "Standby-list purge failed: $($_.Exception.Message)"
        return $false
    }
}

function Invoke-MeasuredLaunch {
    param(
        [string] $ExePath,
        [int]    $Index,
        [string] $ReportPath,
        [string] $DataRoot,
        [bool]   $Cold
    )

    Stop-RunningInstances
    if (Test-Path $ReportPath) { Remove-Item $ReportPath -Force }
    if ($DataRoot -and (Test-Path $DataRoot)) { Remove-Item $DataRoot -Recurse -Force }

    $env:CONNECTONION_PERF_OUT = $ReportPath
    if ($DataRoot) { $env:CONNECTONION_DATA_ROOT = $DataRoot }
    else { Remove-Item Env:CONNECTONION_DATA_ROOT -ErrorAction SilentlyContinue }

    if ($Cold) { $null = Clear-StandbyList }

    $proc = Start-Process -FilePath $ExePath -PassThru
    try {
        # The app writes its report atomically at first frame; polling for the file is more
        # reliable than watching for a window handle, which appears before anything is drawn.
        $deadline = (Get-Date).AddSeconds($LaunchTimeoutSeconds)
        while (-not (Test-Path $ReportPath)) {
            if ($proc.HasExited) {
                throw "Iteration $Index exited (code $($proc.ExitCode)) before reporting startup."
            }
            if ((Get-Date) -gt $deadline) {
                throw "Iteration $Index did not reach first frame within $LaunchTimeoutSeconds s."
            }
            Start-Sleep -Milliseconds 50
        }

        # Test-Path can see the report the instant File.Move creates it, a moment before the
        # move releases its handle — so the first read can fail with a sharing violation even
        # though the document is complete. Retry briefly rather than losing the iteration.
        $report = $null
        foreach ($attempt in 1..10) {
            try {
                $report = Get-Content $ReportPath -Raw -ErrorAction Stop | ConvertFrom-Json
                break
            } catch {
                if ($attempt -eq 10) { throw }
                Start-Sleep -Milliseconds 100
            }
        }

        Start-Sleep -Seconds $SettleSeconds
        $proc.Refresh()
        $idleWorkingSet = $proc.WorkingSet64
        $idlePrivate = $proc.PrivateMemorySize64
        $peakWorkingSet = $proc.PeakWorkingSet64
        $handles = $proc.HandleCount
        $threads = $proc.Threads.Count

        # Exit the app the way the tray menu does rather than killing it: shutdown drains runs
        # and stops the Generic Host, and that cost is part of the app's felt performance too.
        $requested = Request-AppExit -Process $proc
        if (-not $requested) {
            Write-Warning "Iteration $Index : tray helper window not found; cannot measure shutdown."
        }

        $shutdown = [Diagnostics.Stopwatch]::StartNew()
        $exited = $false
        if ($requested) { $exited = $proc.WaitForExit(15000) }
        $shutdown.Stop()
        if (-not $exited) {
            Write-Warning "Iteration $Index did not exit gracefully; killing."
            try { $proc.Kill() } catch { }
        }

        $marks = @{}
        foreach ($mark in $report.marks) { $marks[$mark.phase] = [double]$mark.elapsedMs }

        return [pscustomobject]@{
            Iteration        = $Index
            Mode             = $(if ($Cold) { 'cold' } else { 'warm' })
            Configuration    = $report.configuration
            Packaged         = $report.packaged
            TimeToFirstFrameMs = $marks['firstFrame']
            ManagedEntryMs   = $marks['managedEntry']
            HostStartedMs    = $marks['hostStarted']
            WindowCreatedMs  = $marks['windowCreated']
            WindowActivatedMs = $marks['windowActivated']
            FirstFrameWorkingSetMb = [math]::Round($report.memory.workingSetBytes / 1MB, 1)
            ManagedHeapMb    = [math]::Round($report.memory.managedHeapBytes / 1MB, 1)
            IdleWorkingSetMb = [math]::Round($idleWorkingSet / 1MB, 1)
            IdlePrivateMb    = [math]::Round($idlePrivate / 1MB, 1)
            PeakWorkingSetMb = [math]::Round($peakWorkingSet / 1MB, 1)
            HandleCount      = $handles
            ThreadCount      = $threads
            ShutdownMs       = [math]::Round($shutdown.Elapsed.TotalMilliseconds, 0)
            GracefulExit     = $exited
        }
    } finally {
        if (-not $proc.HasExited) { try { $proc.Kill() } catch { } }
        Remove-Item Env:CONNECTONION_PERF_OUT -ErrorAction SilentlyContinue
        Remove-Item Env:CONNECTONION_DATA_ROOT -ErrorAction SilentlyContinue
    }
}

function Get-Stats {
    param([string] $Name, [double[]] $Values, [string] $Unit)
    $clean = @($Values | Where-Object { $null -ne $_ })
    if ($clean.Count -eq 0) { return $null }
    return [pscustomobject]@{
        Metric = $Name
        Unit   = $Unit
        Count  = $clean.Count
        Median = [math]::Round((Get-Median $clean), 1)
        Min    = [math]::Round(($clean | Measure-Object -Minimum).Minimum, 1)
        Max    = [math]::Round(($clean | Measure-Object -Maximum).Maximum, 1)
        P95    = [math]::Round((Get-Percentile $clean 95), 1)
    }
}

# --- resolve inputs ----------------------------------------------------------

if (-not $Exe) {
    $candidates = @(
        "$RepoRoot\ConnectOnion.WinUIClient\bin\x64\Release\net10.0-windows10.0.19041.0\win-x64\$ProcessName.exe"
        "$RepoRoot\ConnectOnion.WinUIClient\bin\x64\Debug\net10.0-windows10.0.19041.0\win-x64\$ProcessName.exe"
    )
    $Exe = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}

if (-not $Exe -or -not (Test-Path $Exe)) {
    throw @"
No app executable found. Build one first:
  dotnet build ConnectOnion.WinUIClient\ConnectOnion.WinUIClient.sln -c Release -p:Platform=x64
"@
}
$Exe = (Resolve-Path $Exe).Path
if ($Exe -match '\\Debug\\' -and -not $AllowDebugBuild) {
    throw "Refusing to benchmark a Debug build ($Exe). Build Release, or pass -AllowDebugBuild."
}

if (-not $OutDir) {
    $OutDir = Join-Path $RepoRoot ("TestResults\perf\" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
$null = New-Item -ItemType Directory -Force -Path $OutDir

$dataRoot = $null
if (-not $UseRealDataRoot) { $dataRoot = Join-Path $OutDir 'data-root' }

Write-Host "Executable : $Exe"
Write-Host "Iterations : $Iterations (settle ${SettleSeconds}s)"
Write-Host "Data root  : $(if ($dataRoot) { $dataRoot } else { 'real profile (%AppData%\ConnectOnion)' })"
Write-Host "Output     : $OutDir"
Write-Host ''

# --- run ---------------------------------------------------------------------

$results = @()
for ($i = 1; $i -le $Iterations; $i++) {
    # Without a standby purge only the first launch is meaningfully cold, so later iterations
    # are labelled warm and aggregated separately rather than averaged together.
    $cold = $ClearStandbyList.IsPresent -or ($i -eq 1)
    Write-Host ("Iteration {0}/{1} ({2}) ..." -f $i, $Iterations, $(if ($cold) { 'cold' } else { 'warm' })) -NoNewline
    $result = Invoke-MeasuredLaunch -ExePath $Exe -Index $i -DataRoot $dataRoot -Cold $cold `
        -ReportPath (Join-Path $OutDir "startup-$i.json")
    $results += $result
    Write-Host (" {0} ms to first frame, {1} MB idle" -f `
        [math]::Round($result.TimeToFirstFrameMs, 0), $result.IdleWorkingSetMb)
}

Stop-RunningInstances

# --- aggregate ---------------------------------------------------------------

$coldRuns = @($results | Where-Object { $_.Mode -eq 'cold' })
$warmRuns = @($results | Where-Object { $_.Mode -eq 'warm' })

$stats = @()
$stats += Get-Stats 'ColdStartMs'      @($coldRuns | ForEach-Object { $_.TimeToFirstFrameMs }) 'ms'
$stats += Get-Stats 'WarmStartMs'      @($warmRuns | ForEach-Object { $_.TimeToFirstFrameMs }) 'ms'
$stats += Get-Stats 'IdleWorkingSetMb' @($results | ForEach-Object { $_.IdleWorkingSetMb }) 'MB'
$stats += Get-Stats 'IdlePrivateMb'    @($results | ForEach-Object { $_.IdlePrivateMb }) 'MB'
$stats += Get-Stats 'ManagedHeapMb'    @($results | ForEach-Object { $_.ManagedHeapMb }) 'MB'
$stats += Get-Stats 'PeakWorkingSetMb' @($results | ForEach-Object { $_.PeakWorkingSetMb }) 'MB'
# Only graceful exits carry a real shutdown duration — a killed run reports the wait timeout,
# which would drag the median toward the timeout rather than toward the app's behaviour. The
# count of non-graceful exits is reported separately instead of being averaged in.
$gracefulRuns = @($results | Where-Object { $_.GracefulExit })
$stats += Get-Stats 'ShutdownMs'       @($gracefulRuns | ForEach-Object { $_.ShutdownMs }) 'ms'
$stats = @($stats | Where-Object { $null -ne $_ })

$verdicts = @()
foreach ($budget in $Budgets) {
    $stat = $stats | Where-Object { $_.Metric -eq $budget.Metric } | Select-Object -First 1
    if (-not $stat) { continue }
    $status = 'PASS'
    if ($stat.Median -gt $budget.Fail) { $status = 'FAIL' }
    elseif ($stat.Median -gt $budget.Target) { $status = 'OVER TARGET' }
    $verdicts += [pscustomobject]@{
        Metric = $budget.Metric; Label = $budget.Label; Unit = $budget.Unit
        Median = $stat.Median; Target = $budget.Target; Fail = $budget.Fail; Status = $status
    }
}

$machine = [pscustomobject]@{
    Machine       = $env:COMPUTERNAME
    OS            = (Get-CimInstance Win32_OperatingSystem).Caption
    OSVersion     = [Environment]::OSVersion.Version.ToString()
    Cores         = [Environment]::ProcessorCount
    TotalMemoryGb = [math]::Round((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory / 1GB, 1)
    Configuration = $results[0].Configuration
    Packaged      = $results[0].Packaged
    StandbyPurged = [bool]$script:StandbyPurgeAvailable
    NonGracefulExits = $results.Count - $gracefulRuns.Count
    Executable    = $Exe
    CapturedUtc   = (Get-Date).ToUniversalTime().ToString('o')
}

# --- report ------------------------------------------------------------------

$jsonPath = Join-Path $OutDir 'results.json'
[pscustomobject]@{
    environment = $machine; budgets = $verdicts; statistics = $stats; iterations = $results
} | ConvertTo-Json -Depth 6 | Set-Content -Path $jsonPath -Encoding utf8

$md = New-Object Text.StringBuilder
$null = $md.AppendLine('# Performance benchmark')
$null = $md.AppendLine()
$null = $md.AppendLine("Captured $($machine.CapturedUtc) on $($machine.Machine) — $($machine.OS) $($machine.OSVersion), $($machine.Cores) cores, $($machine.TotalMemoryGb) GB RAM.")
$null = $md.AppendLine()
$null = $md.AppendLine("Build: **$($machine.Configuration)**, packaged: $($machine.Packaged), standby list purged between launches: $($machine.StandbyPurged).")
if (-not $machine.StandbyPurged) {
    $null = $md.AppendLine()
    $null = $md.AppendLine('> Standby list was not purged (needs an elevated shell), so only iteration 1 is a true cold start.')
}
if ($machine.NonGracefulExits -gt 0) {
    $null = $md.AppendLine()
    $null = $md.AppendLine("> $($machine.NonGracefulExits) of $($results.Count) iteration(s) had to be killed after the tray Exit command; those are excluded from the shutdown statistic.")
}
$null = $md.AppendLine()
$null = $md.AppendLine('## Budgets')
$null = $md.AppendLine()
$null = $md.AppendLine('| Metric | Median | Target | Fail | Status |')
$null = $md.AppendLine('|---|---:|---:|---:|---|')
foreach ($v in $verdicts) {
    $null = $md.AppendLine("| $($v.Label) | $($v.Median) $($v.Unit) | $($v.Target) $($v.Unit) | $($v.Fail) $($v.Unit) | $($v.Status) |")
}
$null = $md.AppendLine()
$null = $md.AppendLine('## Distribution')
$null = $md.AppendLine()
$null = $md.AppendLine('| Metric | n | Median | Min | Max | P95 |')
$null = $md.AppendLine('|---|---:|---:|---:|---:|---:|')
foreach ($s in $stats) {
    $null = $md.AppendLine("| $($s.Metric) ($($s.Unit)) | $($s.Count) | $($s.Median) | $($s.Min) | $($s.Max) | $($s.P95) |")
}
$null = $md.AppendLine()
$null = $md.AppendLine('## Launch phase breakdown')
$null = $md.AppendLine()
$null = $md.AppendLine('Milliseconds since process start. The gap before `managedEntry` is CLR + Windows App SDK bootstrap.')
$null = $md.AppendLine()
$null = $md.AppendLine('| # | Mode | managedEntry | hostStarted | windowCreated | windowActivated | firstFrame |')
$null = $md.AppendLine('|---:|---|---:|---:|---:|---:|---:|')
foreach ($r in $results) {
    $null = $md.AppendLine("| $($r.Iteration) | $($r.Mode) | $([math]::Round($r.ManagedEntryMs,0)) | $([math]::Round($r.HostStartedMs,0)) | $([math]::Round($r.WindowCreatedMs,0)) | $([math]::Round($r.WindowActivatedMs,0)) | $([math]::Round($r.TimeToFirstFrameMs,0)) |")
}
$null = $md.AppendLine()
$null = $md.AppendLine('## Per-iteration resources')
$null = $md.AppendLine()
$null = $md.AppendLine("Memory sampled $SettleSeconds s after first frame.")
$null = $md.AppendLine()
$null = $md.AppendLine('| # | Idle WS (MB) | Idle private (MB) | Peak WS (MB) | Managed heap (MB) | Handles | Threads | Shutdown (ms) |')
$null = $md.AppendLine('|---:|---:|---:|---:|---:|---:|---:|---:|')
foreach ($r in $results) {
    $null = $md.AppendLine("| $($r.Iteration) | $($r.IdleWorkingSetMb) | $($r.IdlePrivateMb) | $($r.PeakWorkingSetMb) | $($r.ManagedHeapMb) | $($r.HandleCount) | $($r.ThreadCount) | $($r.ShutdownMs) |")
}

$mdPath = Join-Path $OutDir 'report.md'
$md.ToString() | Set-Content -Path $mdPath -Encoding utf8

Write-Host ''
$verdicts | Format-Table Label, @{ N = 'Median'; E = { "$($_.Median) $($_.Unit)" } }, Status -AutoSize
Write-Host "Report : $mdPath"
Write-Host "Raw    : $jsonPath"

$failed = @($verdicts | Where-Object { $_.Status -eq 'FAIL' })
if ($failed.Count -gt 0) {
    Write-Warning "$($failed.Count) metric(s) over the failure threshold: $($failed.Metric -join ', ')"
    if ($EnforceBudgets) { exit 1 }
}
exit 0
