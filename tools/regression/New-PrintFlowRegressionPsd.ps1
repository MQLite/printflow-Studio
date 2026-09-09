<#
.SYNOPSIS
    Authors the PSD_WITH_COMPOSITE_PREVIEW input of the standard regression set, in Photoshop.

.DESCRIPTION
    PrintFlow refuses a PSD that does not carry Adobe image resource 1057 with
    hasRealMergedData = true — the flag Photoshop writes when Maximize Compatibility is on, and
    the only thing the Product accepts as a composite preview (PsdCompositeProbe). A PSD
    assembled by anything other than Photoshop could be made to carry that flag, but it would
    then be evidence about this script rather than about Photoshop, which is the application the
    regression set exists to keep honest. So Photoshop writes it.

    What this does, in order:

      1. Binds the accepted Photoshop through the Running Object Table and refuses anything
         whose path and version are not the accepted ones. There is no fallback to "some
         Photoshop".
      2. Refuses to start if any document is open. A document open here is either an operator's
         work or a leftover, and this script will not decide which.
      3. Opens a scratch copy of the synthetic portrait — never the regression input itself, so
         the accepted source bytes cannot be touched even by an unexpected save.
      4. Builds a genuinely layered document: the artwork as a normal layer, a soft tone layer
         above it, and a solid backdrop below. A single-layer PSD has a composite preview that
         is identical to its only layer, which would prove nothing about compositing.
      5. Saves it as the regression PSD with Maximize Compatibility forced on, then restores the
         preference to whatever the workstation had. The accepted Photoshop configuration is
         part of the baseline and this script does not get to change it permanently.
      6. Closes without saving and deletes the scratch copy.
      7. Re-reads the written file and proves resource 1057 says hasRealMergedData = true,
         rather than trusting that step 5 did what it was asked.

    NOT byte-reproducible. Photoshop stamps XMP metadata with the moment of writing, so a rerun
    produces a different SHA-256 for the same picture. The accepted file's hash is therefore
    fixed by the manifest, and regenerating this asset is an explicit rebaseline of the set —
    which is exactly the property SCRUM-11065 asks for.

.PARAMETER SetRoot
    The regression set root. Defaults to D:\PrintFlowStudio\TestData\v1.

.PARAMETER AcceptedPhotoshopPath
    The accepted installation directory, as the preset's photoshopContract names it.

.PARAMETER AcceptedPhotoshopVersion
    The accepted Photoshop scripting version.

.PARAMETER Force
    Overwrite an existing regression PSD. Without it, an existing one is left alone.
