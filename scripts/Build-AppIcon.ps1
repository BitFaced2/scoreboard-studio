param(
    [string]$Source = (Join-Path $PSScriptRoot '../src/ScoreboardStudio.App/Assets/ScoreboardStudio-icon.png'),
    [string]$Output = (Join-Path $PSScriptRoot '../src/ScoreboardStudio.App/Assets/ScoreboardStudio.ico')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase
$sourcePath = (Resolve-Path -LiteralPath $Source).Path
$decoder = [System.Windows.Media.Imaging.BitmapDecoder]::Create(
    [Uri]$sourcePath,
    [System.Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat,
    [System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
$bitmap = $decoder.Frames[0]
if ($bitmap.PixelWidth -ne $bitmap.PixelHeight) { throw 'The icon source must be square.' }

# Package the original artwork at Windows desktop/taskbar/DPI sizes, retaining alpha.
$sizes = @(16, 20, 24, 32, 40, 48, 64, 96, 128, 256)
$frames = foreach ($size in $sizes) {
    $visual = New-Object System.Windows.Media.DrawingVisual
    [System.Windows.Media.RenderOptions]::SetBitmapScalingMode($visual, [System.Windows.Media.BitmapScalingMode]::HighQuality)
    $context = $visual.RenderOpen()
    $context.DrawImage($bitmap, [System.Windows.Rect]::new(0, 0, $size, $size))
    $context.Close()
    $render = [System.Windows.Media.Imaging.RenderTargetBitmap]::new($size, $size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $render.Render($visual)
    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($render))
    $stream = New-Object System.IO.MemoryStream
    try { $encoder.Save($stream); ,$stream.ToArray() } finally { $stream.Dispose() }
}

$destination = [System.IO.Path]::GetFullPath($Output)
$writer = [System.IO.BinaryWriter]::new([System.IO.File]::Create($destination))
try {
    $writer.Write([UInt16]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension)
        $writer.Write([byte]$dimension)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]32)
        $writer.Write([UInt32]$frames[$i].Length)
        $writer.Write([UInt32]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
} finally { $writer.Dispose() }
Write-Output "Created $destination ($($sizes.Count) sizes)"
