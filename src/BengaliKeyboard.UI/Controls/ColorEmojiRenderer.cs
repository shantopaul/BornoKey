using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BengaliKeyboard.UI.Controls;

/// <summary>
/// Hardware-accelerated full-color emoji &amp; country flag renderer using native Windows Direct2D + DirectWrite
/// (<c>D2D1_DRAW_TEXT_OPTIONS_ENABLE_COLOR_FONT</c>) so multi-layer COLR/CPAL glyphs in <c>Segoe UI Emoji</c>
/// and Regional Indicator country flags (e.g., 🇧🇩, 🇺🇸, 🇬🇧, 🇮🇳) render in rich full color instead of WPF's monochrome outline.
/// </summary>
public static unsafe class ColorEmojiRenderer
{
    private const uint D2D1DrawTextOptionsClip = 0x00000002;
    private const uint D2D1DrawTextOptionsEnableColorFont = 0x00000004;
    private const int DefaultPixelSize = 64;

    private static readonly ConcurrentDictionary<string, BitmapSource> BitmapCache = new(StringComparer.Ordinal);
    private static readonly object RenderLock = new();

    private static readonly Guid IidID2D1Factory = new("06152247-6f50-465a-9245-118bfd3b6007");
    private static readonly Guid IidIDWriteFactory = new("b859ee5a-d838-4b5b-a2e8-1adc7d93db48");
    private static nint _sharedD2DFactory = nint.Zero;
    private static nint _sharedDWriteFactory = nint.Zero;

    public static BitmapSource? GetOrCreateEmojiBitmap(string? emoji, int pixelSize = DefaultPixelSize)
    {
        if (string.IsNullOrWhiteSpace(emoji))
        {
            return null;
        }

        var trimmed = emoji.Trim();
        var clampedSize = Math.Clamp(pixelSize, 24, 128);
        var cacheKey = $"{trimmed}_{clampedSize}";

        return BitmapCache.GetOrAdd(cacheKey, _ => RenderEmojiInternal(trimmed, clampedSize));
    }

    private static BitmapSource RenderEmojiInternal(string emoji, int size)
    {
        if (TryExtractRegionalIndicatorCode(emoji, out var countryCode))
        {
            return RenderCountryFlagBitmap(countryCode, size);
        }

        lock (RenderLock)
        {
            var d2dBitmap = TryRenderDirect2DColorGlyph(emoji, size);
            if (d2dBitmap is not null)
            {
                return d2dBitmap;
            }
        }

        return RenderFallbackWpfGlyph(emoji, size);
    }

    public static bool TryExtractRegionalIndicatorCode(string emoji, out string countryCode)
    {
        countryCode = string.Empty;
        if (string.IsNullOrEmpty(emoji))
        {
            return false;
        }

        var runes = emoji.EnumerateRunes().ToArray();
        if (runes.Length == 2 &&
            runes[0].Value is >= 0x1F1E6 and <= 0x1F1FF &&
            runes[1].Value is >= 0x1F1E6 and <= 0x1F1FF)
        {
            var c1 = (char)('A' + (runes[0].Value - 0x1F1E6));
            var c2 = (char)('A' + (runes[1].Value - 0x1F1E6));
            countryCode = new string([c1, c2]);
            return true;
        }

        return false;
    }

