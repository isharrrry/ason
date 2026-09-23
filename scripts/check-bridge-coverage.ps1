#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Fails unless the bridge adapters keep their line and branch coverage above the agreed floor.

.DESCRIPTION
    Reads a cobertura report (the one `dotnet test --collect:"XPlat Code Coverage"` produces) and checks every
    package whose name starts with `Ason.Bridge` - the four projects a person edits. `coverlet.runsettings`
    already excludes the protoc-generated sources, so the numbers describe hand-written code only.

    The thresholds are *floors*, not targets: line 87% and branch 70% were chosen from what the suite actually
    achieves (as of 0.9.0: line 89-98%, branch 75-82%), so a change that drops a whole area is caught while
    ordinary refactoring noise is not.

    Sample assemblies are deliberately not checked. Each sample is a separate process, and coverage is collected
    inside the test host, so a sample's own assembly is not measurable this way; those are covered by the
    process-level end-to-end tests instead.

.EXAMPLE
    ./scripts/check-bridge-coverage.ps1 -CoverageFile artifacts/coverage/*/coverage.cobertura.xml
#>
param(
    [Parameter(Mandatory = $true)][string[]]$CoverageFile,
    [double]$LineThreshold = 87,
    [double]$BranchThreshold = 70
)

$ErrorActionPreference = 'Stop'
$failed = $false
$checked = 0

foreach ($pattern in $CoverageFile) {
    $files = Get-ChildItem -Path $pattern -ErrorAction SilentlyContinue
    if (-not $files) { $files = Get-Item -Path $pattern -ErrorAction SilentlyContinue }
    if (-not $files) { throw "no coverage report matched '$pattern'" }

    foreach ($file in $files) {
        [xml]$report = Get-Content -Raw $file.FullName
        foreach ($package in $report.coverage.packages.package) {
            if ($package.name -notlike 'Ason.Bridge*') { continue }
            $checked++
            $line = [math]::Round([double]$package.'line-rate' * 100, 1)
            $branch = [math]::Round([double]$package.'branch-rate' * 100, 1)
            $ok = ($line -ge $LineThreshold) -and ($branch -ge $BranchThreshold)
            $status = if ($ok) { 'ok  ' } else { 'FAIL' }
            Write-Host ("[{0}] {1,-24} line {2,5}% (>= {3})  branch {4,5}% (>= {5})" -f $status, $package.name, $line, $LineThreshold, $branch, $BranchThreshold)
            if (-not $ok) { $failed = $true }
        }
    }
}

if ($checked -eq 0) { throw "no Ason.Bridge* package was found in the coverage report" }
if ($failed) { throw "coverage is below the floor (line $LineThreshold%, branch $BranchThreshold%)" }
Write-Host "coverage floor met for $checked bridge package(s)"
