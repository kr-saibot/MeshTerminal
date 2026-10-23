# MeshCore wordmark animations for the existing 32x8 terminal logo viewport.
$ErrorActionPreference = 'Stop'
$logoFolder = Join-Path (Split-Path -Parent $PSScriptRoot) 'src\ConsoleClient\logo'
$letters = @{
 M = @('# #','###','###','# #','# #')
 E = @('###','#  ','## ','#  ','###')
 S = @('###','#  ','###','  #','###')
 H = @('# #','# #','###','# #','# #')
 C = @('###','#  ','#  ','#  ','###')
 O = @('###','# #','# #','# #','###')
 R = @('## ','# #','## ','# #','# #')
}
$wordmark = @(for ($row = 0; $row -lt 5; $row++) {
    ((@('M','E','S','H','C','O','R','E') | ForEach-Object { $letters[$_][$row] }) -join ' ').PadRight(32)
})
$styles = @('pulse','scan','build','radio','data')
for ($style = 0; $style -lt $styles.Count; $style++) {
    $output = [Collections.Generic.List[string]]::new()
    for ($frame = 0; $frame -lt 32; $frame++) {
        $rows = @(for ($row = 0; $row -lt 8; $row++) { ,(' ' * 32).ToCharArray() })
        for ($row = 0; $row -lt 5; $row++) {
            for ($column = 0; $column -lt 32; $column++) {
                if ($wordmark[$row][$column] -ne '#') { continue }
                $pixel = '#'
                switch ($styles[$style]) {
                    'pulse' { $pixel = @('.','o','O','@')[[int][Math]::Floor($frame / 8)] }
                    'scan' { $pixel = if ([Math]::Abs($column - $frame) -le 2) { '@' } else { '.' } }
                    'build' { $pixel = if ($column -le [Math]::Min(31, $frame * 2)) { '#' } else { ' ' } }
                }
                $rows[$row + 1][$column] = [char]$pixel
            }
        }
        switch ($styles[$style]) {
            'pulse' { $rows[7] = '          MESHCORE              '.PadRight(32).ToCharArray() }
            'scan' { $rows[0][$frame] = [char]'v'; $rows[7][$frame] = [char]'^' }
            'build' { for ($i = 0; $i -le [Math]::Min(31, $frame * 2); $i++) { $rows[7][$i] = [char]'=' } }
            'radio' {
                $radius = 1 + ($frame % 16)
                foreach ($r in @(0,7)) {
                    $rows[$r][15] = [char]'*'
                    if (15 - $radius -ge 0) { $rows[$r][15 - $radius] = [char]')' }
                    if (15 + $radius -lt 32) { $rows[$r][15 + $radius] = [char]'(' }
                }
            }
            'data' {
                $rows[0] = ('-' * 32).ToCharArray(); $rows[7] = ('-' * 32).ToCharArray()
                $rows[0][$frame] = [char]'>'; $rows[7][31 - $frame] = [char]'<'
            }
        }
        foreach ($row in $rows) { $output.Add((-join $row)) }
    }
    $file = Join-Path $logoFolder ('{0}-meshcore-{1}-animation.txt' -f (112 + $style), $styles[$style])
    [IO.File]::WriteAllLines($file, $output, [Text.UTF8Encoding]::new($false))
    $check = [IO.File]::ReadAllLines($file)
    if ($check.Count -ne 256 -or @($check | Where-Object { $_.Length -ne 32 }).Count -gt 0) { throw "Invalid animation dimensions: $file" }
    $frames = @(for ($i = 0; $i -lt $check.Count; $i += 8) { $check[$i..($i+7)] -join "`n" })
    if (@($frames | Select-Object -Unique).Count -lt 4) { throw "Animation has too few distinct frames: $file" }
    Write-Output ([IO.Path]::GetFileName($file) + ': 32 frames, 32x8 characters')
}