    private static BitmapSource? TryRenderDirect2DColorGlyph(string emoji, int size)
    {
        nint screenDc = nint.Zero;
        nint memDc = nint.Zero;
        nint dibBitmap = nint.Zero;
        nint oldGdiObj = nint.Zero;
        nint d2dFactory = nint.Zero;
        nint dcRenderTarget = nint.Zero;
        nint solidBrush = nint.Zero;
        nint dwriteFactory = nint.Zero;
        nint textFormat = nint.Zero;

        try
        {
            screenDc = GetDC(nint.Zero);
            memDc = CreateCompatibleDC(screenDc);
            if (memDc == nint.Zero)
            {
                return null;
            }

            var bmi = new BitmapInfoHeader
            {
                biSize = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                biWidth = size,
                biHeight = -size, // top-down DIB
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0 // BI_RGB
            };

            dibBitmap = CreateDIBSection(memDc, ref bmi, 0, out var dibBits, nint.Zero, 0);
            if (dibBitmap == nint.Zero || dibBits == nint.Zero)
            {
                return null;
            }

            oldGdiObj = SelectObject(memDc, dibBitmap);

            if (_sharedD2DFactory == nint.Zero)
            {
                var factoryGuid = IidID2D1Factory;
                _ = D2D1CreateFactory(0, ref factoryGuid, nint.Zero, out _sharedD2DFactory);
            }
            d2dFactory = _sharedD2DFactory;
            if (d2dFactory == nint.Zero)
            {
                return null;
            }

            if (_sharedDWriteFactory == nint.Zero)
            {
                var dwriteGuid = IidIDWriteFactory;
                _ = DWriteCreateFactory(0, ref dwriteGuid, out _sharedDWriteFactory);
            }
            dwriteFactory = _sharedDWriteFactory;
            if (dwriteFactory == nint.Zero)
            {
                return null;
            }

            // ID2D1Factory::CreateDCRenderTarget (vtable slot 16)
            var rtProps = new D2D1RenderTargetProperties
            {
                type = 0, // D2D1_RENDER_TARGET_TYPE_DEFAULT
                pixelFormat = new D2D1PixelFormat
                {
                    format = 87, // DXGI_FORMAT_B8G8R8A8_UNORM
                    alphaMode = 1 // D2D1_ALPHA_MODE_PREMULTIPLIED
                },
                dpiX = 96.0f,
                dpiY = 96.0f,
                usage = 0,
                minLevel = 0
            };

            var d2dVtbl = *(nint**)d2dFactory;
            var createDcRt = (delegate* unmanaged[Stdcall]<nint, D2D1RenderTargetProperties*, nint*, int>)d2dVtbl[16];
            var hr = createDcRt(d2dFactory, &rtProps, &dcRenderTarget);
            if (hr < 0 || dcRenderTarget == nint.Zero)
            {
                return null;
            }

            // ID2D1DCRenderTarget::BindDC (vtable slot 57)
            var rtVtbl = *(nint**)dcRenderTarget;
            var bindDc = (delegate* unmanaged[Stdcall]<nint, nint, Win32Rect*, int>)rtVtbl[57];
            var subRect = new Win32Rect { Left = 0, Top = 0, Right = size, Bottom = size };
            hr = bindDc(dcRenderTarget, memDc, &subRect);
            if (hr < 0)
            {
                return null;
            }

            // IDWriteFactory::CreateTextFormat (vtable slot 15)
            var dwVtbl = *(nint**)dwriteFactory;
            var createTextFormat = (delegate* unmanaged[Stdcall]<nint, char*, nint, int, int, int, float, char*, nint*, int>)dwVtbl[15];
            var fontSize = size * 0.72f;

            fixed (char* pFontFamily = "Segoe UI Emoji")
            fixed (char* pLocale = "en-us")
            {
                hr = createTextFormat(
                    dwriteFactory,
                    pFontFamily,
                    nint.Zero,
                    400, // DWRITE_FONT_WEIGHT_NORMAL
                    0,   // DWRITE_FONT_STYLE_NORMAL
                    5,   // DWRITE_FONT_STRETCH_NORMAL
                    fontSize,
                    pLocale,
                    &textFormat);
            }

            if (hr < 0 || textFormat == nint.Zero)
            {
                return null;
            }

            // IDWriteTextFormat::SetTextAlignment (slot 3 -> DWRITE_TEXT_ALIGNMENT_CENTER = 2)
            // IDWriteTextFormat::SetParagraphAlignment (slot 4 -> DWRITE_PARAGRAPH_ALIGNMENT_CENTER = 2)
            // IDWriteTextFormat::SetWordWrapping (slot 5 -> DWRITE_WORD_WRAPPING_NO_WRAP = 1)
            var tfVtbl = *(nint**)textFormat;
            var setTextAlign = (delegate* unmanaged[Stdcall]<nint, int, int>)tfVtbl[3];
            var setParaAlign = (delegate* unmanaged[Stdcall]<nint, int, int>)tfVtbl[4];
            var setWordWrap = (delegate* unmanaged[Stdcall]<nint, int, int>)tfVtbl[5];
            _ = setTextAlign(textFormat, 2);
            _ = setParaAlign(textFormat, 2);
            _ = setWordWrap(textFormat, 1);

            // ID2D1RenderTarget::CreateSolidColorBrush (slot 8)
            var createBrush = (delegate* unmanaged[Stdcall]<nint, D2D1ColorF*, nint, nint*, int>)rtVtbl[8];
            var whiteColor = new D2D1ColorF { r = 0.95f, g = 0.96f, b = 0.97f, a = 1.0f };
            hr = createBrush(dcRenderTarget, &whiteColor, nint.Zero, &solidBrush);
            if (hr < 0 || solidBrush == nint.Zero)
            {
                return null;
            }

            // BeginDraw (slot 48), Clear (slot 47), DrawText (slot 27), EndDraw (slot 49)
            var beginDraw = (delegate* unmanaged[Stdcall]<nint, void>)rtVtbl[48];
            var clear = (delegate* unmanaged[Stdcall]<nint, D2D1ColorF*, void>)rtVtbl[47];
            var drawText = (delegate* unmanaged[Stdcall]<nint, char*, uint, nint, D2D1RectF*, nint, uint, int, void>)rtVtbl[27];
            var endDraw = (delegate* unmanaged[Stdcall]<nint, ulong*, ulong*, int>)rtVtbl[49];

            beginDraw(dcRenderTarget);
            var clearColor = new D2D1ColorF { r = 0f, g = 0f, b = 0f, a = 0f };
            clear(dcRenderTarget, &clearColor);

            var layoutRect = new D2D1RectF
            {
                left = 0f,
                top = 0f,
                right = size,
                bottom = size
            };

            fixed (char* pText = emoji)
            {
                drawText(
                    dcRenderTarget,
                    pText,
                    (uint)emoji.Length,
                    textFormat,
                    &layoutRect,
                    solidBrush,
                    D2D1DrawTextOptionsEnableColorFont | D2D1DrawTextOptionsClip,
                    0);
            }

            ulong tag1 = 0, tag2 = 0;
            hr = endDraw(dcRenderTarget, &tag1, &tag2);
            if (hr < 0)
            {
                return null;
            }

            GdiFlush();

            var stride = size * 4;
            var bitmap = BitmapSource.Create(
                size,
                size,
                96.0,
                96.0,
                PixelFormats.Pbgra32,
                null,
                dibBits,
                stride * size,
                stride);

            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
        finally
        {
            ReleaseComObject(textFormat);
            ReleaseComObject(solidBrush);
            ReleaseComObject(dcRenderTarget);

            if (memDc != nint.Zero && oldGdiObj != nint.Zero)
            {
                _ = SelectObject(memDc, oldGdiObj);
            }
            if (dibBitmap != nint.Zero)
            {
                _ = DeleteObject(dibBitmap);
            }
            if (memDc != nint.Zero)
            {
                _ = DeleteDC(memDc);
            }
            if (screenDc != nint.Zero)
            {
                _ = ReleaseDC(nint.Zero, screenDc);
            }
        }
    }

    private static BitmapSource RenderCountryFlagBitmap(string countryCode, int size)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var padX = size * 0.08;
            var padY = size * 0.18;
            var w = size - (padX * 2);
            var h = size - (padY * 2);
            var flagRect = new Rect(padX, padY, w, h);
            var radius = size * 0.08;

            var clipGeo = new RectangleGeometry(flagRect, radius, radius);
            dc.PushClip(clipGeo);

            switch (countryCode)
            {
                case "BD": // Bangladesh 🇧🇩
                    dc.DrawRectangle(BrushFromHex("#006A4E"), null, flagRect);
                    dc.DrawEllipse(
                        BrushFromHex("#F42A41"),
                        null,
                        new Point(padX + (w * 0.45), padY + (h * 0.50)),
                        h * 0.30,
                        h * 0.30);
                    break;

                case "IN": // India 🇮🇳
                    dc.DrawRectangle(BrushFromHex("#FF9933"), null, new Rect(padX, padY, w, h / 3.0));
                    dc.DrawRectangle(BrushFromHex("#FFFFFF"), null, new Rect(padX, padY + (h / 3.0), w, h / 3.0));
                    dc.DrawRectangle(BrushFromHex("#138808"), null, new Rect(padX, padY + (2.0 * h / 3.0), w, h / 3.0));
                    dc.DrawEllipse(
                        null,
                        new Pen(BrushFromHex("#000080"), Math.Max(1.5, size * 0.03)),
                        new Point(padX + (w * 0.50), padY + (h * 0.50)),
                        h * 0.13,
                        h * 0.13);
                    break;

                case "US": // United States 🇺🇸
                    dc.DrawRectangle(BrushFromHex("#FFFFFF"), null, flagRect);
                    var stripeH = h / 7.0;
                    var redBrush = BrushFromHex("#B22234");
                    for (var i = 0; i < 7; i += 2)
                    {
                        dc.DrawRectangle(redBrush, null, new Rect(padX, padY + (i * stripeH), w, stripeH));
                    }
                    dc.DrawRectangle(BrushFromHex("#3C3B6E"), null, new Rect(padX, padY, w * 0.45, h * 0.54));
                    break;

                case "GB": // United Kingdom 🇬🇧
                    dc.DrawRectangle(BrushFromHex("#012169"), null, flagRect);
                    var whitePen = new Pen(BrushFromHex("#FFFFFF"), Math.Max(3.0, h * 0.22));
                    var redPen = new Pen(BrushFromHex("#C8102E"), Math.Max(1.8, h * 0.12));
                    dc.DrawLine(whitePen, new Point(padX, padY), new Point(padX + w, padY + h));
                    dc.DrawLine(whitePen, new Point(padX + w, padY), new Point(padX, padY + h));
                    dc.DrawLine(redPen, new Point(padX, padY), new Point(padX + w, padY + h));
                    dc.DrawLine(redPen, new Point(padX + w, padY), new Point(padX, padY + h));
                    dc.DrawRectangle(BrushFromHex("#FFFFFF"), null, new Rect(padX + (w * 0.40), padY, w * 0.20, h));
                    dc.DrawRectangle(BrushFromHex("#FFFFFF"), null, new Rect(padX, padY + (h * 0.36), w, h * 0.28));
                    dc.DrawRectangle(BrushFromHex("#C8102E"), null, new Rect(padX + (w * 0.44), padY, w * 0.12, h));
                    dc.DrawRectangle(BrushFromHex("#C8102E"), null, new Rect(padX, padY + (h * 0.42), w, h * 0.16));
                    break;

                case "SA": // Saudi Arabia 🇸🇦
                    dc.DrawRectangle(BrushFromHex("#006C35"), null, flagRect);
                    dc.DrawRectangle(BrushFromHex("#FFFFFF"), null, new Rect(padX + (w * 0.22), padY + (h * 0.34), w * 0.56, h * 0.16));
                    dc.DrawRectangle(BrushFromHex("#FFFFFF"), null, new Rect(padX + (w * 0.28), padY + (h * 0.62), w * 0.44, h * 0.08));
                    break;

                case "PS": // Palestine 🇵🇸
                    dc.DrawRectangle(BrushFromHex("#000000"), null, new Rect(padX, padY, w, h / 3.0));
                    dc.DrawRectangle(BrushFromHex("#FFFFFF"), null, new Rect(padX, padY + (h / 3.0), w, h / 3.0));
                    dc.DrawRectangle(BrushFromHex("#007A3D"), null, new Rect(padX, padY + (2.0 * h / 3.0), w, h / 3.0));
                    var tri = new StreamGeometry();
                    using (var ctx = tri.Open())
                    {
                        ctx.BeginFigure(new Point(padX, padY), isFilled: true, isClosed: true);
                        ctx.LineTo(new Point(padX + (w * 0.42), padY + (h * 0.50)), isStroked: false, isSmoothJoin: false);
                        ctx.LineTo(new Point(padX, padY + h), isStroked: false, isSmoothJoin: false);
                    }
                    dc.DrawGeometry(BrushFromHex("#CE1126"), null, tri);
                    break;

                case "CA": // Canada 🇨🇦
                    dc.DrawRectangle(BrushFromHex("#FF0000"), null, flagRect);
                    dc.DrawRectangle(BrushFromHex("#FFFFFF"), null, new Rect(padX + (w * 0.25), padY, w * 0.50, h));
                    dc.DrawEllipse(
                        BrushFromHex("#FF0000"),
                        null,
                        new Point(padX + (w * 0.50), padY + (h * 0.50)),
                        h * 0.20,
                        h * 0.20);
                    break;

                case "AU": // Australia 🇦🇺
                    dc.DrawRectangle(BrushFromHex("#00008B"), null, flagRect);
                    dc.DrawRectangle(BrushFromHex("#C8102E"), null, new Rect(padX, padY, w * 0.45, h * 0.50));
                    dc.DrawEllipse(BrushFromHex("#FFFFFF"), null, new Point(padX + (w * 0.72), padY + (h * 0.35)), h * 0.08, h * 0.08);
                    dc.DrawEllipse(BrushFromHex("#FFFFFF"), null, new Point(padX + (w * 0.25), padY + (h * 0.75)), h * 0.10, h * 0.10);
                    break;

                case "JP": // Japan 🇯🇵
                    dc.DrawRectangle(BrushFromHex("#FFFFFF"), null, flagRect);
                    dc.DrawEllipse(
                        BrushFromHex("#BC002D"),
                        null,
                        new Point(padX + (w * 0.50), padY + (h * 0.50)),
                        h * 0.28,
                        h * 0.28);
                    break;

                case "BR": // Brazil 🇧🇷
                    dc.DrawRectangle(BrushFromHex("#009C3B"), null, flagRect);
                    var diamond = new StreamGeometry();
                    using (var ctx = diamond.Open())
                    {
                        ctx.BeginFigure(new Point(padX + (w * 0.50), padY + (h * 0.12)), isFilled: true, isClosed: true);
                        ctx.LineTo(new Point(padX + (w * 0.88), padY + (h * 0.50)), isStroked: false, isSmoothJoin: false);
                        ctx.LineTo(new Point(padX + (w * 0.50), padY + (h * 0.88)), isStroked: false, isSmoothJoin: false);
                        ctx.LineTo(new Point(padX + (w * 0.12), padY + (h * 0.50)), isStroked: false, isSmoothJoin: false);
                    }
                    dc.DrawGeometry(BrushFromHex("#FFDF00"), null, diamond);
                    dc.DrawEllipse(
                        BrushFromHex("#002776"),
                        null,
                        new Point(padX + (w * 0.50), padY + (h * 0.50)),
                        h * 0.18,
                        h * 0.18);
                    break;

                case "AR": // Argentina 🇦🇷
                    dc.DrawRectangle(BrushFromHex("#74ACDF"), null, new Rect(padX, padY, w, h / 3.0));
                    dc.DrawRectangle(BrushFromHex("#FFFFFF"), null, new Rect(padX, padY + (h / 3.0), w, h / 3.0));
                    dc.DrawRectangle(BrushFromHex("#74ACDF"), null, new Rect(padX, padY + (2.0 * h / 3.0), w, h / 3.0));
                    dc.DrawEllipse(
                        BrushFromHex("#F6B40E"),
                        null,
                        new Point(padX + (w * 0.50), padY + (h * 0.50)),
                        h * 0.13,
                        h * 0.13);
                    break;

                default:
                    dc.DrawRectangle(BrushFromHex("#2563EB"), null, flagRect);
                    var formatted = new FormattedText(
                        countryCode,
                        CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight,
                        new Typeface("Segoe UI Semibold"),
                        h * 0.48,
                        Brushes.White,
                        1.0);
                    dc.DrawText(
                        formatted,
                        new Point(padX + ((w - formatted.Width) / 2.0), padY + ((h - formatted.Height) / 2.0)));
                    break;
            }

            dc.Pop();

            // Crisp subtle border around flag
            dc.DrawRoundedRectangle(
                null,
                new Pen(BrushFromHex("#4B5563"), 1.0),
                flagRect,
                radius,
                radius);
        }

