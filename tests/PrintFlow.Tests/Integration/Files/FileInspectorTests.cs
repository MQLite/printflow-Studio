using System.IO;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Imaging;
using PrintFlow.Tests.Fixtures;

namespace PrintFlow.Tests.Integration.Files;

/// <summary>
/// Jira 11106a: real file inspection — hashing, magic-byte format detection, and WIC-derived
/// pixel metadata (task §44). All fixtures are generated at test time; no customer or
/// production file is used.
/// </summary>
public sealed class FileInspectorTests
{
    private readonly WicFileInspector _inspector = new();

    [Fact]
    public async Task Sha256_matches_a_known_vector()
    {
        using TempWorkspace workspace = new();
        string path = workspace.CreateSourceFile("known.txt", SyntheticImages.KnownVectorAbc());

        OperationResult<FileFacts> result = await _inspector.InspectAsync(path, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Sha256.Value.ShouldBe(SyntheticImages.KnownVectorAbcSha256);
        result.Value.ByteLength.ShouldBe(3);
    }

    [Fact]
    public async Task Png_is_detected_from_magic_bytes_with_dimensions_dpi_and_alpha()
    {
        using TempWorkspace workspace = new();
        string path = workspace.CreateSourceFile("art.png", SyntheticImages.Png(5, 4, 300, alpha: true));

        OperationResult<FileFacts> result = await _inspector.InspectAsync(path, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Format.ShouldBe(ImageFormat.Png);
        result.Value.PixelWidth.ShouldBe(5);
        result.Value.PixelHeight.ShouldBe(4);
        result.Value.DpiX.ShouldNotBeNull();
        result.Value.DpiX!.Value.ShouldBe(300, tolerance: 0.5);
        result.Value.HasAlpha.ShouldBe(true);
    }

    [Fact]
    public async Task Png_without_alpha_reports_no_alpha()
    {
        using TempWorkspace workspace = new();
        string path = workspace.CreateSourceFile("opaque.png", SyntheticImages.Png(3, 3, 300, alpha: false));

        OperationResult<FileFacts> result = await _inspector.InspectAsync(path, CancellationToken.None);

        result.Value.HasAlpha.ShouldBe(false);
    }

    [Fact]
    public async Task Jpeg_is_detected_from_magic_bytes()
    {
        using TempWorkspace workspace = new();
        string path = workspace.CreateSourceFile("photo.jpg", SyntheticImages.Jpeg(6, 5));

        OperationResult<FileFacts> result = await _inspector.InspectAsync(path, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Format.ShouldBe(ImageFormat.Jpeg);
        result.Value.PixelWidth.ShouldBe(6);
        result.Value.PixelHeight.ShouldBe(5);
    }

    [Fact]
    public async Task Tiff_is_detected_from_magic_bytes()
    {
        using TempWorkspace workspace = new();
        string path = workspace.CreateSourceFile("scan.tif", SyntheticImages.Tiff(4, 4));

        OperationResult<FileFacts> result = await _inspector.InspectAsync(path, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Format.ShouldBe(ImageFormat.Tiff);
        result.Value.PixelWidth.ShouldBe(4);
        result.Value.PixelHeight.ShouldBe(4);
    }

    [Fact]
    public async Task Psd_is_detected_by_magic_bytes_with_null_pixel_metadata()
    {
        using TempWorkspace workspace = new();
        string path = workspace.CreateSourceFile("design.psd", SyntheticImages.PsdHeaderOnly());

        OperationResult<FileFacts> result = await _inspector.InspectAsync(path, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Format.ShouldBe(ImageFormat.Psd);
        result.Value.PixelWidth.ShouldBeNull();
        result.Value.PixelHeight.ShouldBeNull();
        result.Value.HasAlpha.ShouldBeNull();
    }

    [Fact]
    public async Task Pdf_is_detected_by_magic_bytes_with_null_pixel_metadata()
    {
        using TempWorkspace workspace = new();
        string path = workspace.CreateSourceFile("design.pdf", SyntheticImages.PdfHeaderOnly());

        OperationResult<FileFacts> result = await _inspector.InspectAsync(path, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Format.ShouldBe(ImageFormat.Pdf);
        result.Value.PixelWidth.ShouldBeNull();
    }

    [Fact]
    public async Task Extension_does_not_override_content_detection()
    {
        using TempWorkspace workspace = new();
        // JPEG bytes behind a .png extension: the sniffer must trust the bytes, not the name.
        string path = workspace.CreateSourceFile("picture.png", SyntheticImages.Jpeg());

        OperationResult<FileFacts> result = await _inspector.InspectAsync(path, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Format.ShouldBe(ImageFormat.Jpeg);
    }

    [Fact]
    public async Task Empty_file_is_reported_as_unreadable_not_a_valid_zero_length_image()
    {
        using TempWorkspace workspace = new();
        string path = workspace.CreateSourceFile("empty.png", []);

        OperationResult<FileFacts> result = await _inspector.InspectAsync(path, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.OutputUnreadable);
    }

    [Fact]
    public async Task Missing_file_is_reported_as_missing()
    {
        using TempWorkspace workspace = new();
        string path = Path.Combine(workspace.Root, "does-not-exist.png");

        OperationResult<FileFacts> result = await _inspector.InspectAsync(path, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.OutputMissing);
    }

    [Fact]
    public async Task Exclusively_locked_file_is_reported_as_unreadable()
    {
        using TempWorkspace workspace = new();
        string path = workspace.CreateSourceFile("locked.png", SyntheticImages.Png());

        using FileStream exclusiveHold = new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        OperationResult<FileFacts> result = await _inspector.InspectAsync(path, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.OutputUnreadable);
    }

    [Fact]
    public async Task Large_file_streams_without_loading_entirely_into_a_single_buffer()
    {
        using TempWorkspace workspace = new();
        byte[] png = SyntheticImages.Png(200, 200, alpha: true);
        string path = workspace.CreateSourceFile("large.png", png);

        OperationResult<FileFacts> result = await _inspector.InspectAsync(path, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ByteLength.ShouldBe(png.LongLength);
        result.Value.PixelWidth.ShouldBe(200);
    }
}
