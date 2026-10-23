# Outline-letter MeshCore variants matching 107-meshtastic-animation-06.txt.
$ErrorActionPreference = 'Stop'
$folder = Join-Path (Split-Path -Parent $PSScriptRoot) 'src\ConsoleClient\logo'
$template = [IO.File]::ReadAllLines((Join-Path $folder '107-meshtastic-animation-06.txt'))
$thin = @('╔╦╗┌─┐┌─┐┬ ┬┌─┐┌─┐┬─┐┌─┐', '║║║├┤ └─┐├─┤│  │ │├┬┘├┤ ', '╩ ╩└─┘└─┘┴ ┴└─┘└─┘┴└─└─┘')
$thick = @('╔╦╗╔═╗╔═╗║ ║╔═╗╔═╗╦═╗╔═╗', '║║║╠═ ╚═╗╠═╣║  ║ ║╠╦╝╠═ ', '╩ ╩╚═╝╚═╝╩ ╩╚═╝╚═╝╩╚═╚═╝')
foreach ($variant in @(117,118,119)) {
    $rows = [Collections.Generic.List[string]]::new()
    $frames = [int]($template.Length / 8)
    for ($frame = 0; $frame -lt $frames; $frame++) {
        $sourceFrame = if ($variant -eq 118) { $frames - 1 - $frame } else { $frame }
        $block = @($template[($sourceFrame * 8)..($sourceFrame * 8 + 7)])
        $letters = if ($variant -eq 118) { $thick } else { $thin }
        for ($line = 0; $line -lt 3; $line++) {
            $text = $letters[$line]; $left = [int][Math]::Floor((32 - $text.Length) / 2)
            $block[$line + 1] = ((' ' * $left) + $text).PadRight(32)
        }
        if ($variant -eq 119) {
            $block[4] = '   o-------o-------o-------o    '
            $block[5] = '    \     / \     / \     /     '
            $block[6] = '     o---o---o---o---o---o      '
            $row = if (($frame % 2) -eq 0) { 4 } else { 6 }
            $nodes = @(for ($column = 0; $column -lt $block[$row].Length; $column++) { if ($block[$row][$column] -eq 'o') { $column } })
            $chars = $block[$row].ToCharArray(); $chars[$nodes[([int][Math]::Floor($frame / 2)) % $nodes.Count]] = [char]'@'
            $block[$row] = -join $chars
        }
        foreach ($line in $block) { $rows.Add($line.PadRight(32)) }
    }
    $name = switch ($variant) { 117 { 'meshcore-outline-mesh' } 118 { 'meshcore-double-outline-mesh' } 119 { 'meshcore-outline-network' } }
    $file = Join-Path $folder ($variant.ToString() + '-' + $name + '-animation.txt')
    if (@($rows | Where-Object { $_.Length -ne 32 }).Count -ne 0 -or $rows.Count % 8 -ne 0) { throw "Invalid dimensions: $file" }
    [IO.File]::WriteAllLines($file, $rows, [Text.UTF8Encoding]::new($false))
    Write-Output ([IO.Path]::GetFileName($file) + ': ' + $frames + ' frames, 32x8 characters')
}
