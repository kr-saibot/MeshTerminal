<#
Creates redistributable Windows/Linux ZIPs without private runtime data.
Usage: .\Pack-Releases.ps1
       .\Pack-Releases.ps1 -WithoutVersion
#>
param(
    [switch] $WithoutVersion,
    [string] $BuildRoot = $PSScriptRoot
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Test-PrivateReleaseFile([string] $RelativePath) {
    $name = [IO.Path]::GetFileName($RelativePath)
    return $name -match '(?i)(settings|messages|clipboard|screenshot)' -or
        $name -match '(?i)\.(db|sqlite|sqlite3)(-wal|-shm|-journal)?$|\.(ini|log|bak|tmp|pdb)$'
}

$root = [IO.Path]::GetFullPath($BuildRoot)
$manifestPath = Join-Path $root 'release-files.json'
if (-not (Test-Path -LiteralPath $manifestPath)) {
    throw 'release-files.json is missing. Run tools\Build-All.ps1 in the MeshTerminal project first.'
}
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
foreach ($platform in @('Windows', 'Linux')) {
    $source = [IO.Path]::GetFullPath((Join-Path $root $platform)).TrimEnd('\', '/')
    $prefix = $source + [IO.Path]::DirectorySeparatorChar
    $files = @($manifest.$platform)
    if ($files.Count -eq 0) { throw "No release files listed for $platform." }
    $exe = Join-Path $source 'MeshTerminal.exe'
    if (-not (Test-Path -LiteralPath $exe)) { throw "Program missing: $exe" }
    $suffix = ''
    if (-not $WithoutVersion) {
        $info = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe)
        $match = [regex]::Match($info.ProductVersion, '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?')
        if (-not $match.Success) { throw "Cannot determine the version of $exe. Use -WithoutVersion if necessary." }
        $suffix = '-' + $match.Value
    }
    $destination = Join-Path $root ($platform.ToLowerInvariant() + $suffix + '.zip')
    $temporary = Join-Path $root ('.meshterminal-package-' + [Guid]::NewGuid().ToString('N') + '.tmp')
    $stream = $null; $archive = $null; $count = 0
    try {
        $stream = [IO.File]::Open($temporary, [IO.FileMode]::CreateNew, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $false)
        foreach ($relative in $files) {
            if (Test-PrivateReleaseFile $relative) { continue }
            if ([IO.Path]::IsPathRooted($relative) -or $relative -match '(^|[\\/])\.\.([\\/]|$)') { throw "Invalid release path: $relative" }
            $file = [IO.Path]::GetFullPath((Join-Path $source $relative))
            if (-not $file.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw "Release path outside $platform`: $relative" }
            if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Release file missing: $relative. Rebuild before packaging." }
            $entry = $relative.Replace('\', '/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file, $entry, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
            $count++
        }
        $archive.Dispose(); $archive = $null; $stream = $null
        Move-Item -LiteralPath $temporary -Destination $destination -Force
        Write-Host ("Created: {0} ({1} files, {2:N1} MB)" -f $destination, $count, ((Get-Item -LiteralPath $destination).Length / 1MB))
    }
    finally {
        if ($archive) { $archive.Dispose() }
        if ($stream) { $stream.Dispose() }
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
    }
}
