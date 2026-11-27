using System.Buffers.Binary;
using System.Globalization;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Fuzz;

// The screens of the Window Management API (https://w3c.github.io/window-management/): the order of the screens, the
// current screen of a window, and the events of the changes
public static partial class WebPlatformFuzzTargets
{
    private const byte ScreenMoveWindow = 0;
    private const byte ScreenResizeWindow = 1;
    private const byte ScreenChange = 2;
    private const byte ScreenAdd = 3;
    private const byte ScreenRemove = 4;
    private const byte ScreenOperationCount = 5;

    /// <summary>
    /// Builds screens from the input, then runs a sequence of operations (move or resize the client window, change, add or
    /// remove a screen), and checks the properties of <c>getScreenDetails()</c> after each one: the screens are sorted by
    /// position, the current screen is one of them and has the largest intersection with the client window (or is the
    /// primary screen), <c>window.screen</c> has the size of the current screen, and <c>isExtended</c> tells whether there
    /// are several screens.
    /// </summary>
    public static void ScreenDetailsTarget(ReadOnlySpan<byte> data)
    {
        var provider = new ScreenProvider();
        var count = data.IsEmpty ? 1 : 1 + (data[0] % 4);
        data = data.IsEmpty ? data : data[1..];
        for (var i = 0; i < count; i++)
        {
            provider.AddScreen(ReadFuzzScreen(ref data));
        }

        var permissions = new PermissionStore();
        permissions.SetState("window-management", PermissionState.Granted);
        using var context = new BrowsingContext(new BrowsingContextOptions { Screens = new ScreenOptions { Provider = provider }, Permissions = permissions });
        var document = context.Navigate("https://example.com/", _ => "<!DOCTYPE html>");
        context.EventLoop.RunUntilIdle();
        var window = document.DefaultView!;
        var request = window.GetScreenDetails();
        context.EventLoop.RunUntilIdle();
        FuzzAssert.True(request.IsCompletedSuccessfully, "getScreenDetails() did not resolve");
        var details = request.Result;
        CheckScreenDetails(window, details, provider);

        for (var step = 0; step < 32 && data.Length >= 5; step++)
        {
            var operation = (byte)(data[0] % ScreenOperationCount);
            var a = BinaryPrimitives.ReadInt16LittleEndian(data[1..]);
            var b = BinaryPrimitives.ReadInt16LittleEndian(data[3..]);
            data = data[5..];
            var screens = provider.Screens;
            switch (operation)
            {
                case ScreenMoveWindow:
                    context.MoveClientWindow(a, b);
                    break;

                case ScreenResizeWindow:
                    context.ResizeClientWindow(Math.Abs((int)a), Math.Abs((int)b));
                    break;

                case ScreenChange when screens.Count > 0:
                    var screen = screens[Math.Abs(a % screens.Count)];
                    screen.Left = b;
                    screen.Width = Math.Abs((int)a);
                    screen.IsPrimary = (b & 1) != 0;
                    break;

                case ScreenAdd when screens.Count < 6:
                    provider.AddScreen(new DeviceScreen(a, b, 1 + Math.Abs(a % 4000), 1 + Math.Abs(b % 3000)) { Label = "Screen " + screens.Count.ToString(CultureInfo.InvariantCulture) });
                    break;

                case ScreenRemove when screens.Count > 0:
                    provider.RemoveScreen(screens[Math.Abs(a % screens.Count)]);
                    break;
            }

            context.EventLoop.RunUntilIdle();
            CheckScreenDetails(window, details, provider);
        }
    }

