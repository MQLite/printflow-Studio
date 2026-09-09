<#
.SYNOPSIS
    Builds the customer-like inputs of the standard local regression set (SCRUM-11065).

.DESCRIPTION
    The set has to be customer-like and local, and it must not be made of real customer work.
    Two of its seven categories — NORMAL_JPG_PORTRAIT and COMPLEX_BACKGROUND_FINE_HAIR — exist
    to exercise the real Meitu visual workflow, so neither may be a rectangle with a label on
    it. This script renders them: a head-and-shoulders subject with modelled skin, hair and
    studio lighting, and a second one whose hair is drawn as several thousand individual
    sub-pixel strands splaying into a textured, non-uniform background. That is genuinely the
    narrow high-frequency boundary detail the fine-hair category is about, and it is synthetic,
    which is what the manifests say it is.

    The other inputs are not rendered here because rendering them would be a lie about their
    provenance:

      TRANSPARENT_PNG          copied from the approved Meitu cutout reference output, so its
                               alpha is a real cutout's alpha, soft hair edges included.
      REFERENCE_PRODUCTION_TIFF copied from the Maintop-proven baseline TIFF.
      SINGLE_PAGE_PDF          assembled here around the rendered portrait, as one page.
      PSD_WITH_COMPOSITE_PREVIEW produced by Photoshop itself — see
                               New-PrintFlowRegressionPsd.ps1. A PSD whose composite preview
                               was written by something other than Photoshop would not be
                               evidence that Photoshop can prepare one.
      COMPLETE_CUSTOMER_DESIGN already present and explicitly approved for permanent local
                               regression use. This script never touches it.

    Deterministic. Every random-looking choice comes from a fixed-seed generator defined in
    this file, so a rerun reproduces the same bytes and the manifests' SHA-256 values stay
    meaningful. Nothing is downloaded and nothing leaves the workstation.

.PARAMETER SetRoot
    The regression set root. Defaults to the fixed workstation's D:\PrintFlowStudio\TestData\v1.

.PARAMETER Force
    Overwrite inputs that already exist. Without it, an existing input is left alone — the
    accepted set's bytes are regression authority and are not to be quietly replaced.
