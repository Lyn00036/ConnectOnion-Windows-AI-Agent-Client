[CmdletBinding()]
param(
    [string]$ResultsDirectory = (Join-Path $PSScriptRoot '..\TestResults'),
    [ValidateRange(0, 100)]
    [double]$ProtocolLineThreshold = 86.0,
    [ValidateRange(0, 100)]
    [double]$CoreLineThreshold = 88.5
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-CoberturaReports {
    param(
        [Parameter(Mandatory)]
        [string]$Root,
        [Parameter(Mandatory)]
        [string[]]$Suites
    )

    $reports = foreach ($suite in $Suites) {
        $suiteRoot = Join-Path $Root $suite
        if (-not (Test-Path -LiteralPath $suiteRoot -PathType Container)) {
            throw "Coverage results are missing for the '$suite' test suite: $suiteRoot"
        }

        Get-ChildItem -LiteralPath $suiteRoot -Recurse -Filter 'coverage.cobertura.xml' -File
    }

    if (@($reports).Count -eq 0) {
        throw "No coverage.cobertura.xml files were found under $Root"
    }

    return @($reports)
}

function Get-MergedLineCoverage {
    param(
        [Parameter(Mandatory)]
        [string]$Assembly,
        [Parameter(Mandatory)]
        [System.IO.FileInfo[]]$Reports,
        [Parameter(Mandatory)]
        [double]$Threshold
    )

    # The same Core source is instrumented by both the unit and integration suites. Merge by
    # source-file + line and keep the highest hit count so executable lines are counted once.
    $lines = @{}
    foreach ($report in $Reports) {
        [xml]$coverage = Get-Content -LiteralPath $report.FullName -Raw
        foreach ($package in @($coverage.coverage.packages.package)) {
            if ([string]$package.name -ne $Assembly) { continue }

            foreach ($class in @($package.classes.class)) {
                $file = ([string]$class.filename).Replace('/', '\')
                if ($file -match '(^|\\)obj\\') { continue }

                foreach ($line in @($class.lines.line)) {
                    $key = "$file|$($line.number)"
                    $covered = [int]$line.hits -gt 0
                    if (-not $lines.ContainsKey($key) -or $covered) {
                        $lines[$key] = $covered
                    }
                }
            }
        }
    }

    if ($lines.Count -eq 0) {
        throw "Coverage reports did not contain executable lines for $Assembly."
    }

    $coveredLines = @($lines.Values | Where-Object { $_ }).Count
    $rate = 100.0 * $coveredLines / $lines.Count
    [pscustomobject]@{
        Assembly = $Assembly
        Covered = $coveredLines
        Total = $lines.Count
        Rate = $rate
        Threshold = $Threshold
        Passed = $rate -ge $Threshold
    }
}

$resolvedResults = (Resolve-Path -LiteralPath $ResultsDirectory).Path
$protocolReports = Get-CoberturaReports -Root $resolvedResults -Suites @('protocol')
$coreReports = Get-CoberturaReports -Root $resolvedResults -Suites @('client', 'integration')

$results = @(
    Get-MergedLineCoverage -Assembly 'ConnectOnion.Protocol' `
        -Reports $protocolReports -Threshold $ProtocolLineThreshold
    Get-MergedLineCoverage -Assembly 'ConnectOnion.WinUIClient.Core' `
        -Reports $coreReports -Threshold $CoreLineThreshold
)

Write-Host 'Merged production line coverage (duplicate source lines counted once):'
$results |
    Select-Object Assembly, Covered, Total,
        @{ Name = 'Rate'; Expression = { '{0:N2}%' -f $_.Rate } },
        @{ Name = 'Required'; Expression = { '{0:N2}%' -f $_.Threshold } },
        @{ Name = 'Result'; Expression = { if ($_.Passed) { 'PASS' } else { 'FAIL' } } } |
    Format-Table -AutoSize

if ($env:GITHUB_STEP_SUMMARY) {
    $summary = @(
        '## Coverage gate'
        ''
        '| Assembly | Covered lines | Total lines | Coverage | Required | Result |'
        '| --- | ---: | ---: | ---: | ---: | --- |'
    )
    foreach ($result in $results) {
        $verdict = if ($result.Passed) { 'PASS' } else { 'FAIL' }
        $summary += "| $($result.Assembly) | $($result.Covered) | $($result.Total) | $($result.Rate.ToString('N2'))% | $($result.Threshold.ToString('N2'))% | $verdict |"
    }
    $summary += ''
    Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value $summary
}

$failures = @($results | Where-Object { -not $_.Passed })
if ($failures.Count -gt 0) {
    foreach ($failure in $failures) {
        Write-Error -ErrorAction Continue (
            '{0} line coverage is {1:N2}%, below the required {2:N2}%.' -f `
                $failure.Assembly, $failure.Rate, $failure.Threshold)
    }
    exit 1
}

Write-Host 'Coverage gate passed.'
