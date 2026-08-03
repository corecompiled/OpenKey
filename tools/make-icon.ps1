<#
.SYNOPSIS
    Regenerates assets/openkey.ico from the OpenKey mark's geometry.

.DESCRIPTION
    The .ico is a COMMITTED BINARY ARTEFACT, not a build step. Nothing in the build depends on
    this script; run it by hand when the mark or the brand colour changes, and commit the result.
    That is deliberate — it keeps the publish pipeline free of an image toolchain, and keeps the
    "one file you can copy anywhere" story intact.

    Uses only System.Drawing from the .NET Framework GAC, which is present on every Windows box.
    No install, no NuGet, no ImageMagick.

    Two geometries, and the split matters:

      >= 24px  the stroked master. Stroke weight is 3/24 of the canvas, which is an integer at
               every size divisible by 8, so no hinting is needed.

      <= 20px  a hinted variant with filled polygons. Under a straight scale the master's round
               apex collapses to a 1px radius and antialiases into a grey smudge — the mark looks
               like it has a dirty tip — and the stroke wants a fractional 2.33px. The hint holds
               a 2px stroke and a flat nose, and absorbs the difference by steepening the arms
               from 45.2 to 47.6 degrees. Nobody perceives 2.4 degrees of arm angle; everybody
               perceives a smudged tip.

    Margin is 12.5% at >= 24 and 6.25% at 16 and 20. That exception is deliberate: Explorer and
    the taskbar already inset small icons, so a 12.5% margin at 16px would leave 12px of ink and
    make the 2px stroke look thin.

    Colour is the fixed external brand hex, NOT a theme token. Themes do not exist outside the
    app, and this one value has to clear 3:1 against a white README, GitHub dark, and both
    Windows 11 taskbar themes at once. Worst case is 3.51:1.
#>

[CmdletBinding()]
param(
    [string] $OutputPath = (Join-Path $PSScriptRoot '..\assets\openkey.ico'),
    [string] $Color = '#0891B2'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# 16 and 20 use the hinted variant; the rest use the stroked master. 40 and 128 are omitted —
# Windows downscales cleanly from 48 and 256, and a 45-degree edge reduced by a small integer
# ratio is indistinguishable from a rendered one.
$sizes = @(16, 20, 24, 32, 48, 64, 256)

$rgb = [System.Drawing.ColorTranslator]::FromHtml($Color)

function New-MarkBitmap {
    param([int] $Size)

    $bmp = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.Clear([System.Drawing.Color]::Transparent)
        $brush = New-Object System.Drawing.SolidBrush($rgb)

        if ($Size -le 20) {
            # Hinted variant on a 16-unit grid, filled directly — no stroking, so the flat nose
            # and the pixel-aligned 2px stem survive exactly as specified.
            $s = $Size / 16.0

            # Each product is parenthesised: in PowerShell the comma binds tighter than "*",
            # so a bare "1 * $s, 1 * $s" parses as one array being multiplied, not two arguments.
            $stem = New-Object System.Drawing.RectangleF(($s), ($s), (2 * $s), (14 * $s))
            $g.FillRectangle($brush, $stem)

            $pts = @(
                (New-Object System.Drawing.PointF((7.35 * $s), (1.00 * $s))),
                (New-Object System.Drawing.PointF((14.50 * $s), (7.54 * $s))),
                (New-Object System.Drawing.PointF((14.50 * $s), (8.46 * $s))),
                (New-Object System.Drawing.PointF((7.35 * $s), (15.00 * $s))),
                (New-Object System.Drawing.PointF((6.00 * $s), (13.52 * $s))),
                (New-Object System.Drawing.PointF((12.04 * $s), (8.00 * $s))),
                (New-Object System.Drawing.PointF((6.00 * $s), (2.48 * $s)))
            )
            $g.FillPolygon($brush, $pts)
        }
        else {
            # Stroked master on the 24-unit grid. Round join shapes the apex; flat caps cut the
            # stem horizontally and the arm tips perpendicular to the arm, so the two elements
            # read as different stroke directions rather than one accidentally-broken shape.
            $s = $Size / 24.0
            $pen = New-Object System.Drawing.Pen($rgb, (3.0 * $s))
            $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Flat
            $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Flat
            $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
            try {
                $g.DrawLine($pen, (4.5 * $s), (3.0 * $s), (4.5 * $s), (21.0 * $s))
                $g.DrawLines($pen, @(
                    (New-Object System.Drawing.PointF((11.5 * $s), (4.06 * $s))),
                    (New-Object System.Drawing.PointF((19.5 * $s), (12.00 * $s))),
                    (New-Object System.Drawing.PointF((11.5 * $s), (19.94 * $s)))
                ))
            }
            finally { $pen.Dispose() }
        }

        $brush.Dispose()
    }
    finally { $g.Dispose() }

    return $bmp
}

