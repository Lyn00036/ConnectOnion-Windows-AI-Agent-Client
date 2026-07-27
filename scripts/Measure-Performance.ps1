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
    Measured launches for the selected mode. Default and minimum 5.

.PARAMETER Mode
    One of WarmUnpackaged, ColdUnpackaged, WarmMsix, or ColdMsix. Cold modes require successful
    standby-list purge before every sample; MSIX modes resolve the installed package executable.

.PARAMETER EnforceBudgets
    Exit non-zero when a median metric breaches its Fail threshold. Off by default so the first
    run can establish the baseline rather than fail against a guess.

.EXAMPLE
    pwsh scripts/Measure-Performance.ps1
.EXAMPLE
    pwsh scripts/Measure-Performance.ps1 -Mode ColdUnpackaged -Iterations 5 -EnforceBudgets
#>
[CmdletBinding()]
param(
    [string] $Exe,
    [ValidateSet('WarmUnpackaged', 'ColdUnpackaged', 'WarmMsix', 'ColdMsix')]
    [string] $Mode = 'WarmUnpackaged',
    [int]    $Iterations = 5,
    [int]    $LaunchTimeoutSeconds = 60,
    [string] $OutDir,
    [string] $PackageName = '779a2035-a8d1-4bc8-91cf-82aecda14cc5',
    [string] $PackageExecutable = 'ConnectOnion.WinUIClient.exe',
    [string] $DatasetId = 'empty-isolated-profile',
    [string] $FixturePath,
    [switch] $RequireConversation,
    [switch] $UseRealDataRoot,
    [switch] $AllowDebugBuild,
    [switch] $EnforceBudgets
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$ProcessName = 'ConnectOnion.WinUIClient'

if ($Iterations -lt 5) {
    throw "At least five valid samples are required; -Iterations must be 5 or greater."
}

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

function Request-AppExit {
    <#
        Signals the one-shot named event armed by StartupTelemetry. The callback invokes the same
        MainWindow.ExitApplication path as File > Exit; no tray implementation detail is assumed.
    #>
    param([Threading.EventWaitHandle] $ExitEvent)
    return $ExitEvent.Set()
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
        $status = [Perf.MemoryList]::NtSetSystemInformation(80, [ref] $command, 4)
        if ($status -ne 0) {
            Write-Warning ("Standby-list purge returned NTSTATUS 0x{0:X8}." -f $status)
            return $false
        }
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
        [string] $FixturePath,
        [bool]   $Cold,
        [bool]   $ConversationRequired
    )

    Stop-RunningInstances
    if (Test-Path $ReportPath) { Remove-Item $ReportPath -Force }
    if ($DataRoot -and (Test-Path $DataRoot)) { Remove-Item $DataRoot -Recurse -Force }
    if ($DataRoot -and $FixturePath) {
        $null = New-Item -ItemType Directory -Force -Path $DataRoot
        Copy-Item -Path (Join-Path $FixturePath '*') -Destination $DataRoot -Recurse -Force
    }

    if ($Cold -and -not (Clear-StandbyList)) {
        throw 'Cold mode requires an elevated shell and a successful standby-list purge. No sample was recorded.'
    }

    $exitEventName = "Local\ConnectOnion.PerfExit.$([guid]::NewGuid().ToString('N'))"
    $exitEvent = [Threading.EventWaitHandle]::new(
        $false, [Threading.EventResetMode]::AutoReset, $exitEventName)
    $env:CONNECTONION_PERF_OUT = $ReportPath
    $env:CONNECTONION_PERF_EXIT_EVENT = $exitEventName
    if ($ConversationRequired) { $env:CONNECTONION_PERF_OPEN_CONVERSATION = '1' }
    else { Remove-Item Env:CONNECTONION_PERF_OPEN_CONVERSATION -ErrorAction SilentlyContinue }
    if ($DataRoot) { $env:CONNECTONION_DATA_ROOT = $DataRoot }
    else { Remove-Item Env:CONNECTONION_DATA_ROOT -ErrorAction SilentlyContinue }

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

        # Wait for actual readiness marks instead of sleeping for an arbitrary settling period.
        $requiredMarks = @('firstFrame', 'firstInteractive', 'sessionListLoaded', 'deferredStartupCompleted')
        if ($ConversationRequired) { $requiredMarks += 'firstConversationRendered' }
        while ($true) {
            $report = Get-Content $ReportPath -Raw | ConvertFrom-Json
            $observed = @($report.marks | ForEach-Object { $_.phase })
            if (@($requiredMarks | Where-Object { $_ -notin $observed }).Count -eq 0) { break }
            if ((Get-Date) -gt $deadline) {
                throw "Iteration $Index did not reach required readiness milestones: $($requiredMarks -join ', ')."
            }
            Start-Sleep -Milliseconds 50
        }
        $proc.Refresh()
        $idleWorkingSet = $proc.WorkingSet64
        $idlePrivate = $proc.PrivateMemorySize64
        $peakWorkingSet = $proc.PeakWorkingSet64
        $handles = $proc.HandleCount
        $threads = $proc.Threads.Count

        # Exit the app the way the tray menu does rather than killing it: shutdown drains runs
        # and stops the Generic Host, and that cost is part of the app's felt performance too.
        $requested = Request-AppExit -ExitEvent $exitEvent
        if (-not $requested) {
            Write-Warning "Iteration $Index : named exit event could not be signalled; cannot measure shutdown."
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
            FirstInteractiveMs = $marks['firstInteractive']
            SessionListLoadedMs = $marks['sessionListLoaded']
            FirstConversationRenderedMs = $marks['firstConversationRendered']
            DeferredStartupCompletedMs = $marks['deferredStartupCompleted']
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
        Remove-Item Env:CONNECTONION_PERF_EXIT_EVENT -ErrorAction SilentlyContinue
        Remove-Item Env:CONNECTONION_PERF_OPEN_CONVERSATION -ErrorAction SilentlyContinue
        Remove-Item Env:CONNECTONION_DATA_ROOT -ErrorAction SilentlyContinue
        $exitEvent.Dispose()
    }
}