#>
[CmdletBinding()]
param(
    [string] $SetRoot = 'D:\PrintFlowStudio\TestData\v1',
    [string] $AcceptedPhotoshopPath = 'D:\Adobe Photoshop CC 2019',
    [string] $AcceptedPhotoshopVersion = '20.0.10',
    [switch] $Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$target = Join-Path $SetRoot 'inputs\FIX-PSD-001.psd'
$artwork = Join-Path $SetRoot 'inputs\FIX-PORTRAIT-001.jpg'

if ((Test-Path -LiteralPath $target) -and -not $Force) {
    Write-Host "FIX-PSD-001.psd already exists; left untouched." -ForegroundColor DarkGray
    return
}
if (-not (Test-Path -LiteralPath $artwork)) {
    throw "The synthetic portrait '$artwork' must exist first (New-PrintFlowRegressionAssets.ps1)."
}

# ------------------------------------------------------------------------------------------
# Bind the accepted Photoshop through the ROT
# ------------------------------------------------------------------------------------------
# The same mechanism the Product's own runtime fact reader uses. ProgID activation is not used:
# it would happily start a second Photoshop, and "a Photoshop was started" is not "the accepted
# Photoshop answered".
if (-not ('PrintFlowRegressionRot' -as [type])) {
    Add-Type -Language CSharp -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

public static class PrintFlowRegressionRot
{
    [DllImport("ole32.dll")] private static extern int GetRunningObjectTable(int reserved, out IRunningObjectTable rot);
    [DllImport("ole32.dll")] private static extern int CreateBindCtx(int reserved, out IBindCtx ctx);

    // Written for the C# 5 compiler Windows PowerShell 5.1 hosts: no inline out-variables and
    // no interpolation. Keeping the script runnable in the shell the workstation actually has
    // matters more here than modern syntax.
    public static object Find(string acceptedPath, string acceptedVersion, out string described)
    {
        IRunningObjectTable table;
        IBindCtx context;
        IEnumMoniker monikers;
        Marshal.ThrowExceptionForHR(GetRunningObjectTable(0, out table));
        Marshal.ThrowExceptionForHR(CreateBindCtx(0, out context));
        table.EnumRunning(out monikers);
        IMoniker[] current = new IMoniker[1];
        List<string> seen = new List<string>();
        object accepted = null;
        string acceptedDescription = null;
        while (monikers.Next(1, current, IntPtr.Zero) == 0)
        {
            object running = null;
            try
            {
                table.GetObject(current[0], out running);
                string version = Get(running, "Version");
                string path = Get(running, "Path");
                if (version == null || path == null) { continue; }
                seen.Add(path + " | " + version);
                if (string.Equals(path.TrimEnd('\\'), acceptedPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) &&
                    version == acceptedVersion)
                {
                    accepted = running;
                    acceptedDescription = path + " | " + version;
                    running = null;
                }
            }
            catch (COMException) { }
            finally { if (running != null) { Marshal.ReleaseComObject(running); } }
        }
        described = acceptedDescription ?? ("none accepted; saw: " + string.Join(", ", seen));
        return accepted;
    }

    private static string Get(object target, string property)
    {
        try
        {
            object value = target.GetType().InvokeMember(
                property, System.Reflection.BindingFlags.GetProperty, null, target, null);
            return value == null ? null : value.ToString();
        }
        catch (Exception) { return null; }
    }

    public static string DoJavaScript(object application, string script)
    {
        object value = application.GetType().InvokeMember(
            "DoJavaScript", System.Reflection.BindingFlags.InvokeMethod, null, application, new object[] { script });
        return value == null ? string.Empty : value.ToString();
    }
}
'@
}

$described = $null
$photoshop = [PrintFlowRegressionRot]::Find($AcceptedPhotoshopPath, $AcceptedPhotoshopVersion, [ref] $described)
if ($null -eq $photoshop) {
    throw "The accepted Photoshop ($AcceptedPhotoshopPath, $AcceptedPhotoshopVersion) is not running: $described"
}
Write-Host "Bound accepted Photoshop: $described" -ForegroundColor Cyan

$scratch = Join-Path ([System.IO.Path]::GetTempPath()) ("printflow-regression-psd-" + [Guid]::NewGuid().ToString('N') + '.jpg')
Copy-Item -LiteralPath $artwork -Destination $scratch -Force

# ------------------------------------------------------------------------------------------
# Author the document
# ------------------------------------------------------------------------------------------
$jsx = @"
(function () {
    var report = [];
    if (app.documents.length !== 0) {
        return 'REFUSED documents-open=' + app.documents.length;
    }

    var previousCompatibility = app.preferences.maximizeCompatibility;
    var previousUnits = app.preferences.rulerUnits;
    app.preferences.rulerUnits = Units.PIXELS;

    var doc = null;
    try {
        doc = app.open(new File('$($scratch -replace '\\','\\\\')'));
        if (doc.mode !== DocumentMode.RGB) { doc.changeMode(ChangeMode.RGB); }
        if (doc.bitsPerChannel !== BitsPerChannelType.EIGHT) { doc.bitsPerChannel = BitsPerChannelType.EIGHT; }

        // The opened JPEG arrives as a locked background. Promoting it is what makes the file a
        // layered design rather than a picture in a PSD wrapper.
        var art = doc.artLayers[0];
        art.isBackgroundLayer = false;
        art.name = 'Artwork';

        // A solid backdrop underneath, so the composite is a real composite of more than one
        // visible layer even where the artwork is opaque.
        var backdrop = doc.artLayers.add();
        backdrop.name = 'Backdrop';
        backdrop.move(art, ElementPlacement.PLACEAFTER);
        doc.activeLayer = backdrop;
        doc.selection.selectAll();
        var fill = new SolidColor();
        fill.rgb.red = 240; fill.rgb.green = 236; fill.rgb.blue = 228;
        doc.selection.fill(fill);
        doc.selection.deselect();

        // A partially transparent tone layer above, so flattening is not a no-op: the composite
        // preview and the top layer are genuinely different pictures.
        var tone = doc.artLayers.add();
        tone.name = 'Warm tone';
        tone.move(art, ElementPlacement.PLACEBEFORE);
        doc.activeLayer = tone;
        doc.selection.select([[0, 0], [doc.width, 0], [doc.width, doc.height * 0.34], [0, doc.height * 0.34]]);
        var warm = new SolidColor();
        warm.rgb.red = 255; warm.rgb.green = 214; warm.rgb.blue = 168;
        doc.selection.fill(warm);
        doc.selection.deselect();
        tone.opacity = 22;
        tone.blendMode = BlendMode.OVERLAY;

        report.push('layers=' + doc.layers.length);
        report.push('width=' + doc.width.value);
        report.push('height=' + doc.height.value);
        report.push('mode=' + doc.mode.toString());
        report.push('bits=' + doc.bitsPerChannel.toString());
        report.push('previousCompatibility=' + previousCompatibility.toString());

        app.preferences.maximizeCompatibility = QueryStateType.ALWAYS;

        var options = new PhotoshopSaveOptions();
        options.alphaChannels = false;
        options.annotations = false;
        options.embedColorProfile = true;
        options.layers = true;
        options.spotColors = false;
        doc.saveAs(new File('$($target -replace '\\','\\\\')'), options, false, Extension.LOWERCASE);
        report.push('saved=1');
    } finally {
        try { app.preferences.maximizeCompatibility = previousCompatibility; } catch (e) {}
        try { app.preferences.rulerUnits = previousUnits; } catch (e) {}
        try { if (doc !== null) { doc.close(SaveOptions.DONOTSAVECHANGES); } } catch (e) {}
    }
    report.push('documentsAfter=' + app.documents.length);
    return report.join('\n');
}());
"@

try {
    $result = [PrintFlowRegressionRot]::DoJavaScript($photoshop, $jsx)
} finally {
    Remove-Item -LiteralPath $scratch -Force -ErrorAction SilentlyContinue
    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($photoshop)
}

Write-Host $result
if ($result -like 'REFUSED*') { throw "Photoshop was not in a safe starting state: $result" }
if (-not (Test-Path -LiteralPath $target)) { throw "Photoshop reported no error but '$target' was not written." }

# ------------------------------------------------------------------------------------------
# Prove the composite preview, rather than trusting the save
# ------------------------------------------------------------------------------------------
# A deliberate re-implementation of the envelope walk in PsdCompositeProbe: same file, same
# resource, read independently. If these two ever disagree, the set is wrong and this says so
# before the file is ever accepted into it.
function Test-PsdComposite([string] $Path) {
    # PSD is big-endian throughout. BitConverter is little-endian on this platform, so every
    # multi-byte field is reversed before conversion. An earlier version of this used shift-and-or
    # on [byte] operands, which PowerShell does not widen the way C does — it silently kept only
    # the low byte and reported a 1200x1600 document as 176x64.
    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $reader = New-Object System.IO.BinaryReader($stream)
        $readU32 = { $b = $reader.ReadBytes(4); [Array]::Reverse($b); [System.BitConverter]::ToUInt32($b, 0) }
        $readU16 = { $b = $reader.ReadBytes(2); [Array]::Reverse($b); [System.BitConverter]::ToUInt16($b, 0) }

        if ([System.Text.Encoding]::ASCII.GetString($reader.ReadBytes(4)) -ne '8BPS') { throw 'Not a PSD.' }
        if ((& $readU16) -ne 1) { throw 'Not PSD version 1.' }
        $null = $reader.ReadBytes(6)
        $channels = & $readU16
        $height = & $readU32
        $width = & $readU32
        $depth = & $readU16
        $mode = & $readU16

        # Each length is read into a variable before the stream is moved. `$stream.Position +=
        # (& $readU32)` looks equivalent and is not: PowerShell reads the property for the
        # compound assignment before evaluating the operand, so assigning the sum silently
        # rewinds over the four bytes the length itself occupied — which lands the resource walk
        # on the colour-mode field and finds no resources at all.
        $colourModeLength = & $readU32
        $stream.Position = $stream.Position + $colourModeLength

        $resourceSectionLength = & $readU32
        $resourceEnd = $stream.Position + $resourceSectionLength
        $merged = $null
        while ($stream.Position -lt $resourceEnd) {
            if ([System.Text.Encoding]::ASCII.GetString($reader.ReadBytes(4)) -ne '8BIM') { throw 'Bad resource signature.' }
            $id = & $readU16
            $nameLength = $reader.ReadByte()
            $stream.Position = $stream.Position + $nameLength + ((($nameLength + 1) % 2))
            $size = & $readU32
            $end = $stream.Position + $size + ($size % 2)
            if ($id -eq 1057) {
                $null = & $readU32                          # version, always 1
                $merged = ($reader.ReadByte() -eq 1)
            }
            $stream.Position = $end
        }

        [pscustomobject]@{
            Width = $width; Height = $height; Channels = $channels; Depth = $depth
            Mode = $mode; HasRealMergedData = $merged
        }
    } finally { $stream.Dispose() }
}

