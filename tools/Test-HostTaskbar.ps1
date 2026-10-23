$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $PSScriptRoot '..\.build-staging\WindowsHost\MeshTerminal.Host.exe'))
$type = $assembly.GetType('MeshTerminal.Host.TaskbarIdentity')
$flags = [Reflection.BindingFlags]'Static,NonPublic,Public'
$storeType = $type.GetNestedType('IPropertyStore', [Reflection.BindingFlags]::NonPublic)
$keyType = $type.GetNestedType('PropertyKey', [Reflection.BindingFlags]::NonPublic)
$variantType = $type.GetNestedType('PropVariant', [Reflection.BindingFlags]::NonPublic)
$idMethod = $type.GetMethod('GetApplicationId', $flags)
$firstId = $idMethod.Invoke($null, @('C:\HostA\MeshTerminal.Host.exe'))
$secondId = $idMethod.Invoke($null, @('C:\HostB\MeshTerminal.Host.exe'))
if ($firstId -eq $secondId) { throw 'Copies share a taskbar identity' }
$settings = $assembly.GetType('MeshTerminal.Host.AppSettings').GetMethod('GetSettingsFileName', $flags)
$canonical = $settings.Invoke($null, @('C:\HostA\MeshTerminal.Host.exe'))
$renamed = $settings.Invoke($null, @('C:\HostA\MobileHost.exe'))
if ($canonical -eq $renamed -or $canonical -ne 'C:\HostA\settings.ini') { throw 'Copies share their settings file' }
$legacy = $settings.Invoke($null, @('C:\HostA\MeshtasticConsoleHost.exe'))
if ($legacy -ne $canonical) { throw 'Legacy host preferences are not compatible' }
$resources = $assembly.GetManifestResourceNames()
if ('MeshTerminal.Host.assets.meshtastic-chirp.ico' -notin $resources) { throw 'Default host icon resource was lost' }
if (@($resources | Where-Object { $_ -like 'MeshTerminal.Host.assets.icons.*.ico' }).Count -lt 2) { throw 'Selectable host icons were lost' }
$form = New-Object System.Windows.Forms.Form
$icon = [Drawing.SystemIcons]::Information.Clone()
try {
    $type.GetMethod('Apply', $flags).Invoke($null, @($form.Handle, $icon, 'taskbar-test'))
    $arguments = [object[]]@($form.Handle, $storeType.GUID, $null)
    $type.GetMethod('SHGetPropertyStoreForWindow', $flags).Invoke($null, $arguments)
    $store = $arguments[2]
    try {
        foreach ($id in @(3,4,5)) {
            $key = [Activator]::CreateInstance($keyType)
            $key.Format = [Guid]'9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3'; $key.Id = $id
            $values = [object[]]@($key, [Activator]::CreateInstance($variantType))
            $storeType.GetMethod('GetValue').Invoke($store, $values)
            $value = $values[1]
            try {
                if ($value.Type -ne 31) { throw "Taskbar property $id missing" }
                $text = [Runtime.InteropServices.Marshal]::PtrToStringUni($value.Value)
                if ($id -eq 3 -and -not (Test-Path -LiteralPath ($text -replace ',0$',''))) { throw 'Taskbar icon file missing' }
                if ($id -eq 4 -and $text -ne 'MeshTerminal') { throw 'Wrong taskbar title' }
                if ($id -eq 5 -and $text -ne $type.GetMethod('GetApplicationId', $flags).Invoke($null, @([Windows.Forms.Application]::ExecutablePath))) { throw 'Wrong taskbar identity' }
            } finally { [Runtime.InteropServices.Marshal]::FreeCoTaskMem($value.Value) }
        }
    } finally { [void][Runtime.InteropServices.Marshal]::ReleaseComObject($store) }
    Write-Output 'Host taskbar smoke test passed: Windows accepted icon resource, title and application identity.'
} finally { $form.Dispose(); $icon.Dispose() }
