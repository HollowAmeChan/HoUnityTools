param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../src/HoFace.Studio/Assets')
)
$ErrorActionPreference = 'Stop'
if ([Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') {
    throw 'Run this exporter with powershell.exe -NoProfile -STA -File branding/Render-Icon.ps1'
}
Add-Type -AssemblyName PresentationCore,WindowsBase
$source = Join-Path $PSScriptRoot '../src/HoFace.Studio/Assets/hoface-studio.svg'
[xml]$svg = [IO.File]::ReadAllText([IO.Path]::GetFullPath($source))
$output = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($output) | Out-Null
function Number($node, [string]$name, [double]$fallback = 0) {
    $value = $node.GetAttribute($name)
    if ($value -eq '') { return $fallback }
    return [double]::Parse($value, [Globalization.CultureInfo]::InvariantCulture)
}
function Brush([string]$color) {
    if ($color -eq '' -or $color -eq 'none') { return $null }
    if ($color -eq 'url(#tile)') {
        $gradient = [Windows.Media.LinearGradientBrush]::new()
        $gradient.StartPoint = [Windows.Point]::new(0, 0)
        $gradient.EndPoint = [Windows.Point]::new(1, 1)
        $stops = $svg.SelectNodes("//*[local-name()='linearGradient' and @id='tile']/*[local-name()='stop']")
        foreach ($stop in $stops) {
            $gradient.GradientStops.Add([Windows.Media.GradientStop]::new(
                [Windows.Media.ColorConverter]::ConvertFromString($stop.GetAttribute('stop-color')),
                (Number $stop 'offset')))
        }
        return $gradient
    }
    return [Windows.Media.SolidColorBrush]::new([Windows.Media.ColorConverter]::ConvertFromString($color))
}
$visual = [Windows.Media.DrawingVisual]::new()
$drawing = $visual.RenderOpen()
$drawing.PushTransform([Windows.Media.ScaleTransform]::new(4, 4))
$shapes = $svg.SelectNodes("//*[local-name()='rect' or local-name()='path' or local-name()='circle']")
foreach ($shape in $shapes) {
    $fill = Brush $shape.GetAttribute('fill')
    $pen = $null
    if ($shape.HasAttribute('stroke')) {
        $pen = [Windows.Media.Pen]::new((Brush $shape.GetAttribute('stroke')), (Number $shape 'stroke-width' 1))
        $pen.StartLineCap = 'Round'
        $pen.EndLineCap = 'Round'
        $pen.LineJoin = 'Round'
    }
    switch ($shape.LocalName) {
        'rect' {
            $drawing.DrawRoundedRectangle($fill, $pen,
                [Windows.Rect]::new((Number $shape 'x'), (Number $shape 'y'), (Number $shape 'width'), (Number $shape 'height')),
                (Number $shape 'rx'), (Number $shape 'rx'))
        }
        'path' { $drawing.DrawGeometry($fill, $pen, [Windows.Media.Geometry]::Parse($shape.GetAttribute('d'))) }
        'circle' {
            $drawing.DrawEllipse($fill, $pen,
                [Windows.Point]::new((Number $shape 'cx'), (Number $shape 'cy')), (Number $shape 'r'), (Number $shape 'r'))
        }
    }
}
$drawing.Pop()
$drawing.Close()
$bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new(1024, 1024, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
$bitmap.Render($visual)
$encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
$stream = [IO.File]::Create((Join-Path $output 'hoface-studio.png'))
try { $encoder.Save($stream) } finally { $stream.Dispose() }
# Pillow is used only to resample the rendered SVG and write standard ICO frames.
$python = @'
from pathlib import Path
import sys
from PIL import Image

folder = Path(sys.argv[1])
image = Image.open(folder / "hoface-studio.png").convert("RGBA")
assert image.size == (1024, 1024)
assert image.getpixel((0, 0))[3] == 0
image.save(folder / "hoface-studio.ico", sizes=[(s, s) for s in (16, 20, 24, 32, 40, 48, 64, 128, 256)])
print(f"Exported PNG and multi-size ICO to {folder}")
'@
$python | & python - $output
if ($LASTEXITCODE -ne 0) { throw 'Pillow icon export failed.' }
