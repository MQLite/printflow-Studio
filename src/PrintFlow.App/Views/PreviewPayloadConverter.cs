using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace PrintFlow.App.Views;

/// <summary>
/// Turns the preview seam's encoded bytes into something an <c>Image</c> can show
/// (Epic 11200 Part C1 §3, §29).
/// </summary>
/// <remarks>
/// This exists so that the one unavoidable stream in the preview path lives in the view layer
/// rather than in a view model. <c>PrintFlow.App.ViewModels</c> must contain no
/// <c>System.IO</c> at all — an architecture test asserts it — and a view model that
/// constructed a <c>MemoryStream</c> to build a bitmap would break that for a purely
/// presentational reason.
/// <para>
/// <see cref="BitmapCacheOption.OnLoad"/> plus <c>Freeze</c> means the bitmap owns its pixels
/// outright: the stream is finished with before this method returns, and the result can be
/// handed to any thread. The bytes are already a PNG the preview decoder produced and verified
/// by encoding it, so the failure path here is a guard rather than an expectation — and it
/// yields no image rather than an exception, because a preview that cannot be drawn must never
/// take a review screen down (§21).
/// </para>
/// </remarks>
public sealed class PreviewPayloadConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not ReadOnlyMemory<byte> payload || payload.IsEmpty)
        {
            return null;
        }

        try
        {
            using MemoryStream stream = new(payload.ToArray(), writable: false);

            BitmapImage bitmap = new();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();

            return bitmap;
        }
        catch (NotSupportedException)
        {
            return null;
        }
        catch (FileFormatException)
        {
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("A preview is displayed, never edited.");
}
