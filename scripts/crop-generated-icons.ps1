param(
    [Parameter(Mandatory = $true)]
    [string] $AtlasPath,
    [Parameter(Mandatory = $true)]
    [string] $OutputDirectory,
    [ValidateRange(0, 64)]
    [int] $Padding = 0
)

# Crops the clean, alpha-transparent user atlas without redrawing it. In particular, do
# not use Graphics.DrawImage/SetPixel here: those APIs premultiply semi-transparent edge
# pixels and make pixel-art outlines visibly darker after a PNG round trip.
Add-Type -AssemblyName System.Drawing

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$atlas = [System.Drawing.Bitmap]::FromFile($AtlasPath)
$names = @(
    'BarChart', 'Download', 'Package', 'Settings', 'Upload', 'ArrowLeft',
    'Minus', 'Maximize', 'Gamepad', 'Wrench', 'Lightbulb', 'AlertTriangle',
    'Info', 'RefreshCw', 'FileText', 'Globe', 'ExternalLink', 'FolderOpen',
    'Power', 'Save', 'RotateCcw', 'Trash2', 'Inbox', 'Lock', 'Unlock',
    'LogOut', 'Check', 'X', 'HardDrive'
)

try {
    $cellWidth = [int]($atlas.Width / 6)
    for ($index = 0; $index -lt $names.Count; $index++) {
        $column = $index % 6
        $row = [int][Math]::Floor($index / 6)
        $x0 = $column * $cellWidth
        $y0 = [int][Math]::Round($row * $atlas.Height / 5)
        $x1 = if ($column -eq 5) { $atlas.Width - 1 } else { (($column + 1) * $cellWidth) - 1 }
        $y1 = if ($row -eq 4) { $atlas.Height - 1 } else { [int][Math]::Round(($row + 1) * $atlas.Height / 5) - 1 }
        $width = $x1 - $x0 + 1
        $height = $y1 - $y0 + 1
        $cellRect = [System.Drawing.Rectangle]::new($x0, $y0, $width, $height)
        $cell = $atlas.Clone($cellRect, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)

        try {
            $minX = $width
            $minY = $height
            $maxX = -1
            $maxY = -1
            for ($y = 0; $y -lt $height; $y++) {
                for ($x = 0; $x -lt $width; $x++) {
                    if ($cell.GetPixel($x, $y).A -gt 0) {
                        $minX = [int][Math]::Min([int]$minX, [int]$x)
                        $minY = [int][Math]::Min([int]$minY, [int]$y)
                        $maxX = [int][Math]::Max([int]$maxX, [int]$x)
                        $maxY = [int][Math]::Max([int]$maxY, [int]$y)
                    }
                }
            }

            if ($maxX -lt 0) {
                continue
            }

            $minX = [Math]::Max(0, $minX - $Padding)
            $minY = [Math]::Max(0, $minY - $Padding)
            $maxX = [Math]::Min($width - 1, $maxX + $Padding)
            $maxY = [Math]::Min($height - 1, $maxY + $Padding)
            $cropRect = [System.Drawing.Rectangle]::new(
                $minX,
                $minY,
                $maxX - $minX + 1,
                $maxY - $minY + 1)

            # Clone performs an exact pixel copy. The natural image size is intentional:
            # Avalonia's Stretch=Uniform fills each toolbar/icon button without the old
            # 128px canvas padding that made the controls look undersized.
            $cropped = $cell.Clone($cropRect, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
            try {
                $cropped.Save(
                    (Join-Path $OutputDirectory ($names[$index] + '.png')),
                    [System.Drawing.Imaging.ImageFormat]::Png)
            }
            finally {
                $cropped.Dispose()
            }
        }
        finally {
            $cell.Dispose()
        }
    }
}
finally {
    $atlas.Dispose()
}