        var rtb = new RenderTargetBitmap(size, size, 96.0, 96.0, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    private static BitmapSource RenderFallbackWpfGlyph(string emoji, int size)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var formatted = new FormattedText(
                emoji,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI Emoji"),
                size * 0.68,
                Brushes.White,
                1.0);

            dc.DrawText(
                formatted,
                new Point((size - formatted.Width) / 2.0, (size - formatted.Height) / 2.0));
        }

        var rtb = new RenderTargetBitmap(size, size, 96.0, 96.0, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    private static SolidColorBrush BrushFromHex(string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static void ReleaseComObject(nint ptr)
    {
        if (ptr != nint.Zero)
        {
            var vtbl = *(nint**)ptr;
            var release = (delegate* unmanaged[Stdcall]<nint, uint>)vtbl[2];
            _ = release(ptr);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Win32Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D2D1PixelFormat
    {
        public uint format;
        public uint alphaMode;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D2D1RenderTargetProperties
    {
        public uint type;
        public D2D1PixelFormat pixelFormat;
        public float dpiX;
        public float dpiY;
        public uint usage;
        public uint minLevel;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D2D1ColorF
    {
        public float r;
        public float g;
        public float b;
        public float a;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D2D1RectF
    {
        public float left;
        public float top;
        public float right;
        public float bottom;
    }

    [DllImport("d2d1.dll", ExactSpelling = true)]
    private static extern int D2D1CreateFactory(
        uint factoryType,
        ref Guid riid,
        nint pFactoryOptions,
        out nint ppIFactory);

    [DllImport("dwrite.dll", ExactSpelling = true)]
    private static extern int DWriteCreateFactory(
        uint factoryType,
        ref Guid iid,
        out nint factory);

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint hWnd, nint hDC);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint hdc);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(nint hdc);

    [DllImport("gdi32.dll")]
    private static extern nint CreateDIBSection(
        nint hdc,
        ref BitmapInfoHeader pbmi,
        uint usage,
        out nint ppvBits,
        nint hSection,
        uint offset);

    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint hdc, nint h);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint ho);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GdiFlush();
}
