<#
.SYNOPSIS
    Writes the manifests of the standard local regression set (SCRUM-11065).

.DESCRIPTION
    One manifest per asset, under <SetRoot>\manifests\<asset-id>.json. The manifests are the
    part of the set that SCRUM-11065 actually asks for in writing — "record the expected
    processing path and relevant expected properties for every file" — and they are what
    Set-PrintFlowProductionRevalidation.ps1 reads to decide whether all seven categories exist.

    Two kinds of fact live in a manifest and they are gathered differently:

      Measured    length, SHA-256, pixel dimensions, DPI, page count, TIFF structure. Read off
                  the file here, never typed in. A manifest that asserted a hash somebody
                  believed would be worth nothing.
      Authored    category, provenance, the expected processing path, the expected properties,
                  the comparison policy and the manual checks. These are judgements about what
                  the Product should do, they are reviewed in Git as part of this script, and
                  they are the reason the script exists rather than a bare hashing loop.

    The set lives outside Git — it is customer-like local test data and the repository's privacy
    posture keeps it out (see the existing FIX-CUSTOMER-DESIGN-001 manifest's privacy block). So
    this script is how the manifests are reviewable and reproducible at all.

    Schema version 2. Version 1 was the single hand-written FIX-CUSTOMER-DESIGN-001 manifest;
    everything it carried is carried forward under the same names, and the new fields are the
    ones the fixed-set contract needs: setId, provenance, expectedProcessingPath,
    expectedExternalApplications, expectedProperties, comparisonPolicy and manualChecks.

.PARAMETER SetRoot
    The regression set root. Defaults to D:\PrintFlowStudio\TestData\v1.

.PARAMETER SetVersion
    The explicit set contract to write. v1 preserves the historical larger-only portrait
    expectation; v2 writes the approved two-axis non-shrinking expectation. v3 keeps v2's portrait
    and replaces only the fine-hair trimBoundsStrictlyInsideCanvas property with the approved exact
    trimMatchesAlphaBoundsAndMargins contract. The manifest schema stays at version 2 for all sets.
#>
[CmdletBinding()]
param(
    [string] $SetRoot = 'D:\PrintFlowStudio\TestData\v1',
    [ValidateSet('v1', 'v2', 'v3')]
    [string] $SetVersion = 'v1'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$SetId = "printflow-regression-$SetVersion"
$SchemaVersion = 2

$inputs = Join-Path $SetRoot 'inputs'
$reference = Join-Path $SetRoot 'reference'
$manifests = Join-Path $SetRoot 'manifests'
$expected = Join-Path $SetRoot 'expected'
New-Item -ItemType Directory -Path $manifests -Force | Out-Null

# ------------------------------------------------------------------------------------------
# Measurement
# ------------------------------------------------------------------------------------------
function Get-FileFacts([string] $Path) {
    $item = Get-Item -LiteralPath $Path
    [ordered] @{
        path      = $item.FullName
        extension = $item.Extension.ToLowerInvariant()
        length    = $item.Length
        sha256    = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    }
}

function Add-RasterFacts([System.Collections.Specialized.OrderedDictionary] $Facts, [string] $Path) {
    $image = [System.Drawing.Image]::FromFile($Path)
    try {
        $Facts.widthPixels = $image.Width
        $Facts.heightPixels = $image.Height
        $Facts.dpiX = [Math]::Round($image.HorizontalResolution, 2)
        $Facts.dpiY = [Math]::Round($image.VerticalResolution, 2)
        $Facts.pixelFormat = $image.PixelFormat.ToString()
        $Facts.hasAlphaChannel = ($image.PixelFormat.ToString() -match 'Argb|PArgb')
    } finally { $image.Dispose() }
}

# The PSD envelope, read the way PrintFlow's own PsdCompositeProbe reads it. Lengths are taken
# into variables before the stream moves: `Position += (read)` rewinds over the length field.
function Get-PsdFacts([string] $Path) {
    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $reader = New-Object System.IO.BinaryReader($stream)
        $u32 = { $b = $reader.ReadBytes(4); [Array]::Reverse($b); [System.BitConverter]::ToUInt32($b, 0) }
        $u16 = { $b = $reader.ReadBytes(2); [Array]::Reverse($b); [System.BitConverter]::ToUInt16($b, 0) }

        if ([System.Text.Encoding]::ASCII.GetString($reader.ReadBytes(4)) -ne '8BPS') { throw "Not a PSD: $Path" }
        if ((& $u16) -ne 1) { throw "Not PSD version 1: $Path" }
        $null = $reader.ReadBytes(6)
        $channels = & $u16
        $height = & $u32
        $width = & $u32
        $depth = & $u16
        $mode = & $u16

        $colourModeLength = & $u32
        $stream.Position = $stream.Position + $colourModeLength
        $resourceLength = & $u32
        $resourceEnd = $stream.Position + $resourceLength

        $merged = $null
        $resolution = $null
        while ($stream.Position -lt $resourceEnd) {
            if ([System.Text.Encoding]::ASCII.GetString($reader.ReadBytes(4)) -ne '8BIM') { throw "Bad PSD resource signature in $Path" }
            $id = & $u16
            $nameLength = $reader.ReadByte()
            $stream.Position = $stream.Position + $nameLength + ((($nameLength + 1) % 2))
            $size = & $u32
            $end = $stream.Position + $size + ($size % 2)
            if ($id -eq 1057) { $null = & $u32; $merged = ($reader.ReadByte() -eq 1) }
            if ($id -eq 1005) { $whole = & $u16; $null = & $u16; $resolution = [double] $whole }
            $stream.Position = $end
        }

        [ordered] @{
            widthPixels       = [int] $width
            heightPixels      = [int] $height
            channelCount      = [int] $channels
            bitsPerChannel    = [int] $depth
            colourModeNumber  = [int] $mode
            colourMode        = switch ([int] $mode) { 3 { 'RGB' } 4 { 'CMYK' } default { "mode$mode" } }
            hasRealMergedData = $merged
            horizontalDpi     = $resolution
        }
    } finally { $stream.Dispose() }
}

# The TIFF IFD, read directly. The preset already states this file's structure; reading it here
# means the manifest records what the bytes say rather than what another document says about
# them, which is the only version worth checking a set against.
function Get-TiffFacts([string] $Path) {
    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $reader = New-Object System.IO.BinaryReader($stream)
        $order = [System.Text.Encoding]::ASCII.GetString($reader.ReadBytes(2))
        if ($order -ne 'II' -and $order -ne 'MM') { throw "Not a TIFF: $Path" }
        $little = ($order -eq 'II')
        function Convert-U16([byte[]] $b) { if (-not $little) { [Array]::Reverse($b) }; [System.BitConverter]::ToUInt16($b, 0) }
        function Convert-U32([byte[]] $b) { if (-not $little) { [Array]::Reverse($b) }; [System.BitConverter]::ToUInt32($b, 0) }

        if ((Convert-U16 $reader.ReadBytes(2)) -ne 42) { throw "Not a classic TIFF: $Path" }
        $stream.Position = Convert-U32 $reader.ReadBytes(4)
        $entryCount = Convert-U16 $reader.ReadBytes(2)

        $tags = @{}
        for ($i = 0; $i -lt $entryCount; $i++) {
            $tag = Convert-U16 $reader.ReadBytes(2)
            $type = Convert-U16 $reader.ReadBytes(2)
            $count = Convert-U32 $reader.ReadBytes(4)
            $valueBytes = $reader.ReadBytes(4)
            $width = switch ([int] $type) { 1 { 1 } 2 { 1 } 3 { 2 } 4 { 4 } 5 { 8 } default { 4 } }
            $total = $count * $width
            $values = @()

            # Photoshop's own image-resource tag (34377) is megabytes of packed data and the ink-
            # name tag is a long string. Reading either value by value in PowerShell takes
            # minutes and none of the structural facts below need them — presence is the whole
            # question. Anything larger than a short vector is recorded as present and skipped.
            if ($count -gt 32) {
                $tags[[int] $tag] = @()
                continue
            }

            if ($total -le 4) {
                for ($k = 0; $k -lt $count; $k++) {
                    $slice = $valueBytes[($k * $width)..(($k * $width) + $width - 1)]
                    $values += if ($width -eq 2) { Convert-U16 $slice } elseif ($width -eq 4) { Convert-U32 $slice } else { [int] $slice[0] }
                }
            } else {
                $offset = Convert-U32 $valueBytes
                $resume = $stream.Position
                $stream.Position = $offset
                for ($k = 0; $k -lt $count; $k++) {
                    if ($width -eq 8) {
                        $numerator = Convert-U32 $reader.ReadBytes(4)
                        $denominator = Convert-U32 $reader.ReadBytes(4)
                        $values += if ($denominator -eq 0) { 0 } else { [double] $numerator / $denominator }
                    } elseif ($width -eq 2) { $values += Convert-U16 $reader.ReadBytes(2) }
                    elseif ($width -eq 4) { $values += Convert-U32 $reader.ReadBytes(4) }
                    else { $values += [int] $reader.ReadByte() }
                }
                $stream.Position = $resume
            }
            $tags[[int] $tag] = $values
        }

        function Tag([int] $id) { if ($tags.ContainsKey($id)) { $tags[$id] } else { $null } }

        [ordered] @{
            byteOrder                = if ($little) { 'IBM PC / little-endian' } else { 'Motorola / big-endian' }
            widthPixels              = [int] (Tag 256)[0]
            heightPixels             = [int] (Tag 257)[0]
            bitsPerSample            = @((Tag 258) | ForEach-Object { [int] $_ })
            compression              = [int] (Tag 259)[0]
            photometricInterpretation = [int] (Tag 262)[0]
            samplesPerPixel          = [int] (Tag 277)[0]
            planarConfiguration      = [int] (Tag 284)[0]
            xResolution              = [double] (Tag 282)[0]
            yResolution              = [double] (Tag 283)[0]
            resolutionUnit           = [int] (Tag 296)[0]
            # `if ((Tag 338))` would be wrong: the tag's value is @(0), which PowerShell unrolls
            # to 0 and treats as false, so a present extraSamples of 0 would be recorded as absent.
            extraSamples             = if ($null -ne (Tag 338)) { @((Tag 338) | ForEach-Object { [int] $_ }) } else { $null }
            hasPhotoshopResources    = $tags.ContainsKey(34377)
            hasInkNames              = $tags.ContainsKey(333)
        }
    } finally { $stream.Dispose() }
}

