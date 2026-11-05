using System.Collections;

namespace PrintFlow.Domain.Trimming;

/// <summary>Conservative bounds excluding small, detached components visible only at alpha 1/255.</summary>
/// <remarks>
/// Eight-neighbour connectivity preserves diagonal hairs and every faint fringe touching a
/// stronger pixel. Components larger than one percent of the stronger foreground (at least
/// four pixels) are retained too. If the entire artwork is faint, its exact bounds survive.
/// This selects a canvas rectangle; it neither erases nor changes any retained pixel.
/// </remarks>
public static class NoiseTolerantAlphaBounds
{
    public static TrimBounds? Compute(
        int width, int height, ReadOnlySpan<byte> alpha, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TrimBounds? exact = AlphaBounds.Compute(width, height, alpha);
        if (exact is null) return null;

        int strongPixels = 0;
        bool hasFaint = false;
        int left = width, top = height, right = 0, bottom = 0;
        for (int y = 0; y < height; y++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (int x = 0; x < width; x++)
            {
                byte value = alpha[y * width + x];
                hasFaint |= value == 1;
                if (value <= 1) continue;
                strongPixels++;
                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x + 1);
                bottom = Math.Max(bottom, y + 1);
            }
        }

        if (strongPixels == 0 || !hasFaint) return exact;

        int maximumSpeckArea = Math.Max(4, strongPixels / 100);
        BitArray visited = new(alpha.Length);
        Stack<int> pending = new();
        for (int start = 0; start < alpha.Length; start++)
        {
            if ((start & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            if (alpha[start] != 1 || visited[start]) continue;

            int area = 0;
            int componentLeft = width, componentTop = height, componentRight = 0, componentBottom = 0;
            bool touchesForeground = false;
            visited[start] = true;
            pending.Push(start);
            while (pending.TryPop(out int index))
            {
                if ((area & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                area++;
                int x = index % width, y = index / width;
                componentLeft = Math.Min(componentLeft, x);
                componentTop = Math.Min(componentTop, y);
                componentRight = Math.Max(componentRight, x + 1);
                componentBottom = Math.Max(componentBottom, y + 1);
                for (int ny = Math.Max(0, y - 1); ny <= Math.Min(height - 1, y + 1); ny++)
                for (int nx = Math.Max(0, x - 1); nx <= Math.Min(width - 1, x + 1); nx++)
                {
                    int neighbour = ny * width + nx;
                    if (alpha[neighbour] > 1) touchesForeground = true;
                    else if (alpha[neighbour] == 1 && !visited[neighbour])
                    {
                        visited[neighbour] = true;
                        pending.Push(neighbour);
                    }
                }
            }

            if (!touchesForeground && area <= maximumSpeckArea) continue;
            left = Math.Min(left, componentLeft);
            top = Math.Min(top, componentTop);
            right = Math.Max(right, componentRight);
            bottom = Math.Max(bottom, componentBottom);
        }

        return TrimBounds.FromEdges(left, top, right, bottom);
    }
}