#>
[CmdletBinding()]
param(
    [string] $SetRoot = 'D:\PrintFlowStudio\TestData\v1',
    [switch] $Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$inputs = Join-Path $SetRoot 'inputs'
$reference = Join-Path $SetRoot 'reference'
New-Item -ItemType Directory -Path $inputs -Force | Out-Null
New-Item -ItemType Directory -Path $reference -Force | Out-Null

# ------------------------------------------------------------------------------------------
# Determinism
# ------------------------------------------------------------------------------------------
# A 32-bit linear congruential generator written out here rather than System.Random, whose
# sequence is not contractually stable across .NET versions. The set's hashes are fixed
# authority; a generator that drifted with the runtime would silently invalidate them. Kept to
# 32 bits deliberately — Windows PowerShell 5.1 has no unsigned 64-bit literal, so a 64-bit
# state would have to be emulated and would be the least trustworthy part of the file.
$script:Seed = [int64] 0x5DEECE66D

function Reset-Rng([int64] $seed) { $script:Seed = ($seed -band 4294967295) }

function Get-Rand {
    # Numerical Recipes' constants. Two draws combined, because the low bits of any LCG of this
    # size are visibly periodic and hair drawn from them would stripe.
    $script:Seed = ((($script:Seed * 1664525) + 1013904223) -band 4294967295)
    $hi = ($script:Seed -shr 16) -band 0xFFFF
    $script:Seed = ((($script:Seed * 1664525) + 1013904223) -band 4294967295)
    $lo = ($script:Seed -shr 16) -band 0xFFFF
    [double] ((($hi * 65536) + $lo) / 4294967296.0)
}

function Get-RandRange([double] $lo, [double] $hi) { $lo + ((Get-Rand) * ($hi - $lo)) }

function New-Colour([int] $a, [int] $r, [int] $g, [int] $b) {
    [System.Drawing.Color]::FromArgb(
        [Math]::Max(0, [Math]::Min(255, $a)),
        [Math]::Max(0, [Math]::Min(255, $r)),
        [Math]::Max(0, [Math]::Min(255, $g)),
        [Math]::Max(0, [Math]::Min(255, $b)))
}

# ------------------------------------------------------------------------------------------
# The subject
# ------------------------------------------------------------------------------------------
# One routine draws the person for both images, so the portrait and the fine-hair case differ
# in exactly the property under test — the hair boundary and the background behind it — rather
# than being two unrelated pictures that happen to share a folder.
function Get-HeadGeometry([int] $Width, [int] $Height) {
    [pscustomobject]@{
        CentreX     = $Width * 0.5
        HeadTop     = $Height * 0.16
        HeadWidth   = $Width * 0.40
        HeadHeight  = $Height * 0.30
        HeadCentreY = ($Height * 0.16) + ($Height * 0.30 * 0.5)
    }
}

function Add-Subject {
    param(
        [System.Drawing.Graphics] $G,
        $Head,
        [int] $Width,
        [int] $Height
    )

    $cx = $Head.CentreX
    $headTop = $Head.HeadTop
    $headW = $Head.HeadWidth
    $headH = $Head.HeadHeight
    $headCy = $Head.HeadCentreY

    # Shoulders and neck first, so the head overlaps them.
    $shoulder = New-Object System.Drawing.Drawing2D.GraphicsPath
    $shoulder.AddClosedCurve(@(
        (New-Object System.Drawing.PointF([single] ($cx - ($Width * 0.46)), [single] $Height)),
        (New-Object System.Drawing.PointF([single] ($cx - ($Width * 0.34)), [single] ($Height * 0.70))),
        (New-Object System.Drawing.PointF([single] ($cx - ($Width * 0.12)), [single] ($Height * 0.56))),
        (New-Object System.Drawing.PointF([single] $cx,                     [single] ($Height * 0.545))),
        (New-Object System.Drawing.PointF([single] ($cx + ($Width * 0.12)), [single] ($Height * 0.56))),
        (New-Object System.Drawing.PointF([single] ($cx + ($Width * 0.34)), [single] ($Height * 0.70))),
        (New-Object System.Drawing.PointF([single] ($cx + ($Width * 0.46)), [single] $Height))
    ), 0.35)
    $garment = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF([single] 0, [single] ($Height * 0.62))),
        (New-Object System.Drawing.PointF([single] 0, [single] $Height)),
        (New-Colour 255 62 74 96), (New-Colour 255 28 34 48))
    $G.FillPath($garment, $shoulder)
    $garment.Dispose(); $shoulder.Dispose()

    $neck = New-Object System.Drawing.Drawing2D.GraphicsPath
    $neck.AddClosedCurve(@(
        (New-Object System.Drawing.PointF([single] ($cx - ($Width * 0.095)), [single] ($Height * 0.60))),
        (New-Object System.Drawing.PointF([single] ($cx - ($Width * 0.085)), [single] ($headCy + ($headH * 0.30)))),
        (New-Object System.Drawing.PointF([single] ($cx + ($Width * 0.085)), [single] ($headCy + ($headH * 0.30)))),
        (New-Object System.Drawing.PointF([single] ($cx + ($Width * 0.095)), [single] ($Height * 0.60)))
    ), 0.2)
    $G.FillPath((New-Object System.Drawing.SolidBrush((New-Colour 255 196 152 124))), $neck)
    $neck.Dispose()

    # The face, modelled with a radial light from the upper left rather than filled flat: a
    # single flat skin tone would give background removal an artificially easy edge.
    $faceRect = New-Object System.Drawing.RectangleF(
        [single] ($cx - ($headW * 0.5)), [single] $headTop, [single] $headW, [single] $headH)
    $facePath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $facePath.AddEllipse($faceRect)
    $face = New-Object System.Drawing.Drawing2D.PathGradientBrush($facePath)
    $face.CenterPoint = New-Object System.Drawing.PointF(
        [single] ($cx - ($headW * 0.18)), [single] ($headTop + ($headH * 0.34)))
    $face.CenterColor = New-Colour 255 236 198 170
    $face.SurroundColors = @((New-Colour 255 168 122 96))
    $G.FillPath($face, $facePath)
    $face.Dispose(); $facePath.Dispose()

    # Features. Small, low-contrast and asymmetric — enough that the subject reads as a face at
    # a glance, which is what "portrait" has to mean for a visual review to be worth anything.
    $eyeY = $headTop + ($headH * 0.44)
    $eyeDx = $headW * 0.19
    foreach ($side in -1, 1) {
        $ex = $cx + ($side * $eyeDx)
        $G.FillEllipse((New-Object System.Drawing.SolidBrush((New-Colour 255 250 246 240))),
            [single] ($ex - ($headW * 0.085)), [single] ($eyeY - ($headH * 0.030)),
            [single] ($headW * 0.17), [single] ($headH * 0.060))
        $G.FillEllipse((New-Object System.Drawing.SolidBrush((New-Colour 255 72 54 40))),
            [single] ($ex - ($headW * 0.032)), [single] ($eyeY - ($headH * 0.028)),
            [single] ($headW * 0.064), [single] ($headH * 0.056))
        $G.FillEllipse((New-Object System.Drawing.SolidBrush((New-Colour 255 20 16 14))),
            [single] ($ex - ($headW * 0.014)), [single] ($eyeY - ($headH * 0.012)),
            [single] ($headW * 0.028), [single] ($headH * 0.024))
        $brow = New-Object System.Drawing.Drawing2D.GraphicsPath
        $brow.AddCurve(@(
            (New-Object System.Drawing.PointF([single] ($ex - ($headW * 0.10)), [single] ($eyeY - ($headH * 0.085)))),
            (New-Object System.Drawing.PointF([single] $ex,                      [single] ($eyeY - ($headH * 0.115)))),
            (New-Object System.Drawing.PointF([single] ($ex + ($headW * 0.10)), [single] ($eyeY - ($headH * 0.078))))
        ), 0.5)
        $G.DrawPath((New-Object System.Drawing.Pen((New-Colour 210 74 54 40), [single] ($headH * 0.020))), $brow)
        $brow.Dispose()
    }

    $noseP = New-Object System.Drawing.Drawing2D.GraphicsPath
    $noseP.AddCurve(@(
        (New-Object System.Drawing.PointF([single] ($cx + ($headW * 0.02)), [single] ($eyeY + ($headH * 0.02)))),
        (New-Object System.Drawing.PointF([single] ($cx + ($headW * 0.05)), [single] ($eyeY + ($headH * 0.17)))),
        (New-Object System.Drawing.PointF([single] ($cx - ($headW * 0.03)), [single] ($eyeY + ($headH * 0.21))))
    ), 0.5)
    $G.DrawPath((New-Object System.Drawing.Pen((New-Colour 120 150 106 84), [single] ($headH * 0.014))), $noseP)
    $noseP.Dispose()

    $mouthY = $headTop + ($headH * 0.755)
    $mouth = New-Object System.Drawing.Drawing2D.GraphicsPath
    $mouth.AddCurve(@(
        (New-Object System.Drawing.PointF([single] ($cx - ($headW * 0.145)), [single] $mouthY)),
        (New-Object System.Drawing.PointF([single] $cx,                       [single] ($mouthY + ($headH * 0.042)))),
        (New-Object System.Drawing.PointF([single] ($cx + ($headW * 0.145)), [single] $mouthY))
    ), 0.5)
    $G.DrawPath((New-Object System.Drawing.Pen((New-Colour 235 158 86 84), [single] ($headH * 0.030))), $mouth)
    $mouth.Dispose()

    [pscustomobject]@{
        CentreX = $cx; HeadTop = $headTop; HeadWidth = $headW; HeadHeight = $headH; HeadCentreY = $headCy
    }
}

