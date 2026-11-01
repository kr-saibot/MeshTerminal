param(
    [Parameter(Mandatory = $true)][string] $BuildRoot,
    [Parameter(Mandatory = $true)][string] $StagingRoot
)
$ErrorActionPreference = 'Stop'
function Get-ReleaseFiles([string] $Directory, [string] $Prefix = '') {
    $base = [IO.Path]::GetFullPath($Directory).TrimEnd('\', '/')
    if (-not (Test-Path -LiteralPath $base -PathType Container)) { throw "Build staging directory missing: $base" }
    foreach ($file in Get-ChildItem -LiteralPath $base -Recurse -File) {
        $relative = $file.FullName.Substring($base.Length).TrimStart('\', '/')
        if ($file.Name -match '(?i)(settings|messages|clipboard|screenshot|DM2TK)' -or
            $file.Name -match '(?i)\.(db|sqlite|sqlite3)(-wal|-shm|-journal)?$|\.(ini|log|bak|tmp|pdb)$') { continue }
        $Prefix + $relative
    }
}
$manifest = [ordered]@{
    Windows = @((Get-ReleaseFiles (Join-Path $StagingRoot 'Windows')) + (Get-ReleaseFiles (Join-Path $StagingRoot 'WindowsHost') 'Host\') + 'MeshTerminal.cmd' | Sort-Object -Unique)
    Linux = @(Get-ReleaseFiles (Join-Path $StagingRoot 'Linux') | Sort-Object -Unique)
}
New-Item -ItemType Directory -Path $BuildRoot -Force | Out-Null
$manifest | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $BuildRoot 'release-files.json') -Encoding UTF8
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Pack-Releases.ps1') -Destination (Join-Path $BuildRoot 'Pack-Releases.ps1') -Force

Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Pack-Releases.cmd') -Destination (Join-Path $BuildRoot 'Pack-Releases.cmd') -Force
