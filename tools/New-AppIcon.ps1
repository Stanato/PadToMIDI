$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$workspace = Split-Path $PSScriptRoot -Parent
$destination = Join-Path $workspace 'src/PadToMIDI.App/Assets/app.ico'
[void][IO.Directory]::CreateDirectory((Split-Path $destination))
$images = @()
foreach ($size in @(16,32,48,256)) {
    $bitmap = New-Object Drawing.Bitmap $size,$size
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([Drawing.Color]::FromArgb(14,23,40))
    $teal = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(101,223,192))
    $dark = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(14,23,40))
    $graphics.FillEllipse($teal, [single]($size*.10), [single]($size*.25), [single]($size*.8), [single]($size*.55))
    $graphics.FillRectangle($dark, [single]($size*.22), [single]($size*.46), [single]($size*.22), [single]($size*.08))
    $graphics.FillRectangle($dark, [single]($size*.29), [single]($size*.39), [single]($size*.08), [single]($size*.22))
    $graphics.FillEllipse($dark, [single]($size*.63), [single]($size*.40), [single]($size*.09), [single]($size*.09))
    $graphics.FillEllipse($dark, [single]($size*.74), [single]($size*.51), [single]($size*.09), [single]($size*.09))
    $stream = New-Object IO.MemoryStream
    $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
    $images += ,$stream.ToArray()
    $stream.Dispose(); $graphics.Dispose(); $bitmap.Dispose(); $teal.Dispose(); $dark.Dispose()
}
$file = [IO.File]::Create($destination)
$writer = New-Object IO.BinaryWriter $file
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$images.Count)
    $offset = 6 + 16 * $images.Count
    $sizes = @(16,32,48,256)
    for($index=0; $index -lt $images.Count; $index++) {
        $dimension = if($sizes[$index] -eq 256){0}else{$sizes[$index]}
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$images[$index].Length); $writer.Write([uint32]$offset)
        $offset += $images[$index].Length
    }
    foreach($bytes in $images) { $writer.Write([byte[]]$bytes) }
} finally { $writer.Dispose(); $file.Dispose() }
Write-Output "Created original application icon: $destination"