# ------------------------------------------------------------------------------------------
# Hair
# ------------------------------------------------------------------------------------------
# Hair is two passes with the subject drawn between them, because that is the order the picture
# is actually in: the mass sits behind and around the head, the face sits on top of it, and the
# loose strands leave the scalp and cross in front of whatever is behind them. Drawing the mass
# after the face buries the face, which is not a portrait.
function Add-HairMass {
    param([System.Drawing.Graphics] $G, $Head)

    $cx = $Head.CentreX
    $headW = $Head.HeadWidth
    $headH = $Head.HeadHeight
    $top = $Head.HeadTop

    $mass = New-Object System.Drawing.Drawing2D.GraphicsPath
    $mass.AddClosedCurve(@(
        (New-Object System.Drawing.PointF([single] ($cx - ($headW * 0.72)), [single] ($top + ($headH * 1.02)))),
        (New-Object System.Drawing.PointF([single] ($cx - ($headW * 0.76)), [single] ($top + ($headH * 0.34)))),
        (New-Object System.Drawing.PointF([single] ($cx - ($headW * 0.40)), [single] ($top - ($headH * 0.16)))),
        (New-Object System.Drawing.PointF([single] $cx,                      [single] ($top - ($headH * 0.23)))),
        (New-Object System.Drawing.PointF([single] ($cx + ($headW * 0.40)), [single] ($top - ($headH * 0.16)))),
        (New-Object System.Drawing.PointF([single] ($cx + ($headW * 0.76)), [single] ($top + ($headH * 0.34)))),
        (New-Object System.Drawing.PointF([single] ($cx + ($headW * 0.72)), [single] ($top + ($headH * 1.02))))
    ), 0.35)
    $massBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF([single] ($cx - $headW), [single] ($top - $headH))),
        (New-Object System.Drawing.PointF([single] ($cx + $headW), [single] ($top + $headH))),
        (New-Colour 255 78 54 38), (New-Colour 255 34 22 16))
    $G.FillPath($massBrush, $mass)
    $massBrush.Dispose(); $mass.Dispose()
}

