param([Parameter(Mandatory=$true)][string]$DeviceId)
$ErrorActionPreference='Stop'
& (Join-Path $PSScriptRoot '..\.build-staging\Tests\MeshCore.Tests.exe') --ble-admin-read $DeviceId
if ($LASTEXITCODE -ne 0) { throw 'Bluetooth read probe failed.' }
