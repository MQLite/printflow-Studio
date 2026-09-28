using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using PrintFlow.App.ViewModels;

namespace PrintFlow.App.Views;

/// <summary>
/// Shows the proposed trim by cropping the pre-trim source's already decoded preview in memory
/// (SCRUM-11147).
/// </summary>
/// <remarks>
/// Display only: the same payload bytes the adjust surface draws, decoded the same way, cut to the
/// draft's payload rectangle. Nothing is encoded, written, hashed or sent. The last decode is kept
/// so dragging a handle re-cuts the image instead of decoding it again.
/// </remarks>
public sealed class ProposedTrimConverter : IMultiValueConverter
{
    private readonly PreviewPayloadConverter _decoder = new();
    private ReadOnlyMemory<byte> _decodedPayload;
    private BitmapSource? _decoded;

    public object? Convert(object?[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not ReadOnlyMemory<byte> payload || payload.IsEmpty ||
            values[1] is not PayloadRect rect)
        {
            return null;
        }

        if (_decoded is null || !_decodedPayload.Equals(payload))
        {
            _decoded = _decoder.Convert(payload, typeof(BitmapSource), null, culture) as BitmapSource;
            _decodedPayload = payload;
        }

        if (_decoded is null || rect.X < 0 || rect.Y < 0 || rect.Width <= 0 || rect.Height <= 0 ||
            rect.X + rect.Width > _decoded.PixelWidth || rect.Y + rect.Height > _decoded.PixelHeight)
        {
            return null;
        }

        CroppedBitmap cropped = new(_decoded, new Int32Rect(rect.X, rect.Y, rect.Width, rect.Height));
        cropped.Freeze();
        return cropped;
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("A proposed trim is displayed, never edited.");
}
