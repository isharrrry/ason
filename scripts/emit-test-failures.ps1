#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Turns failed test results into workflow annotations, so a failure is readable without the job log.

.DESCRIPTION
    A public repository's check-run annotations are readable without authentication, while its job logs are
    not: `GET /repos/{owner}/{repo}/actions/jobs/{id}/logs` answers 403 ("Must have admin rights") to an
    anonymous caller. A failed test step only produces the generic annotation "Process completed with exit
    code 1", which says nothing about which test broke.

    That matters when the failure is platform-specific: the Ubuntu job and the Windows job run the same
    suites, so "red on Linux, green on Windows" is exactly the case where the log is needed and hardest to
    get (a contributor on a Windows machine, or an agent, may have no authenticated access at all).

    The test steps therefore write TRX files. This script converts every failed test in them into a `::error`
    workflow command, which GitHub attaches to the check run - so the failing test's name and message can be
    read with one anonymous HTTPS request to `/check-runs/{id}/annotations`.

    It never fails the step on its own: the test step already decided the outcome, and a missing TRX (a build
    failure produces none) is reported as an annotation instead of a second failure.

.PARAMETER ResultsDirectory
    One or more places the test steps wrote TRX files to. Defaults to `TestResults` and `artifacts`, which
    covers both plain `dotnet test` runs and the coverage run (`--results-directory artifacts/coverage`).

.EXAMPLE
    pwsh -File scripts/emit-test-failures.ps1
#>
[CmdletBinding()]
param(
    [string[]] $ResultsDirectory = @('TestResults', 'artifacts')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Annotation {
    param([string] $Title, [string] $Message)

    # Workflow commands are single-line: a literal '%' starts an escape, and newlines must be folded.
    $escape = { param($text) $text -replace '%', '%25' -replace "`r`n", '%0A' -replace "`n", '%0A' -replace "`r", '%0A' }
    $oneLine = { param($text) $text -replace '%', '%25' -replace "`r`n", ' ' -replace "`n", ' ' -replace "`r", ' ' }

    Write-Host ("::error title={0}::{1}" -f (& $oneLine $Title), (& $escape $Message))
}

# One file may be found through more than one root; the TRX name is what identifies a run.
$files = @{}
foreach ($root in $ResultsDirectory) {
    if (-not (Test-Path -LiteralPath $root)) { continue }
    foreach ($file in Get-ChildItem -LiteralPath $root -Filter *.trx -Recurse -File -ErrorAction SilentlyContinue) {
        $files[$file.FullName] = $file
    }
}

if ($files.Count -eq 0) {
    Write-Annotation -Title 'test results missing' -Message (
        "No TRX file under: {0} - the step that failed produced no test results, so no test ever ran (a build error, or a command the step's shell rejected). The failing step's log has the reason." -f ($ResultsDirectory -join ', '))
    exit 0
}

$failed = 0
foreach ($file in $files.Values | Sort-Object FullName) {
    $document = New-Object System.Xml.XmlDocument
    $document.Load($file.FullName)

    $results = @($document.GetElementsByTagName('UnitTestResult'))
    $bad = @($results | Where-Object { $_.GetAttribute('outcome') -eq 'Failed' })
    Write-Host ("{0}: {1} result(s), {2} failed" -f $file.Name, $results.Count, $bad.Count)

    foreach ($result in $bad) {
        $failed++
        $name = $result.GetAttribute('testName')

        # XPath with local-name() so the TRX default namespace does not have to be spelled out here.
        $errorInfo = $result.SelectSingleNode("*[local-name()='Output']/*[local-name()='ErrorInfo']")
        $message = ''
        if ($null -ne $errorInfo) {
            $parts = @()
            foreach ($childName in 'Message', 'StackTrace') {
                $child = $errorInfo.SelectSingleNode("*[local-name()='$childName']")
                if ($null -ne $child -and -not [string]::IsNullOrWhiteSpace($child.InnerText)) { $parts += $child.InnerText }
            }
            $message = $parts -join "`n"
        }
        if ([string]::IsNullOrWhiteSpace($message)) { $message = "Test failed (outcome=Failed)." }

        # Keep it readable: the first few non-empty lines, bounded in length.
        $lines = @($message -split "`n" | Where-Object { $_.Trim().Length -gt 0 } | Select-Object -First 4)
        $summary = ($lines -join "`n")
        if ($summary.Length -gt 1200) { $summary = $summary.Substring(0, 1200) + ' ...' }
        Write-Annotation -Title $name -Message $summary
    }
}

if ($failed -eq 0) { Write-Host 'No failed tests in the TRX files.' }
exit 0