function Add-HairStrands {
    param(
        [System.Drawing.Graphics] $G,
        $Head,
        [int] $StrandCount,
        [double] $FlyawayReach,
        [double] $MaxStrandWidth
    )

    $cx = $Head.CentreX
    $headW = $Head.HeadWidth
    $headH = $Head.HeadHeight
    $top = $Head.HeadTop

    # The scalp the strands leave from, and the two radii of the ellipse they leave it along.
    $sx = $cx
    $sy = $top + ($headH * 0.34)
    $rx0 = $headW * 0.60
    $ry0 = $headH * 0.62

    # Each strand is a three-point curve rooted on that ellipse and travelling outward along its
    # own radial, so its far end always lies over the background. That is the whole point of the
    # category: a boundary made of many thin transitions rather than one silhouette edge. The
    # angle is confined to the upper hemisphere and the sides, so no strand wanders back across
    # the face — hair over the eyes would make the visual review about the wrong thing.
    for ($i = 0; $i -lt $StrandCount; $i++) {
        $angle = Get-RandRange (-0.30) ([Math]::PI + 0.30)
        $cos = [Math]::Cos($angle)
        $sin = [Math]::Sin($angle)

        $jitter = Get-RandRange 0.94 1.06
        $rootX = $sx - ($rx0 * $jitter * $cos)
        $rootY = $sy - ($ry0 * $jitter * $sin)

        $len = $headW * (Get-RandRange 0.06 $FlyawayReach)
        $drift = Get-RandRange (-0.30) 0.30
        $endX = $rootX - ($len * [Math]::Cos($angle + $drift))
        $endY = $rootY - ($len * [Math]::Sin($angle + $drift)) + ($headH * (Get-RandRange (-0.02) 0.16))

        $bow = Get-RandRange (-0.09) 0.09
        $midX = (($rootX + $endX) * 0.5) - ($headW * $bow * $sin)
        $midY = (($rootY + $endY) * 0.5) + ($headH * $bow * $cos)

        $shade = [int] (Get-RandRange 26 138)
        $alpha = [int] (Get-RandRange 55 215)
        $w = [single] (Get-RandRange 0.45 $MaxStrandWidth)

        $pen = New-Object System.Drawing.Pen(
            (New-Colour $alpha $shade ([int] ($shade * 0.70)) ([int] ($shade * 0.52))), $w)
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round

        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        $path.AddCurve(@(
            (New-Object System.Drawing.PointF([single] $rootX, [single] $rootY)),
            (New-Object System.Drawing.PointF([single] $midX,  [single] $midY)),
            (New-Object System.Drawing.PointF([single] $endX,  [single] $endY))
        ), 0.6)
        $G.DrawPath($pen, $path)
        $path.Dispose(); $pen.Dispose()
    }
}

function New-Graphics([System.Drawing.Bitmap] $Bitmap) {
    $g = [System.Drawing.Graphics]::FromImage($Bitmap)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g
}

