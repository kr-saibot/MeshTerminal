$ErrorActionPreference = "Stop"

# This is the canonical build destination. Do not change it without updating AGENTS.md.
$BuildRoot = "C:\Users\Tobias\Documents\ConsoleClientBuilds"
$WindowsTarget = Join-Path $BuildRoot "Windows"
$LinuxTarget = Join-Path $BuildRoot "Linux"

# Build into isolated staging folders. This prevents the Windows host from
# overwriting MeshTerminal dependencies with assemblies of another version.
$RepositoryRoot = Split-Path -Parent $PSScriptRoot
$StagingRoot = Join-Path $RepositoryRoot ".build-staging"
$WindowsStaging = Join-Path $StagingRoot "Windows"
$HostStaging = Join-Path $StagingRoot "WindowsHost"
$LinuxStaging = Join-Path $StagingRoot "Linux"
$TestsStaging = Join-Path $StagingRoot "Tests"

function Reset-StagingDirectory([string] $Path) {
    $resolvedRoot = [IO.Path]::GetFullPath($StagingRoot)
    $resolvedPath = [IO.Path]::GetFullPath($Path)
    if (-not $resolvedPath.StartsWith($resolvedRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean a directory outside the staging root: $resolvedPath"
    }
    if (Test-Path -LiteralPath $resolvedPath) {
        Remove-Item -LiteralPath $resolvedPath -Recurse -Force
    }
    New-Item -ItemType Directory -Path $resolvedPath | Out-Null
}

function Copy-BuildFiles([string] $Source, [string] $Destination) {
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    $sourceRoot = [IO.Path]::GetFullPath($Source).TrimEnd('\')
    foreach ($sourceFile in Get-ChildItem -LiteralPath $sourceRoot -Recurse -File) {
        $relativePath = $sourceFile.FullName.Substring($sourceRoot.Length).TrimStart('\')
        $destinationFile = Join-Path $Destination $relativePath
        $destinationDirectory = Split-Path -Parent $destinationFile
        New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
        if (Test-Path -LiteralPath $destinationFile) {
            $existing = Get-Item -LiteralPath $destinationFile
            if ($existing.Length -eq $sourceFile.Length -and
                (Get-FileHash -LiteralPath $existing.FullName -Algorithm SHA256).Hash -eq
                (Get-FileHash -LiteralPath $sourceFile.FullName -Algorithm SHA256).Hash) {
                continue
            }
        }
        Copy-Item -LiteralPath $sourceFile.FullName -Destination $destinationFile -Force
    }
}

Push-Location $RepositoryRoot
try {
    Reset-StagingDirectory $WindowsStaging
    Reset-StagingDirectory $HostStaging
    Reset-StagingDirectory $LinuxStaging
    Reset-StagingDirectory $TestsStaging

    dotnet restore "src\MeshTerminal\MeshTerminal.csproj" -p:Configuration=Release
    if ($LASTEXITCODE -ne 0) { throw "Windows package restore failed." }
    dotnet build "src\MeshTerminal\MeshTerminal.csproj" --configuration Release --no-restore -p:OutputPath="$WindowsStaging\"
    if ($LASTEXITCODE -ne 0) { throw "Windows MeshTerminal build failed." }

    dotnet restore "src\MeshTerminal.Host\MeshTerminal.Host.csproj" -p:Configuration=Release
    if ($LASTEXITCODE -ne 0) { throw "Windows host package restore failed." }
    dotnet build "src\MeshTerminal.Host\MeshTerminal.Host.csproj" --configuration Release --no-restore -p:OutputPath="$HostStaging\"
    if ($LASTEXITCODE -ne 0) { throw "Windows host build failed." }

    dotnet restore "src\MeshTerminal\MeshTerminal.csproj" -p:Configuration=Linux
    if ($LASTEXITCODE -ne 0) { throw "Linux package restore failed." }
    dotnet build "src\MeshTerminal\MeshTerminal.csproj" --configuration Linux --no-restore -p:OutputPath="$LinuxStaging\"
    if ($LASTEXITCODE -ne 0) { throw "Linux MeshTerminal build failed." }

    dotnet restore "tests\MeshCore.Tests\MeshCore.Tests.csproj" -p:Configuration=Release
    if ($LASTEXITCODE -ne 0) { throw "MeshCore test restore failed." }
    dotnet build "tests\MeshCore.Tests\MeshCore.Tests.csproj" --configuration Release --no-restore -p:OutputPath="$TestsStaging\"
    if ($LASTEXITCODE -ne 0) { throw "MeshCore test build failed." }
    & (Join-Path $TestsStaging "MeshCore.Tests.exe")
    if ($LASTEXITCODE -ne 0) { throw "MeshCore protocol tests failed." }

    # Copying with -Force replaces old program files but deliberately does not
    # delete runtime data already present in the destinations. In particular,
    # meshtastic-settings.xml, meshtastic-messages.db and Host\settings.ini survive.
    Copy-BuildFiles $WindowsStaging $WindowsTarget
    Copy-BuildFiles $HostStaging (Join-Path $WindowsTarget "Host")
    # Resolve the host and client relative to the launcher, even after moving.
    $hostLauncher = @(
        '@echo off',
        'start "" "%~dp0Host\MeshTerminal.Host.exe" "%~dp0MeshTerminal.exe"'
    ) -join [Environment]::NewLine
    [IO.File]::WriteAllText((Join-Path $WindowsTarget "MeshTerminal.cmd"), $hostLauncher, [Text.Encoding]::ASCII)
    $oldShortcut = Join-Path $WindowsTarget "MeshTerminal.Host.lnk"
    if (Test-Path -LiteralPath $oldShortcut) { Remove-Item -LiteralPath $oldShortcut -Force }
    # Compatibility for existing shortcuts; keep all runtime files untouched.
    $legacyHostLauncher = @('@echo off', 'call "%~dp0MeshTerminal.cmd"') -join [Environment]::NewLine
    [IO.File]::WriteAllText((Join-Path $WindowsTarget "MeshtasticConsoleHost.cmd"), $legacyHostLauncher, [Text.Encoding]::ASCII)
    Copy-BuildFiles $LinuxStaging $LinuxTarget

    # Remove obsolete program names after successful replacement; never touch data.
    foreach ($target in @($WindowsTarget, $LinuxTarget)) {
        foreach ($name in @('ConsoleClient.exe', 'ConsoleClient.exe.config', 'Meshtastic.Client.dll', 'Meshtastic.Client.dll.config')) {
            $legacyProgram = Join-Path $target $name
            if (Test-Path -LiteralPath $legacyProgram) { Remove-Item -LiteralPath $legacyProgram -Force }
        }
    }
    foreach ($name in @('MeshtasticConsoleHost.exe', 'MeshtasticConsoleHost.exe.config')) {
        $legacyProgram = Join-Path (Join-Path $WindowsTarget 'Host') $name
        if (Test-Path -LiteralPath $legacyProgram) { Remove-Item -LiteralPath $legacyProgram -Force }
    }

    Write-Host ""
    Write-Host "Builds completed successfully:"
    Write-Host "  Windows: $WindowsTarget"
    Write-Host "  Linux:   $LinuxTarget"
    Write-Host "Existing settings and databases were preserved."
}
finally {
    Pop-Location
}
