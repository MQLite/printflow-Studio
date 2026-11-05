using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PrintFlow.Domain.AiDesign;
using PrintFlow.Domain.AiDesign.Authoring;
using PrintFlow.Domain.AiDesign.Memorial;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.AiDesign;
using PrintFlow.Infrastructure.AiDesign.Authoring;
using PrintFlow.Infrastructure.AiDesign.Memorial;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.AiDesign;
using PrintFlow.Workflow.AiDesign.Authoring;
using PrintFlow.Workflow.AiDesign.Memorial;

namespace PrintFlow.Tests.Integration.AiDesign.Memorial;

public sealed class MemorialRendererTests
{
    [Fact]
    public async Task Original_person_and_medal_are_drawn_from_verified_source()
    {
        using var fixture = new MemorialRenderFixture();
        var draft = await fixture.CreateAsync();
        var before = SHA256.HashData(File.ReadAllBytes(fixture.SnapshotPath(draft.Assets[0])));

        var render = await fixture.RenderAsync(draft);
        var bitmap = await fixture.ReadPngAsync(render);

        MemorialRenderFixture.Pixel(bitmap, 80, 94).ShouldBe(Colors.OrangeRed);
        MemorialRenderFixture.Pixel(bitmap, 100, 100).ShouldBe(Colors.Gold);
        MemorialRenderFixture.Pixel(bitmap, 148, 100).ShouldBe(Colors.LimeGreen);
        SHA256.HashData(File.ReadAllBytes(fixture.SnapshotPath(draft.Assets[0]))).ShouldBe(before);
    }

    [Fact]
    public async Task Background_cannot_cover_preserved_layers()
    {
        using var fixture = new MemorialRenderFixture();
        var draft = await fixture.CreateAsync(background: true);
        draft.Layout!.Images.Single(x => x.Role == MemorialLayerRole.Background).ZOrder.ShouldBe(int.MaxValue);

        var bitmap = await fixture.ReadPngAsync(await fixture.RenderAsync(draft));

        MemorialRenderFixture.Pixel(bitmap, 60, 95).ShouldBe(Colors.DodgerBlue);
        MemorialRenderFixture.Pixel(bitmap, 100, 100).ShouldBe(Colors.Gold);
        MemorialRenderFixture.Pixel(bitmap, 148, 100).ShouldBe(Colors.LimeGreen);
    }

    [Fact]
    public async Task Badge_outside_circle_is_really_alpha_zero()
    {
        using var fixture = new MemorialRenderFixture();
        var draft = await fixture.CreateAsync(background: true);

        var bitmap = await fixture.ReadPngAsync(await fixture.RenderAsync(draft));

        bitmap.PixelWidth.ShouldBe(189);
        bitmap.PixelHeight.ShouldBe(189);
        MemorialRenderFixture.Pixel(bitmap, 0, 0).A.ShouldBe((byte)0);
        MemorialRenderFixture.Pixel(bitmap, 0, 80).A.ShouldBe((byte)0);
        MemorialRenderFixture.Pixel(bitmap, 1, 94).ShouldBe(Colors.DodgerBlue);
    }

    [Fact]
    public async Task Tshirt_keeps_rectangular_artwork_and_transparent_gaps()
    {
        using var fixture = new MemorialRenderFixture();
        var draft = await fixture.CreateAsync(badge: false);

        var bitmap = await fixture.ReadPngAsync(await fixture.RenderAsync(draft));

        bitmap.PixelWidth.ShouldBe(189);
        bitmap.PixelHeight.ShouldBe(171);
        MemorialRenderFixture.Pixel(bitmap, 1, 1).ShouldBe(Colors.LimeGreen);
        MemorialRenderFixture.Pixel(bitmap, 15, 15).A.ShouldBe((byte)0);
    }

    [Fact]
    public async Task Overlay_guides_and_checkerboard_never_enter_png()
    {
        using var fixture = new MemorialRenderFixture();
        var draft = await fixture.CreateAsync();

        var bitmap = await fixture.ReadPngAsync(await fixture.RenderAsync(draft));

        for (int x = 20; x < 60; x++)
            MemorialRenderFixture.Pixel(bitmap, x, 94).A.ShouldBe((byte)0);
        int visibleText = 0;
        for (int y = 46; y < 72; y++)
            for (int x = 65; x < 125; x++)
                if (MemorialRenderFixture.Pixel(bitmap, x, y).A != 0) visibleText++;
        visibleText.ShouldBeGreaterThan(20);
    }