function ConvertTo-IconDib {
    <#
        A 32bpp bottom-up DIB: BITMAPINFOHEADER with a doubled height, the BGRA pixels, then an
        AND mask that is all zeros because the alpha channel already carries transparency.

        Icons below 256 use this rather than PNG. PNG entries at every size are legal on Windows
        10 and later, but they are not universally decodable — System.Drawing.Icon refuses them
        outright, which is a fair warning about other consumers. BMP below 256 plus PNG at 256 is
        the layout real icon tooling emits, and it costs about 3 KB here.
    #>
    param([System.Drawing.Bitmap] $Bitmap)

    $w = $Bitmap.Width
    $h = $Bitmap.Height

    $rect = New-Object System.Drawing.Rectangle(0, 0, $w, $h)
    $data = $Bitmap.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
                             [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $pixels = New-Object byte[] ($data.Stride * $h)
        [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $pixels, 0, $pixels.Length)
    }
    finally { $Bitmap.UnlockBits($data) }

    $stream = New-Object System.IO.MemoryStream
    $w2 = New-Object System.IO.BinaryWriter($stream)
    try {
        $w2.Write([uint32]40)          # biSize
        $w2.Write([int32]$w)           # biWidth
        $w2.Write([int32]($h * 2))     # biHeight: image plus mask, per the ICO convention
        $w2.Write([uint16]1)           # biPlanes
        $w2.Write([uint16]32)          # biBitCount
        $w2.Write([uint32]0)           # biCompression: BI_RGB
        $w2.Write([uint32]0)           # biSizeImage
        $w2.Write([int32]0); $w2.Write([int32]0)
        $w2.Write([uint32]0); $w2.Write([uint32]0)

        # Bottom-up rows.
        for ($y = $h - 1; $y -ge 0; $y--) {
            $w2.Write($pixels, $y * $data.Stride, $w * 4)
        }

        # AND mask: zeroed, 4-byte aligned rows.
        $maskStride = [int][Math]::Floor((($w + 31) / 32)) * 4
        $blank = New-Object byte[] ($maskStride * $h)
        $w2.Write($blank, 0, $blank.Length)

        $w2.Flush()

        # Unary comma: returning a bare array unrolls it into the pipeline, so the caller would
        # receive Object[] of boxed bytes. BinaryWriter.Write then resolves that to the bool
        # overload and writes a single 0x01 per icon — which is exactly what it did.
        return ,$stream.ToArray()
    }
    finally { $w2.Dispose(); $stream.Dispose() }
}

$payloads = @()
foreach ($size in $sizes) {
    $bmp = New-MarkBitmap -Size $size
    try {
        if ($size -ge 256) {
            $stream = New-Object System.IO.MemoryStream
            $bmp.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            $bytes = $stream.ToArray()
            $stream.Dispose()
        }
        else {
            $bytes = [byte[]](ConvertTo-IconDib -Bitmap $bmp)
        }
        $payloads += [pscustomobject]@{ Size = $size; Bytes = $bytes }
    }
    finally { $bmp.Dispose() }
}

# ICO container, written by hand: 6-byte ICONDIR, one 16-byte ICONDIRENTRY per image, then the
# payloads concatenated.
$outDir = Split-Path -Parent $OutputPath
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }

$file = [System.IO.File]::Create($OutputPath)
$writer = New-Object System.IO.BinaryWriter($file)
try {
    $writer.Write([uint16]0)                    # reserved
    $writer.Write([uint16]1)                    # type: 1 = icon
    $writer.Write([uint16]$payloads.Count)

    $offset = 6 + (16 * $payloads.Count)
    foreach ($p in $payloads) {
        # A width or height byte of 0 means 256.
        $dim = if ($p.Size -ge 256) { 0 } else { $p.Size }
        $writer.Write([byte]$dim)               # width
        $writer.Write([byte]$dim)               # height
        $writer.Write([byte]0)                  # palette entries: 0 = truecolour
        $writer.Write([byte]0)                  # reserved
        $writer.Write([uint16]1)                # colour planes
        $writer.Write([uint16]32)               # bits per pixel
        $writer.Write([uint32]$p.Bytes.Length)
        $writer.Write([uint32]$offset)
        $offset += $p.Bytes.Length
    }

    foreach ($p in $payloads) { $writer.Write($p.Bytes) }
}
finally {
    $writer.Dispose()
    $file.Dispose()
}

$final = Get-Item $OutputPath
Write-Host ("Wrote {0} ({1:N0} bytes, {2} sizes: {3})" -f $final.FullName, $final.Length, $payloads.Count, ($sizes -join ', '))