    private static DeviceScreen ReadFuzzScreen(ref ReadOnlySpan<byte> data)
    {
        if (data.Length < 9)
        {
            data = [];
            return new DeviceScreen(0, 0, 1920, 1080) { IsPrimary = true };
        }

        var left = BinaryPrimitives.ReadInt16LittleEndian(data);
        var top = BinaryPrimitives.ReadInt16LittleEndian(data[2..]);
        var width = BinaryPrimitives.ReadUInt16LittleEndian(data[4..]) % 5000;
        var height = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]) % 5000;
        var flags = data[8];
        data = data[9..];
        return new DeviceScreen(left, top, width, height)
        {
            AvailHeight = Math.Max(0, height - (flags >> 2)),
            IsPrimary = (flags & 1) != 0,
            IsInternal = (flags & 2) != 0,
            DevicePixelRatio = 1 + (flags & 3),
        };
    }

    private static void CheckScreenDetails(Window window, ScreenDetails details, ScreenProvider provider)
    {
        var screens = details.Screens;
        var deviceScreens = provider.Screens;
        FuzzAssert.Equal(Math.Max(1, deviceScreens.Count), screens.Count, "screens does not have a screen per screen of the device");
        for (var i = 1; i < screens.Count; i++)
        {
            FuzzAssert.True(screens[i - 1].Left < screens[i].Left || (screens[i - 1].Left == screens[i].Left && screens[i - 1].Top <= screens[i].Top), "The screens are not sorted by position");
        }

        var current = details.CurrentScreen;
        FuzzAssert.True(current is not null && screens.Contains(current), "currentScreen is not one of the screens");
        FuzzAssert.Equal(current.Width, window.Screen.Width, "window.screen does not have the width of the current screen");
        FuzzAssert.Equal(current.AvailHeight, window.Screen.AvailHeight, "window.screen does not have the available height of the current screen");
        FuzzAssert.Equal(deviceScreens.Count > 1, window.Screen.IsExtended, "isExtended does not tell whether there are several screens");

        // The current screen has the largest intersection with the client window
        long Intersection(ScreenDetailed screen)
        {
            var width = Math.Min((long)window.ScreenX + window.OuterWidth, (long)screen.Left + screen.Width) - Math.Max(window.ScreenX, screen.Left);
            var height = Math.Min((long)window.ScreenY + window.OuterHeight, (long)screen.Top + screen.Height) - Math.Max(window.ScreenY, screen.Top);
            return width > 0 && height > 0 ? width * height : 0;
        }

        if (screens.Count > 1)
        {
            var best = screens.Max(Intersection);
            FuzzAssert.Equal(best, Intersection(current), "The current screen does not have the largest intersection with the client window");
        }
    }

    private static IEnumerable<FuzzSeed> GetScreenDetailsSeeds()
    {
        static byte[] Screen(short left, short top, ushort width, ushort height, byte flags)
        {
            var bytes = new byte[9];
            BinaryPrimitives.WriteInt16LittleEndian(bytes, left);
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(2), top);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), width);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(6), height);
            bytes[8] = flags;
            return bytes;
        }

        static byte[] Operation(byte operation, short a, short b)
        {
            var bytes = new byte[5];
            bytes[0] = operation;
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(1), a);
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(3), b);
            return bytes;
        }

        byte[][] seeds =
        [
            [0, .. Screen(0, 0, 1920, 1080, 0x83)],
            [1, .. Screen(0, 0, 1920, 1080, 0xA3), .. Screen(1920, 0, 1280, 1024, 0), .. Operation(ScreenMoveWindow, 2000, 10), .. Operation(ScreenMoveWindow, 0, 0)],
            [1, .. Screen(0, 0, 1440, 900, 0x67), .. Screen(-2560, 0, 2560, 1440, 0), .. Operation(ScreenResizeWindow, 2000, 1000), .. Operation(ScreenChange, 1, 3000)],
            [2, .. Screen(0, 0, 800, 600, 1), .. Screen(0, 600, 800, 600, 0), .. Screen(800, 0, 800, 1200, 0), .. Operation(ScreenRemove, 0, 0), .. Operation(ScreenAdd, -500, 200), .. Operation(ScreenMoveWindow, 900, 700)],
            [3, .. Screen(0, 0, 0, 0, 0), .. Screen(0, 0, 0, 0, 0), .. Screen(100, 100, 10, 10, 1), .. Screen(-32768, -32768, 4999, 4999, 0)],
        ];

        for (var i = 0; i < seeds.Length; i++)
        {
            yield return new FuzzSeed("screen-details-" + i.ToString(CultureInfo.InvariantCulture), seeds[i]);
        }
    }
}