# The single-page PDF, through the same Windows authority the Product rasterises with. A page
# count guessed from the file's text would be a guess; Windows.Data.Pdf is what actually decides.
function Get-PdfFacts([string] $Path) {
    [void][Windows.Data.Pdf.PdfDocument, Windows.Data.Pdf, ContentType = WindowsRuntime]
    [void][Windows.Storage.StorageFile, Windows.Storage, ContentType = WindowsRuntime]
    Add-Type -AssemblyName System.Runtime.WindowsRuntime
    $asTask = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
        $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and
        $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' })[0]
    function Wait-Async($operation, $type) {
        $task = $asTask.MakeGenericMethod($type).Invoke($null, @($operation))
        if (-not $task.Wait(120000)) { throw "Timed out reading '$Path'." }
        $task.Result
    }

    $file = Wait-Async ([Windows.Storage.StorageFile]::GetFileFromPathAsync($Path)) ([Windows.Storage.StorageFile])
    $document = Wait-Async ([Windows.Data.Pdf.PdfDocument]::LoadFromFileAsync($file)) ([Windows.Data.Pdf.PdfDocument])
    $page = $document.GetPage(0)
    [ordered] @{
        pageCount            = [int] $document.PageCount
        isPasswordProtected  = [bool] $document.IsPasswordProtected
        pageWidthDip         = [double] $page.Size.Width
        pageHeightDip        = [double] $page.Size.Height
        pageWidthMillimetres = [Math]::Round($page.Size.Width * 25.4 / 96, 3)
        pageHeightMillimetres = [Math]::Round($page.Size.Height * 25.4 / 96, 3)
        rotation             = $page.Rotation.ToString()
        productionRasterWidth  = [int] [Math]::Round($page.Size.Width * 300 / 96, [MidpointRounding]::AwayFromZero)
        productionRasterHeight = [int] [Math]::Round($page.Size.Height * 300 / 96, [MidpointRounding]::AwayFromZero)
        authority            = 'Windows.Data.Pdf'
    }
}