$facts = Test-PsdComposite $target
Write-Host ''
Write-Host ("PSD envelope : {0}x{1}, {2} channels, {3}-bit, mode {4}" -f `
    $facts.Width, $facts.Height, $facts.Channels, $facts.Depth, $facts.Mode)
Write-Host ("Resource 1057 hasRealMergedData : {0}" -f $facts.HasRealMergedData)

# A file that fails is moved aside rather than deleted. Deleting it destroys the only evidence
# of why it failed, and the first version of this script did exactly that to a perfectly good
# PSD when the fault was in the reader above.
function Deny-Psd([string] $reason) {
    $rejected = "$target.rejected-" + (Get-Date).ToString('yyyyMMdd-HHmmss')
    Move-Item -LiteralPath $target -Destination $rejected -Force
    throw "$reason The file was moved to '$rejected' rather than accepted into the set."
}

if ($facts.HasRealMergedData -ne $true) {
    Deny-Psd "Photoshop wrote a PSD without a compatible composite preview (image resource 1057)."
}
if ($facts.Mode -ne 3) {
    Deny-Psd "The PSD is mode $($facts.Mode), not RGB; PrintFlow refuses non-RGB PSDs."
}

$hash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
Write-Host ''
Write-Host ("Wrote {0}" -f $target) -ForegroundColor Green
Write-Host ("  bytes  : {0:N0}" -f (Get-Item -LiteralPath $target).Length)
Write-Host ("  sha256 : {0}" -f $hash)