function Save-Jpeg([System.Drawing.Bitmap] $Bitmap, [string] $Path, [int] $Quality) {
    $codec = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() |
        Where-Object { $_.MimeType -eq 'image/jpeg' }
    $params = New-Object System.Drawing.Imaging.EncoderParameters(1)
    $params.Param[0] = New-Object System.Drawing.Imaging.EncoderParameter(
        [System.Drawing.Imaging.Encoder]::Quality, [int64] $Quality)
    $Bitmap.Save($Path, $codec, $params)
    $params.Dispose()
}

function Test-ShouldWrite([string] $Path) {
    if ((Test-Path -LiteralPath $Path) -and -not $Force) {
        Write-Host "  kept (already present): $(Split-Path -Leaf $Path)" -ForegroundColor DarkGray
        return $false
    }
    return $true
}

$W = 1200
$H = 1600

# ------------------------------------------------------------------------------------------
# FIX-PORTRAIT-001 — NORMAL_JPG_PORTRAIT
# ------------------------------------------------------------------------------------------
$portraitPath = Join-Path $inputs 'FIX-PORTRAIT-001.jpg'
if (Test-ShouldWrite $portraitPath) {
    Reset-Rng (0x1101A)
    $bmp = New-Object System.Drawing.Bitmap($W, $H, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $bmp.SetResolution(300, 300)
    $g = New-Graphics $bmp

    # An even studio backdrop with a soft vignette: the ordinary case, where the subject is
    # clearly separated and enhancement has nothing unusual to contend with.
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF([single] 0, [single] 0)),
        (New-Object System.Drawing.PointF([single] 0, [single] $H)),
        (New-Colour 255 208 214 220), (New-Colour 255 150 158 168))
    $g.FillRectangle($bg, 0, 0, $W, $H)
    $bg.Dispose()
    $vignettePath = New-Object System.Drawing.Drawing2D.GraphicsPath
    $vignettePath.AddEllipse([single] (-$W * 0.35), [single] (-$H * 0.25), [single] ($W * 1.70), [single] ($H * 1.55))
    $vignette = New-Object System.Drawing.Drawing2D.PathGradientBrush($vignettePath)
    $vignette.CenterPoint = New-Object System.Drawing.PointF([single] ($W * 0.42), [single] ($H * 0.30))
    $vignette.CenterColor = New-Colour 0 255 255 255
    $vignette.SurroundColors = @((New-Colour 120 40 44 52))
    $g.FillPath($vignette, $vignettePath)
    $vignette.Dispose(); $vignettePath.Dispose()

    $head = Get-HeadGeometry $W $H
    Add-HairMass -G $g -Head $head
    Add-Subject -G $g -Head $head -Width $W -Height $H | Out-Null
    Add-HairStrands -G $g -Head $head -StrandCount 900 -FlyawayReach 0.22 -MaxStrandWidth 1.9

    $g.Flush(); $g.Dispose()
    Save-Jpeg $bmp $portraitPath 92
    $bmp.Dispose()
    Write-Host "  wrote $portraitPath" -ForegroundColor Green
}