# ------------------------------------------------------------------------------------------
# Authored expectations
# ------------------------------------------------------------------------------------------
# Everything below is a judgement about what the Product should do with each file, taken from
# WorkflowCatalog, SessionService routing, the PSD/PDF preparation processors and the accepted
# preset. No route here is invented: each names steps that exist in the fixed workflows.
$privacySynthetic = [ordered] @{
    containsIdentifiablePerson = $false
    containsMemorialDesign     = $false
    localOnly                  = $true
    gitAllowed                 = $false
    uploadAllowed              = $false
    note                       = 'Synthetic rendering of an imaginary subject. Kept out of Git with the rest of the set so the set has one privacy rule rather than two.'
}

$privacyCustomerLike = [ordered] @{
    containsIdentifiablePerson = $true
    containsMemorialDesign     = $true
    localOnly                  = $true
    gitAllowed                 = $false
    uploadAllowed              = $false
}

$assets = @(
    [ordered] @{
        id = 'FIX-PORTRAIT-001'
        category = 'NORMAL_JPG_PORTRAIT'
        file = Join-Path $inputs 'FIX-PORTRAIT-001.jpg'
        kind = 'Raster'
        privacy = $privacySynthetic
        provenance = [ordered] @{
            classification = 'Synthetic'
            producedBy     = 'tools/regression/New-PrintFlowRegressionAssets.ps1'
            derivedFrom    = $null
            rationale      = 'No project-owned photographic portrait exists on this workstation, and the only customer-like photograph present is the memorial design that already occupies COMPLETE_CUSTOMER_DESIGN. Rendered rather than copied so the category is filled without taking a second real customer file.'
            honestLimits   = 'An illustrated head-and-shoulders subject, not a photograph. It exercises the ordinary JPG enhancement route and an operator visual review; it is not evidence about photographic grain, skin texture or lens characteristics.'
        }
        expectedWorkflow = 'PrepareAsset'
        expectedProcessingPath = @(
            'Import (internal, produces the root Revision)'
            'OriginalConfirmation (operator confirms; no adapter)'
            'Enhancement — OperationKind.Enhance, AdapterKind.Meitu, RequiresReview'
            'BackgroundRemoval — skipped: an ordinary portrait on a plain backdrop needs no cutout for this category'
            'Trim — OperationKind.Trim, AdapterKind.Internal. The Meitu enhanced export carries no real transparency, so automatic alpha trimming has nothing to measure and the operator records KeepOriginalExtent'
            'ApprovedPngExport — OperationKind.PromoteApproved, promotes the approved bytes unchanged'
        )
        expectedExternalApplications = @('Meitu XiuXiu 7.8.7.5')
        expectedProperties = [ordered] @{
            importAccepted            = $true
            enhancementProducesRevision = $true
            enhancedOutputFormat      = 'PNG'
            sourceBytesUnchanged      = $true
            automationLockFreeAfterStep = $true
            terminalStepState         = 'ReviewRequired at Enhancement, then Completed after promotion'
        }
        comparisonPolicy = [ordered] @{
            mode   = 'Structural'
            reason = if ($SetVersion -in @('v2', 'v3')) {
                'Meitu AI enhancement is not a deterministic function of its input — the accepted preset records the feature, not a byte contract — so an exact-hash expectation would fail for a reason that is not a regression. The approved structural size contract compares decoded pixels on both axes: output width must be at least the exact managed pre-Enhancement Working input width, and output height must be at least that input height. PNG, Revision, source integrity, lock release and Operator visual review remain separate requirements.'
            } else {
                'Meitu AI enhancement is not a deterministic function of its input — the accepted preset records the feature, not a byte contract — so an exact-hash expectation would fail for a reason that is not a regression. The structural contract (a PNG revision exists, is larger than the source, the source is untouched, the lock is released) is what an upgrade can actually break.'
            }
        }
        manualChecks = @(
            [ordered] @{
                id = 'PORTRAIT-VISUAL-001'
                question = 'Does the enhanced export still look like a correctly enhanced portrait — subject sharp, skin tone unshifted, no visible artefact introduced along the hair or shoulder edges?'
                decidedBy = 'Operator'
                automatable = $false
                reason = 'Enhancement quality is a judgement. A pixel threshold invented to automate it would be a number nobody derived from a print test, and SCRUM-11095 already records why this project refuses invented thresholds.'
            }
        )
        notes = 'The ordinary case. If this one needs an operator takeover after an upgrade, the upgrade broke the common path.'
    },

    [ordered] @{
        id = 'FIX-FINE-HAIR-001'
        category = 'COMPLEX_BACKGROUND_FINE_HAIR'
        file = Join-Path $inputs 'FIX-FINE-HAIR-001.jpg'
        kind = 'Raster'
        privacy = $privacySynthetic
        provenance = [ordered] @{
            classification = 'Synthetic'
            producedBy     = 'tools/regression/New-PrintFlowRegressionAssets.ps1'
            derivedFrom    = $null
            rationale      = 'Same subject as FIX-PORTRAIT-001 so the two differ only in the property under test. Around 7,200 individual hair strands of 0.45-1.4 px are drawn leaving the scalp and crossing a background of overlapping soft foliage shapes and 5,200 short strokes at the same spatial frequency as the hair itself.'
            honestLimits   = 'Synthetic, and the strands are drawn rather than photographed. What it genuinely contains is narrow high-frequency boundary detail against a non-uniform background, which is the property the category exists for; it is not a claim about how Meitu handles real photographed hair.'
        }
        expectedWorkflow = 'PrepareAsset'
        expectedProcessingPath = @(
            'Import (internal, produces the root Revision)'
            'OriginalConfirmation (operator confirms; no adapter)'
            'Enhancement — skipped: this case is about the cutout boundary, and enhancing first would change what background removal is judged on'
            'BackgroundRemoval — OperationKind.RemoveBackground, AdapterKind.Meitu, RequiresReview. This is the case that must genuinely exercise the Meitu background-removal path'
            'Trim — OperationKind.Trim, AdapterKind.Internal, cropping to the alpha bounds the cutout produced'
            'ApprovedPngExport — OperationKind.PromoteApproved'
        )
        expectedExternalApplications = @('Meitu XiuXiu 7.8.7.5')
        expectedProperties = [ordered] @{
            importAccepted                 = $true
            backgroundRemovalProducesRevision = $true
            cutoutOutputFormat             = 'PNG'
            cutoutHasRealTransparency      = $true
            trimBoundsStrictlyInsideCanvas = $true
            sourceBytesUnchanged           = $true
            automationLockFreeAfterStep    = $true
        }
        comparisonPolicy = [ordered] @{
            mode   = 'Structural'
            reason = if ($SetVersion -eq 'v3') {
                'The cutout is AI output. What is checked deterministically is that transparency is real (an alpha channel with genuinely varying alpha, not an opaque 32-bit image) and that the trim geometry is exact: the smallest rectangle holding every alpha > 0 pixel of the actual pre-Trim cutout, computed independently, grown by the successful Trim attempt''s recorded margins and clamped to the canvas, equals the persisted ContentBounds and AppliedBounds, and the decoded Trim output equals exactly that region of the decoded cutout. A full-canvas output is correct only when that computed rectangle is the full canvas; an unchanged copy fails whenever a removable border exists, and matching dimensions alone never pass. This does not decide whether alpha-bearing edge pixels are desirable foreground. Hair-edge quality and background removal are the manual check below, and are deliberately not reduced to a threshold.'
            } else {
                'The cutout is AI output. What is checked deterministically is that transparency is real (an alpha channel with genuinely varying alpha, not an opaque 32-bit image) and that the trim found bounds inside the canvas. Hair-edge quality is the manual check below, and is deliberately not reduced to a threshold.'
            }
        }
        manualChecks = @(
            [ordered] @{
                id = 'FINE-HAIR-VISUAL-001'
                question = 'Are individual hair strands still retained at the boundary, without a hard halo, and is the foliage background fully removed rather than partly retained as coloured fringing?'
                decidedBy = 'Operator'
                automatable = $false
                reason = 'This is the judgement the category exists to put in front of a person. An automated pass here would be the set lying about its own coverage.'
            }
        )
        notes = 'The hardest visual case in the set. A Meitu upgrade that regresses matting shows up here first.'
    },

    [ordered] @{
        id = 'FIX-TRANSPARENT-001'
        category = 'TRANSPARENT_PNG'
        file = Join-Path $inputs 'FIX-TRANSPARENT-001.png'
        kind = 'Raster'
        privacy = $privacyCustomerLike
        provenance = [ordered] @{
            classification = 'ExistingValidatedReference'
            producedBy     = 'Meitu XiuXiu 7.8.7.5 smart cutout, accepted 2026-08-18'
            derivedFrom    = Join-Path $expected 'FIX-CUSTOMER-DESIGN-001_CUTOUT.png'
            rationale      = 'A byte-identical copy of MEITU_SMART_CUTOUT_REFERENCE, which the accepted preset 1.16.0 already lists under acceptedReferenceOutputs with this exact SHA-256. Its alpha is a real cutout''s alpha — soft, with hair edges — which a generated one would not be, and it is already approved for permanent local regression use.'
            honestLimits   = 'It is an output of this product being reused as an input. That is exactly the transparent-PNG case an operator hits when re-importing an approved asset, so the reuse is the point rather than a shortcut.'
        }
        expectedWorkflow = 'PrepareAsset'
        expectedProcessingPath = @(
            'Import (internal; PNG is decoded at import, so a damaged file would be refused here)'
            'OriginalConfirmation (operator confirms; no adapter)'
            'Enhancement — skipped: the artwork is already an approved export'
            'BackgroundRemoval — skipped: the background is already removed, which is the whole point of this category'
            'Trim — OperationKind.Trim, AdapterKind.Internal. Deterministic alpha-bounds crop, no external application'
            'ApprovedPngExport — OperationKind.PromoteApproved'
        )
        expectedExternalApplications = @()
        expectedProperties = [ordered] @{
            importAccepted            = $true
            alphaPreservedThroughImport = $true
            trimIsDeterministic       = $true
            expectedTrimBounds        = [ordered] @{ x = 229; y = 1210; width = 2724; height = 3685 }
            trimBoundsSource          = 'nonEmptyAlphaBounds recorded on the accepted FIX-CUSTOMER-DESIGN-001 manifest for these exact bytes'
            trimmedOutputRetainsAlpha = $true
            sourceBytesUnchanged      = $true
        }
        comparisonPolicy = [ordered] @{
            mode   = 'ReferenceProperties'
            reason = 'Nothing here is AI output: fixed input bytes through a deterministic internal trim. The alpha bounding box is therefore an exact expectation and is stated as one. The trimmed file''s own hash is recorded by the first accepted run and becomes an ExactHash expectation from the second run onward — it is not asserted here, because no run has produced it yet and a hash nobody has seen is not an expectation.'
        }
        manualChecks = @()
        notes = 'The determinism anchor of the set. If this case ever needs a judgement call, something that should be arithmetic has stopped being arithmetic.'
    },

    [ordered] @{
        id = 'FIX-CUSTOMER-DESIGN-001'
        category = 'COMPLETE_CUSTOMER_DESIGN'
        file = Join-Path $inputs 'FIX-CUSTOMER-DESIGN-001.jpeg'
        kind = 'Raster'
        privacy = $privacyCustomerLike
        provenance = [ordered] @{
            classification = 'ProjectOwnedTestAsset'
            producedBy     = 'Accepted into the set on 2026-08-17 as APPROVED_LOCAL_ONLY for permanent local PrintFlow regression use'
            derivedFrom    = $null
            rationale      = 'The one genuinely customer-like complete design the project already owns and has explicitly designated. Its source bytes are unchanged by this slice.'
            honestLimits   = 'Contains an identifiable person and a memorial design. Local only: never committed, never uploaded.'
        }
        expectedWorkflow = 'PrepareCustomerDesign'
        expectedProcessingPath = @(
            'Import (internal, produces the root Revision)'
            'OriginalConfirmation (operator confirms; no adapter)'
            'Enhancement — skipped: the design is finished, and the accepted enhanced reference already exists at expected\FIX-CUSTOMER-DESIGN-001_HD.png'
            'BackgroundRemoval — skipped: WorkflowCatalog marks this step skippable precisely because a finished design may intentionally keep its background'
            'Trim — the source is an opaque JPEG with no alpha to measure, so the operator records KeepOriginalExtent'
            'PrintDimensions — operator sets the production size; no adapter'
            'SelectWhiteUnderbaseBranch — W1_1px, the preset''s branch for ordinary complete designs'
            'PhotoshopOutput — OperationKind.PhotoshopOutput, AdapterKind.Photoshop. Signed Action, CMYK + W1, TIFF validation, then final review'
        )
        expectedExternalApplications = @('Adobe Photoshop CC 2019 20.0.10')
        expectedProperties = [ordered] @{
            importAccepted             = $true
            producesProductionTiff     = $true
            tiffColourStructure        = 'Separated CMYK plus named W1 spot channel'
            tiffSamplesPerPixel        = 5
            tiffResolutionDpi          = 300
            tiffCompression            = 'None'
            sourceBytesUnchanged       = $true
            automationLockFreeAfterStep = $true
        }
        comparisonPolicy = [ordered] @{
            mode   = 'Structural'
            reason = 'The TIFF''s structure is a hard contract and is checked as one. Its bytes are not: the Action runs against a Photoshop document whose metadata carries the moment it was written, so two correct runs differ.'
        }
        manualChecks = @(
            [ordered] @{
                id = 'CUSTOMER-DESIGN-VISUAL-001'
                question = 'Does the produced TIFF show the complete design at the requested size, with the W1 channel covering the intended ink region?'
                decidedBy = 'Operator'
                automatable = $false
                reason = 'The structural check proves the channel exists; whether it covers the right region is a look.'
            }
        )
        notes = 'Carried over from the pre-existing single-asset set. This slice replaces its "finalTiff": "PENDING" with the outcome of an actual run — see expectedResults.finalTiff.'
    },

    [ordered] @{
        id = 'FIX-PSD-001'
        category = 'PSD_WITH_COMPOSITE_PREVIEW'
        file = Join-Path $inputs 'FIX-PSD-001.psd'
        kind = 'Psd'
        privacy = $privacySynthetic
        provenance = [ordered] @{
            classification = 'DerivedFromProjectOwnedTestAsset'
            producedBy     = 'Adobe Photoshop CC 2019 20.0.10, driven by tools/regression/New-PrintFlowRegressionPsd.ps1'
            derivedFrom    = 'FIX-PORTRAIT-001'
            rationale      = 'Written by Photoshop itself, with Maximize Compatibility forced on for the save and the workstation preference restored afterwards. A PSD assembled by a script could be made to carry image resource 1057, but it would then be evidence about the script rather than about Photoshop.'
            honestLimits   = 'Not byte-reproducible: Photoshop stamps XMP metadata with the moment of writing. The accepted bytes are fixed by the sha256 below, and regenerating this asset is an explicit rebaseline of the set.'
        }
        expectedWorkflow = 'GeneratePrintTiff (WorkflowCatalog.For(type, preparePsd: true))'
        expectedProcessingPath = @(
            'Import (internal; PSD carries no pixel metadata at import and is deliberately not decoded here)'
            'OriginalConfirmation — rewritten by the catalog to OperationKind.PreparePsd, AdapterKind.Photoshop, ProducesRevision, RequiresReview. PsdCompositeProbe refuses a missing composite before Photoshop is opened at all'
            'PrintDimensions — operator sets the production size; no adapter'
            'SelectWhiteUnderbaseBranch — W1_2px, the preset''s branch for full rectangular or similarly solid designs'
            'PhotoshopOutput — OperationKind.PhotoshopOutput, AdapterKind.Photoshop'
        )
        expectedExternalApplications = @('Adobe Photoshop CC 2019 20.0.10')
        expectedProperties = [ordered] @{
            hasRealMergedData           = $true
            compositeResourceId         = 1057
            colourMode                  = 'RGB'
            bitsPerChannel              = 8
            preparedRasterFormat        = 'PNG'
            preparedRasterColourMode    = 'Rgb'
            sourcePsdUnchangedByPreparation = $true
            photoshopReturnsToStartingState = $true
            automationLockFreeAfterStep = $true
        }
        comparisonPolicy = [ordered] @{
            mode   = 'Structural'
            reason = 'The managed raster is produced by Photoshop and inherits its metadata, so it is compared structurally. The one exact expectation is on the source: its SHA-256 must be unchanged after preparation, because "the source is never modified" is a hard product rule and not a judgement.'
        }
        manualChecks = @()
        notes = 'A three-layer document — backdrop, artwork, and a partially transparent overlay in Overlay mode — so the composite preview and the top layer are genuinely different pictures. A single-layer PSD would make the composite check vacuous.'
    },

    [ordered] @{
        id = 'FIX-PDF-001'
        category = 'SINGLE_PAGE_PDF'
        file = Join-Path $inputs 'FIX-PDF-001.pdf'
        kind = 'Pdf'
        privacy = $privacySynthetic
        provenance = [ordered] @{
            classification = 'DerivedFromProjectOwnedTestAsset'
            producedBy     = 'tools/regression/New-PrintFlowRegressionAssets.ps1'
            derivedFrom    = 'FIX-PORTRAIT-001'
            rationale      = 'Assembled byte by byte around the synthetic portrait as a single DCTDecode image XObject. Printing to a PDF driver would have embedded the machine name, driver version and the moment of printing, so the same design would produce different bytes on every run and the fixed hash this set depends on would be a fiction.'
            honestLimits   = 'One image on one page. It is the positive single-page case; multi-page refusal is a separate Product test (PdfPreparationWorkstationSmoke) and is deliberately not represented here.'
        }
        expectedWorkflow = 'GeneratePrintTiff (WorkflowCatalog.For(type, preparePdf: true))'
        expectedProcessingPath = @(
            'Import (internal; PDF carries no pixel metadata at import and is deliberately not decoded here)'
            'OriginalConfirmation — rewritten by the catalog to OperationKind.PreparePdf, AdapterKind.Pdf. WindowsPdfPreparationProcessor rasterises the single page at 300 PPI through Windows.Data.Pdf; no external application is involved'
            'PrintDimensions — operator sets the production size; no adapter'
            'SelectWhiteUnderbaseBranch — W1_2px, the preset''s branch for full rectangular or similarly solid designs'
            'PhotoshopOutput — OperationKind.PhotoshopOutput, AdapterKind.Photoshop'
        )
        expectedExternalApplications = @('Adobe Photoshop CC 2019 20.0.10')
        expectedProperties = [ordered] @{
            pageCount                = 1
            isEncrypted              = $false
            rotationDegrees          = 0
            requestedRasterDpi       = 300
            preparedPageNumber       = 1
            sourcePdfUnchangedByPreparation = $true
            rasterMatchesPageGeometry = 'PixelWidth = round(PageWidthDip * 300 / 96), and likewise for height'
        }
        comparisonPolicy = [ordered] @{
            mode   = 'ReferenceProperties'
            reason = 'PDF rasterisation is deterministic for fixed bytes at a fixed DPI, so the raster dimensions are exact expectations derived from the page geometry rather than copied numbers. The raster''s own hash is recorded by the first accepted run.'
        }
        manualChecks = @()
        notes = 'Page geometry is stated in device-independent pixels because that is what Windows.Data.Pdf reports and what PdfInspection.RasterPixels converts from.'
    },

    [ordered] @{
        id = 'FIX-REFERENCE-TIFF-001'
        category = 'REFERENCE_PRODUCTION_TIFF'
        file = Join-Path $reference 'FIX-REFERENCE-TIFF-001.tif'
        kind = 'Tiff'
        privacy = $privacyCustomerLike
        provenance = [ordered] @{
            classification = 'ExistingValidatedReference'
            producedBy     = 'Adobe Photoshop CC 2019, W1_1px Action, accepted 2026-08-18'
            derivedFrom    = 'D:\PrintFlowStudio\Baseline\workstation-v1\actions\authoring\FIX-CUSTOMER-DESIGN-001_W1-1PX.tif'
            rationale      = 'A byte-identical copy of the artefact the accepted preset names in tiffContract.referenceArtifact, with the same SHA-256. Epic 11000 records that Maintop RIP v6.1 loaded and previewed this exact file without visible error and that its default W1 handling matched routine production practice.'
            honestLimits   = 'The Maintop evidence is a load-and-preview acceptance from 2026-08-18, not a physical print: Epic 11000 states explicitly that no separate print was produced or photographed. Copying this file into the regression set adds no new Maintop evidence and no new print evidence, and does not advance SCRUM-11067, SCRUM-11134 or SCRUM-11135.'
        }
        expectedWorkflow = 'None — reference artefact only'
        expectedProcessingPath = @(
            'Not imported. TIFF is deliberately absent from SupportedInputFormats: it is an output of this product, and design section 9.2 gives it no input rule'
            'The run asserts the refusal rather than assuming it — a Home import of this file must fail as an unsupported format, and must not create a session'
            'Its structure is read directly from the TIFF IFD and compared with the accepted preset''s tiffContract'
        )
        expectedExternalApplications = @()
        expectedProperties = [ordered] @{
            refusedAsHomeInput        = $true
            colourStructure           = 'Separated CMYK plus named W1 spot channel'
            photometricInterpretation = 5
            samplesPerPixel           = 5
            bitsPerSample             = @(8, 8, 8, 8, 8)
            compression               = 1
            planarConfiguration       = 1
            resolutionDpi             = @(300, 300)
            extraSamples              = @(0)
            maintopEvidence           = 'Loaded and previewed without visible error in Maintop RIP v6.1 (Flatbed UV Personal Edition) on 2026-08-18. No physical print.'
        }
        comparisonPolicy = [ordered] @{
            mode   = 'ExactHash'
            reason = 'It is a fixed archived artefact, not something the product regenerates. Its bytes are the expectation, and any change to them means the reference was replaced rather than that a run behaved differently.'
        }
        manualChecks = @()
        notes = 'Present so PrintFlow-produced TIFFs have something to be compared against, and so the "TIFF is not an input" rule has a regression case rather than only a unit test.'
    }
)

