using PrintFlow.Domain.Trimming;

namespace PrintFlow.Tests.Unit.Trimming;

public sealed class NoiseTolerantAlphaBoundsTests
{
    [Fact]
    public void A_visible_isolated_dot_is_preserved_even_beside_a_large_subject()
    {
        byte[] alpha = Canvas(40, 40, (x, y) => x is >= 10 and < 30 && y is >= 10 and < 30 ? (byte)200 : (byte)0);
        alpha[0] = 2;
        NoiseTolerantAlphaBounds.Compute(40, 40, alpha).ShouldBe(TrimBounds.FromEdges(0, 0, 30, 30));
    }

    [Fact]
    public void Diagonally_connected_faint_hair_is_retained_to_its_last_pixel()
    {
        byte[] alpha = Canvas(40, 40, (x, y) => x is >= 10 and < 30 && y is >= 10 and < 30 ? (byte)200 : (byte)0);
        // Four pixels are small enough to be discarded unless diagonal attachment is detected.
        for (int i = 6; i < 10; i++) alpha[i * 40 + i] = 1;
        NoiseTolerantAlphaBounds.Compute(40, 40, alpha).ShouldBe(TrimBounds.FromEdges(6, 6, 30, 30));
    }

    [Fact]
    public void Entirely_faint_artwork_keeps_its_exact_extent()
    {
        byte[] alpha = Canvas(12, 10, (x, y) => x == 1 && y == 1 || x == 10 && y == 8 ? (byte)1 : (byte)0);
        NoiseTolerantAlphaBounds.Compute(12, 10, alpha).ShouldBe(TrimBounds.FromEdges(1, 1, 11, 9));
    }

    [Theory]
    [InlineData(4, 10)]
    [InlineData(5, 0)]
    public void A_large_faint_component_is_not_assumed_to_be_a_speck(int faintArea, int expectedLeft)
    {
        // 400 stronger pixels: the cutoff is four. The fifth faint pixel protects the region.
        byte[] alpha = Canvas(40, 40, (x, y) => x is >= 10 and < 30 && y is >= 10 and < 30 ? (byte)200 : (byte)0);
        for (int i = 0; i < faintArea; i++) alpha[i] = 1;
        NoiseTolerantAlphaBounds.Compute(40, 40, alpha).ShouldBe(
            TrimBounds.FromEdges(expectedLeft, expectedLeft == 0 ? 0 : 10, 30, 30));
    }

    [Fact]
    public void Transparent_pixels_do_not_connect_detached_residue_to_foreground()
    {
        byte[] alpha = Canvas(40, 40, (x, y) => x is >= 10 and < 30 && y is >= 10 and < 30 ? (byte)200 : (byte)0);
        alpha[8 * 40 + 8] = 1;
        NoiseTolerantAlphaBounds.Compute(40, 40, alpha).ShouldBe(TrimBounds.FromEdges(10, 10, 30, 30));
    }

    [Fact]
    public void An_all_transparent_canvas_has_no_detected_subject() =>
        NoiseTolerantAlphaBounds.Compute(5, 4, new byte[20]).ShouldBeNull();

    [Fact]
    public void Cancellation_refuses_to_produce_bounds()
    {
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();
        Should.Throw<OperationCanceledException>(() =>
            NoiseTolerantAlphaBounds.Compute(5, 4, new byte[20], cancelled.Token));
    }

    private static byte[] Canvas(int width, int height, Func<int, int, byte> pixel)
    {
        byte[] alpha = new byte[width * height];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++) alpha[y * width + x] = pixel(x, y);
        return alpha;
    }
}