# ------------------------------------------------------------------------------------------
# FIX-FINE-HAIR-001 — COMPLEX_BACKGROUND_FINE_HAIR
# ------------------------------------------------------------------------------------------
$hairPath = Join-Path $inputs 'FIX-FINE-HAIR-001.jpg'
if (Test-ShouldWrite $hairPath) {
    Reset-Rng (0x11065)
    $bmp = New-Object System.Drawing.Bitmap($W, $H, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $bmp.SetResolution(300, 300)
    $g = New-Graphics $bmp

    # A deliberately unhelpful background: a warm-to-cool gradient, overlapping soft foliage
    # shapes and out-of-focus highlights, several of them close to hair in tone. Background
    # removal that succeeded here on a flat backdrop would prove nothing about this case.
    $bg = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        (New-Object System.Drawing.PointF([single] 0, [single] 0)),
        (New-Object System.Drawing.PointF([single] $W, [single] $H)),
        (New-Colour 255 126 138 96), (New-Colour 255 44 56 62))
    $g.FillRectangle($bg, 0, 0, $W, $H)
    $bg.Dispose()

    for ($i = 0; $i -lt 130; $i++) {
        $r = Get-RandRange ($W * 0.04) ($W * 0.30)
        $x = Get-RandRange (-$r) $W
        $y = Get-RandRange (-$r) $H
        $leafPath = New-Object System.Drawing.Drawing2D.GraphicsPath
        $leafPath.AddEllipse([single] $x, [single] $y, [single] ($r * 2), [single] ($r * 1.4))
        $leaf = New-Object System.Drawing.Drawing2D.PathGradientBrush($leafPath)
        $leaf.CenterColor = New-Colour ([int] (Get-RandRange 40 120)) `
            ([int] (Get-RandRange 70 190)) ([int] (Get-RandRange 80 200)) ([int] (Get-RandRange 50 140))
        $leaf.SurroundColors = @((New-Colour 0 0 0 0))
        $g.FillPath($leaf, $leafPath)
        $leaf.Dispose(); $leafPath.Dispose()
    }

    # Fine background texture, at the same spatial frequency as the hair it sits behind. This is
    # what makes the category hard and what makes it worth testing.
    for ($i = 0; $i -lt 5200; $i++) {
        $x = Get-RandRange 0 $W
        $y = Get-RandRange 0 $H
        $len = Get-RandRange 3 22
        $a = Get-RandRange 0 ([Math]::PI * 2)
        $v = [int] (Get-RandRange 30 190)
        $pen = New-Object System.Drawing.Pen((New-Colour ([int] (Get-RandRange 18 70)) $v ($v + 8) ([int] ($v * 0.7))), [single] (Get-RandRange 0.4 1.2))
        $g.DrawLine($pen, [single] $x, [single] $y,
            [single] ($x + ($len * [Math]::Cos($a))), [single] ($y + ($len * [Math]::Sin($a))))
        $pen.Dispose()
    }

    $head = Get-HeadGeometry $W $H
    Add-HairMass -G $g -Head $head
    Add-Subject -G $g -Head $head -Width $W -Height $H | Out-Null
    Add-HairStrands -G $g -Head $head -StrandCount 7200 -FlyawayReach 0.85 -MaxStrandWidth 1.4

    $g.Flush(); $g.Dispose()
    Save-Jpeg $bmp $hairPath 94
    $bmp.Dispose()
    Write-Host "  wrote $hairPath" -ForegroundColor Green
}

# ------------------------------------------------------------------------------------------
# FIX-TRANSPARENT-001 — TRANSPARENT_PNG (copied, never regenerated)
# ------------------------------------------------------------------------------------------
$cutoutSource = Join-Path $SetRoot 'expected\FIX-CUSTOMER-DESIGN-001_CUTOUT.png'
$transparentPath = Join-Path $inputs 'FIX-TRANSPARENT-001.png'
if (Test-ShouldWrite $transparentPath) {
    if (-not (Test-Path -LiteralPath $cutoutSource)) {
        throw "The approved cutout reference '$cutoutSource' is missing; TRANSPARENT_PNG cannot be sourced honestly."
    }
    Copy-Item -LiteralPath $cutoutSource -Destination $transparentPath -Force
    Write-Host "  copied $transparentPath (from the approved cutout reference)" -ForegroundColor Green
}

# ------------------------------------------------------------------------------------------
# FIX-REFERENCE-TIFF-001 — REFERENCE_PRODUCTION_TIFF (copied, never regenerated)
# ------------------------------------------------------------------------------------------
$tiffSource = 'D:\PrintFlowStudio\Baseline\workstation-v1\actions\authoring\FIX-CUSTOMER-DESIGN-001_W1-1PX.tif'
$tiffPath = Join-Path $reference 'FIX-REFERENCE-TIFF-001.tif'
if (Test-ShouldWrite $tiffPath) {
    if (-not (Test-Path -LiteralPath $tiffSource)) {
        throw "The Maintop-proven reference TIFF '$tiffSource' is missing."
    }
    Copy-Item -LiteralPath $tiffSource -Destination $tiffPath -Force
    Write-Host "  copied $tiffPath (Maintop-proven baseline reference)" -ForegroundColor Green
}

# ------------------------------------------------------------------------------------------
# FIX-PDF-001 — SINGLE_PAGE_PDF
# ------------------------------------------------------------------------------------------
# Built byte by byte rather than printed through a driver: a print-to-PDF path embeds the
# machine's date, printer name and driver version, so the same design would produce different
# bytes every run and the fixed hash the set depends on would be a fiction. The design is the
# rendered portrait, embedded as a DCTDecode stream — one page, no second page possible.
$pdfPath = Join-Path $inputs 'FIX-PDF-001.pdf'
if (Test-ShouldWrite $pdfPath) {
    if (-not (Test-Path -LiteralPath $portraitPath)) { throw "FIX-PORTRAIT-001.jpg must exist before the PDF is built." }
    $jpeg = [System.IO.File]::ReadAllBytes($portraitPath)
    $img = [System.Drawing.Image]::FromFile($portraitPath)
    $iw = $img.Width; $ih = $img.Height
    $img.Dispose()

    # 5 x 6.667 inch page at 72 pt/inch, matching the source aspect exactly so the raster the
    # Product produces at 300 PPI is a clean 1500 x 2000 and no letterboxing is introduced.
    $pageW = 360.0
    $pageH = [Math]::Round($pageW * $ih / $iw, 4)

    $enc = [System.Text.Encoding]::GetEncoding(28591)   # latin-1: one byte per char, no reinterpretation
    $out = New-Object System.IO.MemoryStream
    $offsets = @{}
    function Add-Bytes([byte[]] $b) { $out.Write($b, 0, $b.Length) }
    function Add-Text([string] $s) { Add-Bytes ($enc.GetBytes($s)) }
    function Start-Obj([int] $n) { $offsets[$n] = [int] $out.Position; Add-Text "$n 0 obj`n" }
    function End-Obj { Add-Text "endobj`n" }

    Add-Text "%PDF-1.4`n"
    Add-Bytes ([byte[]] (0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A))

    Start-Obj 1; Add-Text "<< /Type /Catalog /Pages 2 0 R >>`n"; End-Obj
    Start-Obj 2; Add-Text "<< /Type /Pages /Kids [3 0 R] /Count 1 >>`n"; End-Obj
    Start-Obj 3
    Add-Text ("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 $pageW $pageH] " +
              "/Resources << /XObject << /Im0 4 0 R >> >> /Contents 5 0 R >>`n")
    End-Obj

    Start-Obj 4
    Add-Text ("<< /Type /XObject /Subtype /Image /Width $iw /Height $ih /ColorSpace /DeviceRGB " +
              "/BitsPerComponent 8 /Filter /DCTDecode /Length $($jpeg.Length) >>`nstream`n")
    Add-Bytes $jpeg
    Add-Text "`nendstream`n"
    End-Obj

    $content = "q`n$pageW 0 0 $pageH 0 0 cm`n/Im0 Do`nQ`n"
    Start-Obj 5
    # Parenthesised deliberately. In command-invocation syntax `Add-Text "a" + "b"` passes three
    # arguments rather than concatenating, so the unparenthesised form bound only "a" to $s and
    # discarded `endstream` into $args. The resulting PDF parsed far enough to report one page and
    # its geometry, so preflight accepted it, but Windows.Data.Pdf refused the unterminated content
    # stream and rendered the page blank.
    Add-Text ("<< /Length $($content.Length) >>`nstream`n$content" + "endstream`n")
    End-Obj

    $xref = [int] $out.Position
    Add-Text "xref`n0 6`n0000000000 65535 f `n"
    for ($n = 1; $n -le 5; $n++) { Add-Text ("{0:D10} 00000 n `n" -f $offsets[$n]) }
    Add-Text "trailer`n<< /Size 6 /Root 1 0 R >>`nstartxref`n$xref`n%%EOF`n"

    [System.IO.File]::WriteAllBytes($pdfPath, $out.ToArray())
    $out.Dispose()
    Write-Host "  wrote $pdfPath ($pageW x $pageH pt, one page)" -ForegroundColor Green
}

Write-Host ''
Write-Host 'Inputs present:' -ForegroundColor Cyan
Get-ChildItem -LiteralPath $inputs, $reference -File |
    ForEach-Object { "  {0,-34} {1,12:N0} bytes  {2}" -f $_.Name, $_.Length, (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
