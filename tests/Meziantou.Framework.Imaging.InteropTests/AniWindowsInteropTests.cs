using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.TestHarness.ExternalTools;

namespace Meziantou.Framework.Imaging.InteropTests;

/// <summary>
/// Animated cursors written by the library and loaded by Windows itself (user32), the only independent reader of the
/// format available to the tests: the step count and the rate of each step (<c>GetCursorFrameInfo</c>), the hotspot of
/// each step (<c>GetIconInfo</c>) and the pixels Windows draws for each step (<c>DrawIconEx</c> over a black and over a
/// white background, so that transparency is checked through what is displayed). The file repeats a frame, so it also
/// checks that Windows accepts the <c>rate</c> and <c>seq </c> chunks the encoder writes after the frame list.
/// </summary>
/// <remarks>user32 is part of Windows and cannot be downloaded: the cases are skipped on other systems.</remarks>
public sealed class AniWindowsInteropTests
{
    private const int Side = 32;
    private const int Black = 0x000000;
    private const int White = 0xFFFFFF;

    [Theory]
    [InlineData(IconPayloadFormat.Dib)]
    [InlineData(IconPayloadFormat.Png)]
    public void WindowsPlaysTheAnimatedCursorWrittenByTheLibrary(IconPayloadFormat payloadFormat)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.XunitSkip("Animated cursors are loaded by user32, which is only available on Windows.");
            return;
        }

        // Three steps over two stored frames: A, B, then A again
        var frames = new[] { CreatePixels(seed: 1), CreatePixels(seed: 2) };
        int[] sequence = [0, 1, 0];
        uint[] rates = [6, 12, 18];
        Point[] hotspots = [new Point(1, 2), new Point(30, 31)];

        using var image = Image.ImportPixelData<Rgba32>(frames[0], Side, Side);
        image.AppendFrame();
        image.AppendFrame();
        for (var step = 0; step < sequence.Length; step++)
        {
            var frame = image.Frames[step];
            var pixels = frames[sequence[step]];
            frame.ProcessPixelRows(pixels, static (accessor, source) =>
            {
                for (var y = 0; y < accessor.Height; y++)
                {
                    source.AsSpan(y * accessor.Width, accessor.Width).CopyTo(accessor.GetRowSpan(y));
                }
            });

            frame.Metadata.Duration = new FrameDuration(rates[step], 60);
            frame.Metadata.Hotspot = hotspots[sequence[step]];
        }

        var path = InteropSettings.GetArtifactsDirectory(nameof(AniWindowsInteropTests)) / $"user32-{payloadFormat}.ani";
        image.Save(path, new AniEncoder { PayloadFormat = payloadFormat });

        var cursor = NativeMethods.LoadImageW(0, path, NativeMethods.ImageCursor, 0, 0, NativeMethods.LoadFromFile);
        if (cursor == 0)
            Assert.Fail($"user32 could not load {path.Name} (error {Marshal.GetLastPInvokeError()}).");

        try
        {
            for (var step = 0; step < sequence.Length; step++)
            {
                var stepCursor = NativeMethods.GetCursorFrameInfo(cursor, 0, (uint)step, out var rate, out var stepCount);
                Assert.NotEqual(0, stepCursor);
                Assert.Equal((uint)sequence.Length, stepCount);
                Assert.Equal(rates[step], rate);

                Assert.Equal(hotspots[sequence[step]], GetHotspot(stepCursor));
                var expected = frames[sequence[step]];
                AssertDrawn(cursor, step, Black, expected, $"{payloadFormat} step {step} over black");
                AssertDrawn(cursor, step, White, expected, $"{payloadFormat} step {step} over white");
            }
        }
        finally
        {
            NativeMethods.DestroyCursor(cursor);
        }
    }

    /// <summary>Opaque colors that differ for every pixel and seed, with a fully transparent band (alpha is 0 or 255 only, so no blending rule is involved).</summary>
    private static Rgba32[] CreatePixels(int seed)
    {
        var pixels = new Rgba32[Side * Side];
        for (var y = 0; y < Side; y++)
        {
            for (var x = 0; x < Side; x++)
            {
                pixels[(y * Side) + x] = (x + y + seed) % 5 == 0
                    ? default
                    : new Rgba32((byte)((x * 8) + seed), (byte)((y * 8) + seed), (byte)(seed * 90), 255);
            }
        }

        return pixels;
    }

    [SupportedOSPlatform("windows")]
    private static Point GetHotspot(nint cursor)
    {
        if (!NativeMethods.GetIconInfo(cursor, out var info))
            Assert.Fail($"GetIconInfo failed (error {Marshal.GetLastPInvokeError()}).");

        // GetIconInfo creates copies of the bitmaps, which the caller deletes
        if (info.Mask != 0)
        {
            NativeMethods.DeleteObject(info.Mask);
        }

        if (info.Color != 0)
        {
            NativeMethods.DeleteObject(info.Color);
        }

        return new Point((int)info.HotspotX, (int)info.HotspotY);
    }

    /// <summary>Draws one step over a solid background and compares what is displayed: the color of an opaque pixel, the background under a transparent one.</summary>
    [SupportedOSPlatform("windows")]
    private static void AssertDrawn(nint cursor, int step, int background, Rgba32[] expected, string context)
    {
        var header = new NativeMethods.BitmapInfoHeader
        {
            Size = (uint)Marshal.SizeOf<NativeMethods.BitmapInfoHeader>(),
            Width = Side,
            Height = -Side, // top-down rows
            Planes = 1,
            BitCount = 32,
        };

        var deviceContext = NativeMethods.CreateCompatibleDC(0);
        Assert.NotEqual(0, deviceContext);
        try
        {
            var bitmap = NativeMethods.CreateDIBSection(deviceContext, in header, 0, out var bits, 0, 0);
            Assert.NotEqual(0, bitmap);
            Assert.NotEqual(0, bits);
            try
            {
                var previous = NativeMethods.SelectObject(deviceContext, bitmap);
                var drawn = new int[Side * Side];
                Array.Fill(drawn, background);

                // The bits of the DIB section stay valid until the bitmap is deleted, below
                unsafe
                {
                    Marshal.Copy(drawn, 0, bits, drawn.Length);
                }

                var succeeded = NativeMethods.DrawIconEx(deviceContext, 0, 0, cursor, Side, Side, (uint)step, 0, NativeMethods.DrawNormal);
                NativeMethods.GdiFlush();
                unsafe
                {
                    Marshal.Copy(bits, drawn, 0, drawn.Length);
                }

                NativeMethods.SelectObject(deviceContext, previous);
                Assert.True(succeeded, $"DrawIconEx failed for {context} (error {Marshal.GetLastPInvokeError()}).");

                for (var i = 0; i < drawn.Length; i++)
                {
                    // A 32-bit DIB pixel is 0x00RRGGBB; the unused high byte is not compared
                    var pixel = expected[i];
                    var expectedColor = pixel.A == 0 ? background : (pixel.R << 16) | (pixel.G << 8) | pixel.B;
                    if ((drawn[i] & 0xFFFFFF) != expectedColor)
                        Assert.Fail($"{context}: the pixel ({i % Side}, {i / Side}) is 0x{drawn[i] & 0xFFFFFF:X6}; 0x{expectedColor:X6} is expected.");
                }
            }
            finally
            {
                NativeMethods.DeleteObject(bitmap);
            }
        }
        finally
        {
            NativeMethods.DeleteDC(deviceContext);
        }
    }

    /// <summary>
    /// The user32 and gdi32 entry points of the test. They are plain <c>DllImport</c> declarations: the signatures expose no
    /// pointer, and the source-generated form does not compile for every target framework of the test projects under the
    /// memory-safety rules of the repository.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static class NativeMethods
    {
        public const uint ImageCursor = 2;
        public const uint LoadFromFile = 0x0010;
        public const uint DrawNormal = 0x0003;

        private const string LibraryImportJustification = "The generated marshalling code does not compile under the memory-safety rules for every target framework; this test-only declaration needs no marshalling beyond what the runtime does.";

        [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [SuppressMessage("Interoperability", "SYSLIB1054:Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time", Justification = LibraryImportJustification)]
        public static safe extern nint LoadImageW(nint instance, string name, uint type, int width, int height, uint flags);

        /// <summary>Undocumented but stable since Windows NT: the cursor of one step of an animated cursor, its rate in jiffies and the number of steps.</summary>
        [DllImport("user32.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [SuppressMessage("Interoperability", "SYSLIB1054:Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time", Justification = LibraryImportJustification)]
        public static safe extern nint GetCursorFrameInfo(nint cursor, nint reserved, uint step, out uint rate, out uint stepCount);

        [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [SuppressMessage("Interoperability", "SYSLIB1054:Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time", Justification = LibraryImportJustification)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static safe extern bool GetIconInfo(nint icon, out IconInfo info);

        [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [SuppressMessage("Interoperability", "SYSLIB1054:Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time", Justification = LibraryImportJustification)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static safe extern bool DrawIconEx(nint deviceContext, int left, int top, nint icon, int width, int height, uint stepIfAnimatedCursor, nint flickerFreeBrush, uint flags);

        [DllImport("user32.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [SuppressMessage("Interoperability", "SYSLIB1054:Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time", Justification = LibraryImportJustification)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static safe extern bool DestroyCursor(nint cursor);

        [DllImport("gdi32.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [SuppressMessage("Interoperability", "SYSLIB1054:Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time", Justification = LibraryImportJustification)]
        public static safe extern nint CreateCompatibleDC(nint deviceContext);

        [DllImport("gdi32.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [SuppressMessage("Interoperability", "SYSLIB1054:Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time", Justification = LibraryImportJustification)]
        public static safe extern nint CreateDIBSection(nint deviceContext, in BitmapInfoHeader header, uint usage, out nint bits, nint section, uint offset);

        [DllImport("gdi32.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [SuppressMessage("Interoperability", "SYSLIB1054:Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time", Justification = LibraryImportJustification)]
        public static safe extern nint SelectObject(nint deviceContext, nint value);

        [DllImport("gdi32.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [SuppressMessage("Interoperability", "SYSLIB1054:Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time", Justification = LibraryImportJustification)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static safe extern bool DeleteObject(nint value);

        [DllImport("gdi32.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [SuppressMessage("Interoperability", "SYSLIB1054:Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time", Justification = LibraryImportJustification)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static safe extern bool DeleteDC(nint deviceContext);

        [DllImport("gdi32.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [SuppressMessage("Interoperability", "SYSLIB1054:Use 'LibraryImportAttribute' instead of 'DllImportAttribute' to generate P/Invoke marshalling code at compile time", Justification = LibraryImportJustification)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static safe extern bool GdiFlush();

        /// <summary><c>ICONINFO</c>.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct IconInfo
        {
            public int IsIcon;
            public uint HotspotX;
            public uint HotspotY;
            public nint Mask;
            public nint Color;
        }

        /// <summary><c>BITMAPINFOHEADER</c>, which is a whole <c>BITMAPINFO</c> for a 32-bit <c>BI_RGB</c> bitmap (no color table).</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct BitmapInfoHeader
        {
            public uint Size;
            public int Width;
            public int Height;
            public ushort Planes;
            public ushort BitCount;
            public uint Compression;
            public uint SizeImage;
            public int XPelsPerMeter;
            public int YPelsPerMeter;
            public uint ColorsUsed;
            public uint ColorsImportant;
        }
    }
}
