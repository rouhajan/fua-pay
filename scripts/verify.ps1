[CmdletBinding()]
param(
    [switch] $RunDatabaseTests,
    [switch] $RunCsobSandboxTests,
    [string] $Configuration = "Release"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"
$env:MSBUILDDISABLENODEREUSE = "1"

$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$Solution = "FuaPay.slnx"
$WebProject = "src/FuaPay.Web/FuaPay.Web.csproj"
$WebTests = "tests/FuaPay.Web.Tests/FuaPay.Web.Tests.csproj"
$DatabaseTests = "tests/FuaPay.DatabaseTests/FuaPay.DatabaseTests.csproj"
$CsobTests = "tests/FuaPay.CsobSandboxTests/FuaPay.CsobSandboxTests.csproj"

function Invoke-Step {
    param(
        [Parameter(Mandatory)]
        [string] $Name,

        [Parameter(Mandatory)]
        [string[]] $Arguments
    )

    Write-Host ""
    Write-Host "=== $Name ===" -ForegroundColor Cyan
    & dotnet @Arguments

    if ($LASTEXITCODE -ne 0) {
        throw "Krok '$Name' selhal s exit code $LASTEXITCODE."
    }
}

Push-Location $RepositoryRoot

try {
    Invoke-Step -Name "Obnovení lokálních nástrojů" -Arguments @(
        "tool", "restore"
    )
    Invoke-Step -Name "Locked restore" -Arguments @(
        "restore", $Solution, "--locked-mode"
    )
    Invoke-Step -Name "Release build" -Arguments @(
        "build", $Solution,
        "--configuration", $Configuration,
        "--no-restore"
    )
    Invoke-Step -Name "Kontrola formátování" -Arguments @(
        "format", $Solution,
        "--verify-no-changes",
        "--no-restore"
    )
    Invoke-Step -Name "Webové a aplikační testy" -Arguments @(
        "test", $WebTests,
        "--configuration", $Configuration,
        "--no-build",
        "--no-restore"
    )
    Invoke-Step -Name "Kontrola EF modelu" -Arguments @(
        "ef", "migrations", "has-pending-model-changes",
        "--project", $WebProject,
        "--startup-project", $WebProject,
        "--context", "FuaPayDbContext",
        "--configuration", $Configuration,
        "--no-build",
        "--",
        "--ConnectionStrings:FuaPay=Host=localhost;Database=unused;Username=unused;Password=unused"
    )

    if ($RunDatabaseTests) {
        Invoke-Step -Name "PostgreSQL integrační testy" -Arguments @(
            "test", $DatabaseTests,
            "--configuration", $Configuration,
            "--no-build",
            "--no-restore"
        )
    }

    if ($RunCsobSandboxTests) {
        Invoke-Step -Name "Živý ČSOB integrační echo test" -Arguments @(
            "test", $CsobTests,
            "--configuration", $Configuration,
            "--no-build",
            "--no-restore"
        )
    }

    Write-Host ""
    Write-Host "Ověření FUA Pay prošlo." -ForegroundColor Green
}
finally {
    Pop-Location
}