if ($SetVersion -in @('v2', 'v3')) {
    $assets[0].expectedProperties.enhancedOutputIsNotSmallerThanSource = $true
} else {
    $assets[0].expectedProperties.enhancedOutputIsLargerThanSource = $true
}

# v3's one approved expectation change, in the same position so a manifest diff shows a swap and
# nothing else. The v2 property is removed rather than set false: its meaning is not reinterpreted.
if ($SetVersion -eq 'v3') {
    $fineHair = $assets[1].expectedProperties
    $position = @($fineHair.Keys).IndexOf('trimBoundsStrictlyInsideCanvas')
    if ($position -lt 0) {
        throw 'FIX-FINE-HAIR-001 no longer authors trimBoundsStrictlyInsideCanvas, so the v3 swap has nothing to replace.'
    }
    $fineHair.Remove('trimBoundsStrictlyInsideCanvas')
    $fineHair.Insert($position, 'trimMatchesAlphaBoundsAndMargins', $true)
}

# ------------------------------------------------------------------------------------------
# Emit
# ------------------------------------------------------------------------------------------
$written = @()
foreach ($asset in $assets) {
    if (-not (Test-Path -LiteralPath $asset.file)) {
        throw "Asset '$($asset.id)' is missing at '$($asset.file)'. Run New-PrintFlowRegressionAssets.ps1 (and New-PrintFlowRegressionPsd.ps1) first."
    }

    $facts = Get-FileFacts $asset.file
    switch ($asset.kind) {
        'Raster' { Add-RasterFacts $facts $asset.file; $facts.format = if ($facts.extension -eq '.png') { 'PNG' } else { 'JPEG' } }
        'Psd'    { $facts.format = 'PSD'; $facts.psd = Get-PsdFacts $asset.file }
        'Pdf'    { $facts.format = 'PDF'; $facts.pdf = Get-PdfFacts $asset.file }
        'Tiff'   { $facts.format = 'TIFF'; $facts.tiff = Get-TiffFacts $asset.file }
    }

    $manifest = [ordered] @{
        schemaVersion     = $SchemaVersion
        setId             = $SetId
        fixtureSetVersion = $SetVersion
        fixtureId         = $asset.id
        category          = $asset.category
        status            = 'APPROVED_LOCAL_ONLY'
        approval          = 'Permanent local PrintFlow regression use approved; do not upload or commit to Git.'
        privacy           = $asset.privacy
        provenance        = $asset.provenance
        file              = $facts
        expectedWorkflow  = $asset.expectedWorkflow
        expectedProcessingPath = $asset.expectedProcessingPath
        expectedExternalApplications = $asset.expectedExternalApplications
        expectedProperties = $asset.expectedProperties
        comparisonPolicy  = $asset.comparisonPolicy
        manualChecks      = $asset.manualChecks
        notes             = $asset.notes
    }

    # The pre-existing asset keeps every fact its first manifest recorded, including the two
    # accepted Meitu reference outputs and the operator acceptances behind them. Those are real
    # history and this slice has no business dropping them; what it does change is the one field
    # that was never an answer.
    if ($asset.id -eq 'FIX-CUSTOMER-DESIGN-001') {
        $manifest.approvedBy = 'DESKTOP-0BG8884\admin'
        $manifest.approvedAtLocal = '2026-08-17T17:06:57.3529802+12:00'
        $manifest.intendedWorkflow = 'PREPARE_CUSTOMER_DESIGN'
        $manifest.expectedResults = [ordered] @{
            enhancedExport = [ordered] @{
                status = 'APPROVED_REFERENCE_OUTPUT'
                path = Join-Path $expected 'FIX-CUSTOMER-DESIGN-001_HD.png'
                sha256 = 'E90A7FE2972209744E3829CA4380574A98B75ECF02B820F2EE707843853C4903'
                format = 'PNG'; length = 13831390; widthPixels = 3412; heightPixels = 5120
                hasActualTransparency = $false
            }
            visualAcceptance = [ordered] @{
                status = 'ACCEPTED'; acceptedBy = 'DESKTOP-0BG8884\admin'
                acceptedAtLocal = '2026-08-18T11:44:57.7411655+12:00'
            }
            cutoutExport = [ordered] @{
                status = 'APPROVED_REFERENCE_OUTPUT'
                path = Join-Path $expected 'FIX-CUSTOMER-DESIGN-001_CUTOUT.png'
                sha256 = 'A20A722DB394B8CBBAE7975CC930DD456971E913E5844C21F34B27B9C4D377E2'
                format = 'PNG'; length = 3895181; widthPixels = 3412; heightPixels = 5120
                hasActualTransparency = $true
                nonEmptyAlphaBounds = [ordered] @{ x = 229; y = 1210; width = 2724; height = 3685 }
            }
            cutoutVisualAcceptance = [ordered] @{
                status = 'ACCEPTED'; mode = '人像宠物'; retainedContent = 'person and chair'
                acceptedBy = 'DESKTOP-0BG8884\admin'
                acceptedAtLocal = '2026-08-18T11:55:45.7340376+12:00'
            }
            finalTiff = [ordered] @{
                status = 'EXPECTED_FROM_REGRESSION_RUN'
                resolves = 'The "PENDING" this field held from 2026-08-17 until SCRUM-11065.'
                expectation = 'The PhotoshopOutput step produces a validated production TIFF: separated CMYK plus a named W1 spot channel, 5 samples per pixel, 300 dpi, no image compression. The produced file is recorded per run under runs\<run-id>\, not fixed here, because its bytes carry the moment Photoshop wrote them.'
            }
        }
    }

    $path = Join-Path $manifests "$($asset.id).json"
    $manifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $path -Encoding utf8
    $written += $path
    Write-Host ("  {0,-26} {1,-28} {2}" -f $asset.id, $asset.category, $facts.sha256.Substring(0, 16)) -ForegroundColor Green
}