    [Fact]
    public async Task Source_swap_or_cancel_does_not_publish_ready()
    {
        using var fixture = new MemorialRenderFixture();
        var draft = await fixture.CreateAsync();
        var asset = draft.Assets[0];
        var snapshot = fixture.SnapshotPath(asset);
        byte[] original = File.ReadAllBytes(snapshot);
        File.SetAttributes(snapshot, FileAttributes.Normal);
        byte[] substitution = original.ToArray();
        substitution[^1] ^= 1;
        File.WriteAllBytes(snapshot, substitution);

        (await fixture.Compose.RenderAsync(draft.Id, draft.Revision, MemorialRenderPurpose.Preview, default))
            .Failure.Code.ShouldBe(FailureCode.RevisionIntegrityMismatch);
        File.ReadAllBytes(snapshot).ShouldBe(substitution);
        File.WriteAllBytes(snapshot, original);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        (await fixture.Compose.RenderAsync(draft.Id, draft.Revision, MemorialRenderPurpose.Preview, cancelled.Token))
            .Failure.Code.ShouldBe(FailureCode.Cancelled);
        (await fixture.Renders.ListAsync(draft.Id, default)).Value.ShouldBeEmpty();
        File.ReadAllBytes(snapshot).ShouldBe(original);
    }

    [Fact]
    public async Task Stale_revision_and_rehashed_unconfirmed_layout_are_refused()
    {
        using var fixture = new MemorialRenderFixture();
        var draft = await fixture.CreateAsync();
        var old = MemorialCompositionModule.CreatePlan(draft, MemorialRenderPurpose.Preview).Value;
        var moved = draft.Layout! with
        {
            Images = draft.Layout.Images.Select(layer => layer.Role == MemorialLayerRole.Person
                ? layer with { Bounds = layer.Bounds with { X = 19 } } : layer).ToArray()
        };
        var forged = MemorialCompositionModule.CreatePlan(draft with { Layout = moved }, MemorialRenderPurpose.Preview).Value;
        forged.SourceManifestSha256.ShouldNotBe(old.SourceManifestSha256);

        (await fixture.Renderer.RenderAsync(forged, default)).Failure.Code.ShouldBe(FailureCode.RevisionIntegrityMismatch);
        (await fixture.Compose.RenderAsync(draft.Id, draft.Revision + 1, MemorialRenderPurpose.Preview, default))
            .Failure.Code.ShouldBe(FailureCode.RevisionIntegrityMismatch);
        (await fixture.Renders.ListAsync(draft.Id, default)).Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task Actual_decode_dimensions_and_target_limit_are_enforced()
    {
        using var fixture = new MemorialRenderFixture();
        var draft = await fixture.CreateAsync();
        var layout = draft.Layout! with
        {
            Images = draft.Layout.Images.Select(layer => layer.Role == MemorialLayerRole.Person
                ? layer with { SourceExtent = new(0, 0, 5, 5) } : layer).ToArray()
        };
        var wrongExtent = (await fixture.Drafts.SaveAsync(draft with { Layout = layout }, draft.Revision, default)).Value;
        (await fixture.Compose.RenderAsync(draft.Id, wrongExtent.Revision, MemorialRenderPurpose.Preview, default))
            .Failure.Code.ShouldBe(FailureCode.RevisionIntegrityMismatch);

        RectMm CenterOnLargeCanvas(RectMm b) => b with { X = b.X + 2475, Y = b.Y + 2475 };
        var huge = draft.Layout with
        {
            Canvas = new(5000, 5000, 5000, 5000),
            Images = draft.Layout.Images.Select(x => x with { Bounds = CenterOnLargeCanvas(x.Bounds) }).ToArray(),
            Text = draft.Layout.Text.Select(x => x with { Bounds = CenterOnLargeCanvas(x.Bounds) }).ToArray()
        };
        var hugeDraft = (await fixture.Drafts.SaveAsync(wrongExtent with { Layout = huge }, wrongExtent.Revision, default)).Value;
        var tooLarge = await fixture.Compose.RenderAsync(draft.Id, hugeDraft.Revision, MemorialRenderPurpose.DesignMaterial, default);
        tooLarge.Failure.Code.ShouldBe(FailureCode.PreconditionNotMet);
        tooLarge.Failure.TechnicalDetail.ShouldContain("64 million");
        (await fixture.Renders.ListAsync(draft.Id, default)).Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task Revision_that_changes_before_publication_cannot_publish()
    {
        using var fixture = new MemorialRenderFixture();
        var draft = await fixture.CreateAsync();
        var drafts = new ChangeAtPublication(fixture.Drafts);
        var renderer = new WpfMemorialRenderer(new VerifiedAiAttachmentReader(fixture.Root), new WpfMemorialTextLayout(), fixture.Renders, drafts);
        var compose = new MemorialCompositionModule(drafts, renderer);

        (await compose.RenderAsync(draft.Id, draft.Revision, MemorialRenderPurpose.Preview, default))
            .IsFailure.ShouldBeTrue();
        (await fixture.Drafts.ReadAsync(draft.Id, default)).Value.Revision.ShouldBe(draft.Revision + 1);
        (await fixture.Renders.ListAsync(draft.Id, default)).Value.ShouldBeEmpty();
    }

    private sealed class ChangeAtPublication(IAiAuthoringDraftStore inner) : IAiAuthoringDraftStore
    {
        public Task<OperationResult<AiAuthoringDraftListing>> ListAsync(CancellationToken ct) => inner.ListAsync(ct);
        public Task<OperationResult<AiAuthoringDraft>> ReadAsync(Guid id, CancellationToken ct) => inner.ReadAsync(id, ct);
        public Task<OperationResult<IReadOnlyList<AiAuthoringDraft>>> ReadHistoryAsync(Guid id, CancellationToken ct) => inner.ReadHistoryAsync(id, ct);
        public Task<OperationResult<AiAuthoringDraft>> SaveAsync(AiAuthoringDraft value, long? revision, CancellationToken ct) => inner.SaveAsync(value, revision, ct);
        public async Task<OperationResult<T>> WithCurrentRevisionAsync<T>(Guid id, long revision,
            Func<AiAuthoringDraft, Task<OperationResult<T>>> publish, CancellationToken ct)
        {
            var current = (await inner.ReadAsync(id, ct)).Value;
            (await inner.SaveAsync(current with { Mode = AiAuthoringMode.Quick }, revision, ct)).IsSuccess.ShouldBeTrue();
            return await inner.WithCurrentRevisionAsync(id, revision, publish, ct);
        }
    }
}

internal sealed class MemorialRenderFixture : IDisposable
{
    private readonly TempWorkspace _workspace = new();
    private readonly TempWorkspace _source = new();
    public string Root => _workspace.Root;
    public FileAiAuthoringDraftStore Drafts { get; }
    public FileMemorialRenderStore Renders { get; }
    public WpfMemorialRenderer Renderer { get; }
    public MemorialCompositionModule Compose { get; }

    public MemorialRenderFixture()
    {
        Drafts = new(Root); Renders = new(Root);
        Renderer = new(new VerifiedAiAttachmentReader(Root), new WpfMemorialTextLayout(), Renders, Drafts);
        Compose = new(Drafts, Renderer);
    }

    public async Task<AiAuthoringDraft> CreateAsync(bool background = false, bool badge = true)
    {
        Guid id = Guid.NewGuid();
        string person = _source.CreateSourceFile("person.png", Png(4, (x, y) => x >= 2 && y >= 2 ? Colors.Gold : Colors.OrangeRed));
        string decor = _source.CreateSourceFile("decor.png", Png(4, (_, _) => Colors.LimeGreen));
        string backdrop = _source.CreateSourceFile("background.png", Png(4, (_, _) => Colors.DodgerBlue));
        var attachments = new List<SelectedAiDesignFile> { new(person, "person"), new(decor, "decor") };
        if (background) attachments.Add(new(backdrop, "background"));
        var assets = (await new FileAiDesignAttachments(Root).SnapshotAsync(id, attachments, [], default)).Value;
        List<AiTemplateField> definitions =
        [
            new("literal", AiTemplateFieldKind.Text, true, Semantic: AiTemplateSemantic.Name),
            new("photo", AiTemplateFieldKind.ImageReference, true, SlotId: "person", Semantic: AiTemplateSemantic.PersonImage),
            new("decoration", AiTemplateFieldKind.ImageReference, true, SlotId: "decor")
        ];
        if (background) definitions.Add(new("backdrop", AiTemplateFieldKind.ImageReference, true, SlotId: "background"));
        var template = new AiTemplateContent("Memorial fixture", badge ? AiProductKind.MemorialBadge : AiProductKind.MemorialTshirt,
            background ? "{{literal}} {{photo}} {{decoration}} {{backdrop}}" : "{{literal}} {{photo}} {{decoration}}", definitions);
        var fields = new Dictionary<string, string> { ["literal"] = "A" };
        var document = PromptDocumentComposer.Compose(template, fields).Document;
        var slots = definitions.Where(x => x.SlotId is not null).Select(x => new AiReferenceSlot(x.SlotId!, x.SlotId!, x.Required)).ToArray();
        List<MemorialImageLayer> images =
        [
            Layer(0, MemorialLayerRole.Person, new(18, 20, 10, 10), 5),
            Layer(1, MemorialLayerRole.OriginalDecor, badge ? new(38, 24, 3, 3) : new(0, 0, 3, 3), 10)
        ];
        if (background) images.Add(Layer(2, MemorialLayerRole.Background, badge ? new(0, 0, 50, 50) : new(0, 0, 45, 45), int.MaxValue));
        var draft = new AiAuthoringDraft
        {
            Id = id, Template = new("fixture", 1), TemplateSnapshot = template, Product = template.Product,
            Mode = AiAuthoringMode.Detailed, Fields = fields, InitialDocument = document, FinalDocument = document,
            Assets = assets, Slots = slots, Bindings = slots.Select((x, i) => new AiSlotBinding(x.SlotId, assets[i].Id)).ToArray(),
            Preservation = assets.Select((x, i) => new AiPreservationRule(x.Id, i < 2, true, true, false, i >= 2)).ToArray(),
            Layout = new(badge ? new(50, 50, 50, 40) : new(50, 45, null, null), images,
                [new("name", "A", "Arial", 3, new(18, 12, 14, 6), MemorialTextPlacement.Flat, null)], true)
        };
        var confirmations = assets.Select(x => AiAuthoringConfirmationRules.PreservationKey(x.Id)).Append("name")
            .ToDictionary(x => x, x => AiAuthoringConfirmationRules.ContentHash(draft, x)!);
        return (await Drafts.SaveAsync(draft with { Confirmations = confirmations }, null, default)).Value;

        MemorialImageLayer Layer(int index, MemorialLayerRole role, RectMm bounds, int z) =>
            new(assets[index].Id, role, bounds, null, z) { SourceExtent = new(0, 0, 4, 4) };
    }

    public async Task<MemorialRenderResult> RenderAsync(AiAuthoringDraft draft, MemorialRenderPurpose purpose = MemorialRenderPurpose.Preview)
    {
        var result = await Compose.RenderAsync(draft.Id, draft.Revision, purpose, default);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.TechnicalDetail : "");
        return result.Value;
    }

    public async Task<BitmapSource> ReadPngAsync(MemorialRenderResult result)
    {
        var opened = await Renders.OpenVerifiedAsync(result, default);
        opened.IsSuccess.ShouldBeTrue(opened.IsFailure ? opened.Failure.TechnicalDetail : "");
        await using var input = opened.Value;
        var frame = new PngBitmapDecoder(input, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames.Single();
        var bitmap = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        bitmap.Freeze(); return bitmap;
    }

    public string SnapshotPath(AiDesignAttachment asset) =>
        Path.Combine(Root, "AiDesign", "Requests", asset.Snapshot.RequestId.ToString("D"), asset.Snapshot.RelativePath);
    public string DraftRenderPath(Guid id) => Path.Combine(Root, "AiDesign", "Authoring", "Renders", id.ToString("D"));
    public string RenderPath(MemorialRenderResult render) => Path.Combine(DraftRenderPath(render.File.DraftId), render.File.RenderId.ToString("D"));

    public static Color Pixel(BitmapSource image, int x, int y)
    {
        byte[] pixel = new byte[4];
        image.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
    }

    public static byte[] Png(int size, Func<int, int, Color> colorAt)
    {
        byte[] pixels = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var color = colorAt(x, y); int p = (y * size + x) * 4;
                pixels[p] = color.B; pixels[p + 1] = color.G; pixels[p + 2] = color.R; pixels[p + 3] = color.A;
            }
        var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var bytes = new MemoryStream(); encoder.Save(bytes); return bytes.ToArray();
    }

    public void Dispose() { _source.Dispose(); _workspace.Dispose(); }
}
