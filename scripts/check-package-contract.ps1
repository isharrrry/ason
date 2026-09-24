#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Packs Ason.Bridge.Grpc and fails unless the produced package really carries protos/ason_bridge.proto.

.DESCRIPTION
    A non-.NET caller generates its client from the contract, and the contract travels inside the package, so the
    only honest check is to open the nupkg that was built: the .proto sitting in the repository proves nothing.

    The archive is read with the runtime's own zip reader instead of `unzip`. That removes a tool dependency, and
    it lets a failure explain itself: the first time this check ran in CI it failed in two seconds with nothing but
    "Process completed with exit code 1", and whether the pack had failed, no package had been produced, or the
    entry was named differently was not observable from outside. It now says which of those it was and lists the
    entries it did find as a workflow annotation.

    The entry *name* matters as much as its presence. `PackagePath="protos\"` used to produce
    "protos//ason_bridge.proto" - a double slash, which is not what a consumer looks for and not what the
    documentation promises.

.PARAMETER Project
    Project to pack. Defaults to the gRPC adapter, whose package carries the contract.

.PARAMETER OutputDirectory
    Where the nupkg is written and read. Defaults to artifacts/pack, the path the workflow and ci-linux.sh use.

.PARAMETER Configuration
    Build configuration passed to `dotnet pack`. Defaults to Release.

.PARAMETER ExpectedEntry
    The entry the package must contain. Defaults to protos/ason_bridge.proto.

.PARAMETER NoPack
    Skip `dotnet pack` and inspect whatever is already in OutputDirectory (useful when a previous step packed).

.EXAMPLE
    ./scripts/check-package-contract.ps1

.EXAMPLE
    ./scripts/check-package-contract.ps1 -OutputDirectory artifacts/pack -NoPack
#>
param(
    [string]$Project = 'src/Ason.Bridge.Grpc/Ason.Bridge.Grpc.csproj',
    [string]$OutputDirectory = 'artifacts/pack',
    [string]$Configuration = 'Release',
    [string]$ExpectedEntry = 'protos/ason_bridge.proto',
    [switch]$NoPack
)

$ErrorActionPreference = 'Stop'

# Windows PowerShell 5.1 does not load System.IO.Compression.FileSystem on demand, while PowerShell 7 does.
# Loading it only when the type is missing keeps this script runnable from either shell - a maintainer on
# Windows is as likely to type `powershell` as `pwsh`.
if (-not ('System.IO.Compression.ZipFile' -as [type])) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
}

# Resolved from the script's own location so the check works from any working directory.
Push-Location (Split-Path -Parent $PSScriptRoot)
try {
    if (-not $NoPack) {
        dotnet pack $Project --configuration $Configuration -o $OutputDirectory
        if ($LASTEXITCODE -ne 0) {
            Write-Host "::error title=the package could not be built::dotnet pack exited with $LASTEXITCODE"
            throw "dotnet pack exited with $LASTEXITCODE"
        }
    }

    $package = Get-ChildItem -Path $OutputDirectory -Filter '*.nupkg' -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($null -eq $package) {
        Write-Host "::error title=the package could not be built::no nupkg under $OutputDirectory"
        throw "no nupkg under $OutputDirectory"
    }

    $archive = [System.IO.Compression.ZipFile]::OpenRead($package.FullName)
    try { $entries = @($archive.Entries | ForEach-Object { $_.FullName }) }
    finally { $archive.Dispose() }
    $entries | Sort-Object | ForEach-Object { Write-Host "  $_" }

    if ($entries -notcontains $ExpectedEntry) {
        Write-Host ("::error title=the contract is missing from the package::{0} has no {1}. It contains: {2}" -f $package.Name, $ExpectedEntry, ($entries -join ', '))
        throw "$($package.Name) has no $ExpectedEntry"
    }

    Write-Host ("{0} ships {1}" -f $package.Name, $ExpectedEntry)
}
finally {
    Pop-Location
}