# ------------------------------------------------------------------------------------------
# The set index
# ------------------------------------------------------------------------------------------
$index = [ordered] @{
    schemaVersion = $SchemaVersion
    setId         = $SetId
    setVersion    = $SetVersion
    root          = $SetRoot
    status        = 'ACCEPTED_FIXED_REGRESSION_SET'
    immutability  = "Accepted input bytes and their SHA-256 are regression authority. Changing what a category contains creates a new set version or is an explicit, recorded rebaseline of this one; it is never a silent edit of $SetVersion."
    requiredCategories = @(
        'NORMAL_JPG_PORTRAIT', 'COMPLEX_BACKGROUND_FINE_HAIR', 'TRANSPARENT_PNG',
        'COMPLETE_CUSTOMER_DESIGN', 'PSD_WITH_COMPOSITE_PREVIEW', 'SINGLE_PAGE_PDF',
        'REFERENCE_PRODUCTION_TIFF')
    assets        = @($assets | ForEach-Object { [ordered] @{ id = $_.id; category = $_.category; manifest = "manifests\$($_.id).json" } })
    privacy       = [ordered] @{
        localOnly = $true; gitAllowed = $false; uploadAllowed = $false
        note = 'The set is customer-like local test data. It lives on the fixed workstation and outside the repository; tools/regression rebuilds it.'
    }
    generatedBy   = 'tools/regression/New-PrintFlowRegressionManifests.ps1'
    generatedAtLocal = (Get-Date).ToString('o')
}
$indexPath = Join-Path $SetRoot 'set.json'
$index | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $indexPath -Encoding utf8

Write-Host ''
Write-Host ("Wrote {0} manifests and {1}" -f $written.Count, $indexPath) -ForegroundColor Cyan