function Format-OptionalMilliseconds {
    param($Value)
    if ($null -eq $Value) { return '' }
    return [math]::Round([double]$Value, 0)
}

function Get-Stats {
    param([string] $Name, [object[]] $Values, [string] $Unit)
    # Keep optional milestones optional. Binding directly to [double[]] converts a missing
    # conversation mark ($null) to 0.0 and fabricates a zero-millisecond render in the report.
    $clean = @($Values | Where-Object { $null -ne $_ } | ForEach-Object { [double]$_ })
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

$packageFullName = $null
if ($Mode -like '*Msix') {
    $package = Get-AppxPackage -Name $PackageName | Sort-Object Version -Descending | Select-Object -First 1
    if (-not $package) { throw "Installed MSIX package '$PackageName' was not found." }
    $packageFullName = $package.PackageFullName
    $Exe = Join-Path $package.InstallLocation $PackageExecutable
} elseif (-not $Exe) {
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
if ($FixturePath) {
    $FixturePath = (Resolve-Path $FixturePath).Path
    if ($UseRealDataRoot) { throw '-FixturePath cannot be combined with -UseRealDataRoot.' }
}

Write-Host "Executable : $Exe"
Write-Host "Mode       : $Mode"
Write-Host "Iterations : $Iterations (minimum valid sample count: 5)"
Write-Host "Data root  : $(if ($dataRoot) { $dataRoot } else { 'real profile (%AppData%\ConnectOnion)' })"
Write-Host "Output     : $OutDir"
Write-Host ''

# --- run ---------------------------------------------------------------------

$results = @()
if ($Mode -like 'Warm*') {
    Write-Host 'Warm-up (not recorded) ... ' -NoNewline
    $warmup = Invoke-MeasuredLaunch -ExePath $Exe -Index 0 -DataRoot $dataRoot `
        -FixturePath $FixturePath -Cold $false `
        -ReportPath (Join-Path $OutDir 'warmup.json') `
        -ConversationRequired $RequireConversation.IsPresent
    if (-not $warmup.GracefulExit) { throw 'Warm-up did not exit gracefully.' }
    Write-Host 'complete'
}
for ($i = 1; $i -le $Iterations; $i++) {
    # Without a standby purge only the first launch is meaningfully cold, so later iterations
    # are labelled warm and aggregated separately rather than averaged together.
    $cold = $Mode -like 'Cold*'
    Write-Host ("Iteration {0}/{1} ({2}) ..." -f $i, $Iterations, $(if ($cold) { 'cold' } else { 'warm' })) -NoNewline
    $result = Invoke-MeasuredLaunch -ExePath $Exe -Index $i -DataRoot $dataRoot -FixturePath $FixturePath -Cold $cold `
        -ReportPath (Join-Path $OutDir "startup-$i.json") -ConversationRequired $RequireConversation.IsPresent
    $expectedPackaged = $Mode -like '*Msix'
    if ([bool]$result.Packaged -ne $expectedPackaged) {
        throw "Mode $Mode expected packaged=$expectedPackaged, but the app reported packaged=$($result.Packaged)."
    }
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
$stats += Get-Stats 'FirstInteractiveMs' @($results | ForEach-Object { $_.FirstInteractiveMs }) 'ms'
$stats += Get-Stats 'SessionListLoadedMs' @($results | ForEach-Object { $_.SessionListLoadedMs }) 'ms'
$stats += Get-Stats 'FirstConversationRenderedMs' @($results | ForEach-Object { $_.FirstConversationRenderedMs }) 'ms'
$stats += Get-Stats 'DeferredStartupCompletedMs' @($results | ForEach-Object { $_.DeferredStartupCompletedMs }) 'ms'
$stats += Get-Stats 'IdleWorkingSetMb' @($results | ForEach-Object { $_.IdleWorkingSetMb }) 'MB'
$stats += Get-Stats 'IdlePrivateMb'    @($results | ForEach-Object { $_.IdlePrivateMb }) 'MB'
$stats += Get-Stats 'ManagedHeapMb'    @($results | ForEach-Object { $_.ManagedHeapMb }) 'MB'
$stats += Get-Stats 'PeakWorkingSetMb' @($results | ForEach-Object { $_.PeakWorkingSetMb }) 'MB'
# Only graceful exits carry a real shutdown duration — a killed run reports the wait timeout,
# which would drag the median toward the timeout rather than toward the app's behaviour. The
# count of non-graceful exits is reported separately instead of being averaged in.
$gracefulRuns = @($results | Where-Object { $_.GracefulExit })
if ($gracefulRuns.Count -lt 5) {
    throw "Only $($gracefulRuns.Count) graceful shutdown sample(s) were valid; at least five are required."
}
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

$buildCommit = (git -C $RepoRoot rev-parse HEAD).Trim()
$buildDirty = [bool](git -C $RepoRoot status --porcelain)
Add-Type -AssemblyName System.Windows.Forms
$machine = [pscustomobject]@{
    Machine       = $env:COMPUTERNAME
    OS            = (Get-CimInstance Win32_OperatingSystem).Caption
    OSVersion     = [Environment]::OSVersion.Version.ToString()
    Cores         = [Environment]::ProcessorCount
    TotalMemoryGb = [math]::Round((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory / 1GB, 1)
    Configuration = $results[0].Configuration
    Packaged      = $results[0].Packaged
    StandbyPurged = [bool]$script:StandbyPurgeAvailable
    ColdCacheQualification = $(if ($Mode -like 'Cold*') { 'Qualified-PurgedBeforeEverySample' } else { 'NotApplicable-WarmMode' })
    BenchmarkMode = $Mode
    BuildCommit = $buildCommit
    BuildDirty = $buildDirty
    ExecutableSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $Exe).Hash
    PackageFullName = $packageFullName
    Dataset = $DatasetId
    PowerScheme = ((powercfg /getactivescheme) -join ' ').Trim()
    PowerSource = [System.Windows.Forms.SystemInformation]::PowerStatus.PowerLineStatus.ToString()
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
if ($Mode -like 'Cold*' -and -not $machine.StandbyPurged) {
    $null = $md.AppendLine()
    $null = $md.AppendLine('> Standby list was not purged (needs an elevated shell), so only iteration 1 is a true cold start.')
}
if ($machine.NonGracefulExits -gt 0) {
    $null = $md.AppendLine()
    $null = $md.AppendLine("> $($machine.NonGracefulExits) of $($results.Count) iteration(s) had to be killed after the named graceful-exit request; those are excluded from the shutdown statistic.")
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
$null = $md.AppendLine('| # | Mode | managedEntry | hostStarted | windowCreated | windowActivated | firstFrame | interactive | sessions | conversation | deferred |')
$null = $md.AppendLine('|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|')
foreach ($r in $results) {
    $null = $md.AppendLine("| $($r.Iteration) | $($r.Mode) | $(Format-OptionalMilliseconds $r.ManagedEntryMs) | $(Format-OptionalMilliseconds $r.HostStartedMs) | $(Format-OptionalMilliseconds $r.WindowCreatedMs) | $(Format-OptionalMilliseconds $r.WindowActivatedMs) | $(Format-OptionalMilliseconds $r.TimeToFirstFrameMs) | $(Format-OptionalMilliseconds $r.FirstInteractiveMs) | $(Format-OptionalMilliseconds $r.SessionListLoadedMs) | $(Format-OptionalMilliseconds $r.FirstConversationRenderedMs) | $(Format-OptionalMilliseconds $r.DeferredStartupCompletedMs) |")
}
$null = $md.AppendLine()
$null = $md.AppendLine('## Per-iteration resources')
$null = $md.AppendLine()
$null = $md.AppendLine('Memory sampled after all required readiness milestones completed.')
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
