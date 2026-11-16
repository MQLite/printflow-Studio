using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PrintFlow.Domain.AiDesign;
using PrintFlow.Domain.AiDesign.Memorial;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.AiDesign.Authoring;
using PrintFlow.Workflow.AiDesign.Authoring;
using PrintFlow.Workflow.AiDesign.Memorial;

namespace PrintFlow.Infrastructure.AiDesign.Memorial;

public sealed class WpfMemorialRenderer(VerifiedAiAttachmentReader attachments, WpfMemorialTextLayout textLayout,
    FileMemorialRenderStore renders, IAiAuthoringDraftStore drafts) : IMemorialRenderer
{
    private static readonly SemaphoreSlim Serial = new(1, 1);

    public async Task<OperationResult<MemorialRenderResult>> RenderAsync(MemorialRenderPlan plan, CancellationToken ct)
    {
        bool entered = false;
        var opened = new Dictionary<Guid, VerifiedAiAttachmentRead>();
        try
        {
            ct.ThrowIfCancellationRequested();
            if (plan is null || plan.DraftId == Guid.Empty || plan.DraftRevision < 0 || plan.Assets is null || !Enum.IsDefined(plan.Purpose))
                return Integrity();
            // Detach even direct port callers' lists before waiting for any I/O.
            plan = plan with { Layout = MemorialLayoutModule.Freeze(plan.Layout), Assets = Array.AsReadOnly(plan.Assets.ToArray()) };
            if (plan.Assets.Any(x => x is null) || plan.SourceManifestSha256 != MemorialCompositionModule.ComputeSourceManifest(plan))
                return Integrity();

            await Serial.WaitAsync(ct); entered = true;
            var saved = await drafts.ReadAsync(plan.DraftId, ct);
            if (saved.IsFailure) return OperationResult.Fail<MemorialRenderResult>(saved.Failure);
            if (saved.Value.Revision != plan.DraftRevision) return Integrity();
            var authority = MemorialCompositionModule.CreatePlan(saved.Value, plan.Purpose);
            if (authority.IsFailure) return OperationResult.Fail<MemorialRenderResult>(authority.Failure);
            if (plan.SourceManifestSha256 != authority.Value.SourceManifestSha256 ||
                !plan.Assets.OrderBy(x => x.Id).SequenceEqual(authority.Value.Assets)) return Integrity();
            plan = authority.Value;
            if (!MemorialRasterTarget.TryCreate(plan, out var target))
                return OperationResult.Fail<MemorialRenderResult>(FailureCode.PreconditionNotMet, "Memorial render exceeds the 64 million pixel limit.");

            var drawn = plan.Layout.Images.Select(x => x.AssetId).ToHashSet();
            foreach (var asset in plan.Assets)
            {
                var input = await attachments.OpenAsync(plan.DraftId, asset, ct);
                if (input.IsFailure) return OperationResult.Fail<MemorialRenderResult>(input.Failure);
                opened.Add(asset.Id, input.Value);
                if (drawn.Contains(asset.Id) && !await VerifiedAiAttachmentReader.HasImageHeaderAsync(input.Value.Stream, asset.MediaType, ct))
                    return OperationResult.Fail<MemorialRenderResult>(FailureCode.SourceFormatUnsupported, "A memorial layer is not a supported image.");
            }
            var preserved = saved.Value.Preservation.Where(x => x.KeepContent).Select(x => x.AssetId).ToHashSet();
            var artwork = await MemorialSta.RunAsync(() => Compose(plan, target, opened, preserved, ct));
            if (artwork.IsFailure) return OperationResult.Fail<MemorialRenderResult>(artwork.Failure);
            // Draft edits may continue during drawing. The same CAS gate used by saves
            // ensures that only the still-current revision can commit a newly ready result.
            return await drafts.WithCurrentRevisionAsync(plan.DraftId, plan.DraftRevision, current =>
            {
                var final = MemorialCompositionModule.CreatePlan(current, plan.Purpose);
                return final.IsSuccess && final.Value.SourceManifestSha256 == plan.SourceManifestSha256 &&
                    final.Value.Assets.SequenceEqual(plan.Assets)
                    ? MemorialSta.RunAsync(() => Publish(plan, artwork.Value, ct))
                    : Task.FromResult(Integrity());
            }, ct);
        }
        catch (OperationCanceledException) { return Cancelled(); }
        catch (ExtentMismatchException) { return Integrity(); }
        catch (Exception ex) when (FileMemorialRenderStore.IsBoundaryFailure(ex))
        {
            return ct.IsCancellationRequested ? Cancelled() : OperationResult.Fail<MemorialRenderResult>(
                FailureCode.OutputUnreadable, $"Memorial artwork could not be drawn: {ex.Message}");
        }
        finally
        {
            try { foreach (var input in opened.Values) await input.DisposeAsync(); }
            finally { if (entered) Serial.Release(); }
        }
    }

    private OperationResult<BitmapSource> Compose(MemorialRenderPlan plan, MemorialRasterTarget target,
        IReadOnlyDictionary<Guid, VerifiedAiAttachmentRead> sources, HashSet<Guid> preserved, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var layout = plan.Layout;
        var bitmap = new RenderTargetBitmap(target.Width, target.Height, target.Dpi, target.Dpi, PixelFormats.Pbgra32);
        var canvas = new Rect(0, 0, layout.Canvas.WidthMm, layout.Canvas.HeightMm);
        Geometry clip = layout.Canvas.OuterDiameterMm is { } outer
            ? new EllipseGeometry(new Point(outer / 2, outer / 2), outer / 2, outer / 2) : new RectangleGeometry(canvas);
        clip.Freeze();
        var scale = new ScaleTransform(96 / 25.4, 96 / 25.4); scale.Freeze();
        foreach (var layer in layout.Images.OrderBy(x => x.Role == MemorialLayerRole.Background ? 0 :
            x.Role is MemorialLayerRole.Person or MemorialLayerRole.OriginalDecor || preserved.Contains(x.AssetId) ? 2 : 1).ThenBy(x => x.ZOrder))
        {
            ct.ThrowIfCancellationRequested();
            RenderImage(bitmap, layer, sources[layer.AssetId].Stream, clip, scale, target.Dpi);
        }

        foreach (var line in layout.Text)
        {
            ct.ThrowIfCancellationRequested();
            var text = textLayout.Prepare(line);
            if (text.IsFailure) return OperationResult.Fail<BitmapSource>(text.Failure);
            var visual = new DrawingVisual();
            using (var draw = visual.RenderOpen())
            {
                draw.PushTransform(scale); draw.PushClip(clip);
                draw.DrawGeometry(Brushes.Black, null, text.Value.Ink);
                draw.Pop(); draw.Pop();
            }
            bitmap.Render(visual);
        }

        BitmapSource image = target.CircleRadiusPixels is null ? bitmap : EnforceCircle(bitmap, target, ct);
        if (!image.IsFrozen) image.Freeze();
        return OperationResult.Ok(image);
    }

    private OperationResult<MemorialRenderResult> Publish(MemorialRenderPlan plan, BitmapSource image, CancellationToken ct) =>
        renders.Publish(plan, output =>
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            encoder.Save(output);
        }, ct);

    private static void RenderImage(RenderTargetBitmap target, MemorialImageLayer layer, Stream stream,
        Geometry clip, ScaleTransform scale, int dpi)
    {
        var bitmap = DecodeLayer(layer, stream, dpi);
        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var draw = visual.RenderOpen())
        {
            draw.PushTransform(scale); draw.PushClip(clip);
            draw.DrawImage(bitmap, new Rect(layer.Bounds.X, layer.Bounds.Y, layer.Bounds.Width, layer.Bounds.Height));
            draw.Pop(); draw.Pop();
        }
        target.Render(visual); // Composite one visual; don't retain a scene with every decoded source.
    }

    private static BitmapSource DecodeLayer(MemorialImageLayer layer, Stream input, int dpi)
    {
        input.Position = 0;
        var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
        if (decoder.Frames.Count != 1) throw new IOException("A memorial layer requires a single image frame.");
        BitmapSource frame = decoder.Frames[0];
        int width = frame.PixelWidth, height = frame.PixelHeight;
        if (layer.SourceExtent != new RectPx(0, 0, width, height)) throw new ExtentMismatchException();
        if (width <= 0 || height <= 0 || (long)width * height > MemorialRasterTarget.MaximumPixels)
            throw new IOException("Memorial source image exceeds the decode limit.");
        if (layer.SourceCrop is { } crop)
            frame = new CroppedBitmap(frame, new Int32Rect(crop.X, crop.Y, crop.Width, crop.Height));
        int targetWidth = Math.Min(frame.PixelWidth, (int)Math.Ceiling(layer.Bounds.Width / 25.4 * dpi));
        int targetHeight = Math.Min(frame.PixelHeight, (int)Math.Ceiling(layer.Bounds.Height / 25.4 * dpi));
        if (targetWidth < frame.PixelWidth || targetHeight < frame.PixelHeight)
        {
            double factor = Math.Max(1.0 / Math.Min(frame.PixelWidth, frame.PixelHeight),
                Math.Min((double)targetWidth / frame.PixelWidth, (double)targetHeight / frame.PixelHeight));
            frame = new TransformedBitmap(frame, new ScaleTransform(factor, factor));
        }
        return frame;
    }

    private static BitmapSource EnforceCircle(BitmapSource bitmap, MemorialRasterTarget target, CancellationToken ct)
    {
        var clipped = new WriteableBitmap(bitmap);
        int stride = checked(target.Width * 4);
        byte[] zero = new byte[stride];
        for (int y = 0; y < target.Height; y++)
        {
            ct.ThrowIfCancellationRequested();
            var (start, end) = target.CircleSpan(y);
            if (start > 0) clipped.WritePixels(new Int32Rect(0, y, start, 1), zero, stride, 0);
            if (end < target.Width) clipped.WritePixels(new Int32Rect(end, y, target.Width - end, 1), zero, stride, 0);
        }
        clipped.Freeze(); return clipped;
    }

    private sealed class ExtentMismatchException : IOException { }
    private static OperationResult<MemorialRenderResult> Integrity() => OperationResult.Fail<MemorialRenderResult>(
        FailureCode.RevisionIntegrityMismatch, "Memorial render source, layout or revision does not match the verified draft.");
    private static OperationResult<MemorialRenderResult> Cancelled() => OperationResult.Fail<MemorialRenderResult>(
        FailureCode.Cancelled, "Memorial composition cancelled.");
}
