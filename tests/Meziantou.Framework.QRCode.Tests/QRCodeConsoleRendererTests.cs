using Meziantou.Framework.InlineSnapshotTesting;
using Xunit;

namespace Meziantou.Framework.Tests;

public class QRCodeConsoleRendererTests
{
    [Fact]
    public void ToConsoleString_DefaultOptions()
    {
        var qr = QRCode.Create("TEST", ErrorCorrectionLevel.L);
        var text = qr.ToConsoleString();

        Assert.NotNull(text);
        Assert.NotEmpty(text);
    }

    [Fact]
    public void ToConsoleString_SmallQRCode_Snapshot()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);
        var text = qr.ToConsoleString(new QRCodeConsoleOptions { QuietZoneModules = 0 });

        InlineSnapshot.Validate(text, """
            █▀▀▀▀▀█  █▄█▀ █▀▀▀▀▀█
            █ ███ █ ▀█ █▀ █ ███ █
            █ ▀▀▀ █   ▀ █ █ ▀▀▀ █
            ▀▀▀▀▀▀▀ █▄▀▄█ ▀▀▀▀▀▀▀
            ▀██▄▀▀▀█▀▀█▀ ▀█   █▄
             ▄ ▀▀█▀▄▄▄█ ▀ ▄ ▀ ▄▄▀
            ▀▀▀ ▀▀▀ ██ ▄▀▄▀▄▀▄▀▀▀
            █▀▀▀▀▀█ █▄██▄█▀█▄█▀▄
            █ ███ █ ▀▄ ▀ ▀█▀ ▀█▄▀
            █ ▀▀▀ █ █▀█ ▀ ▄ ▀ ▄▄█
            ▀▀▀▀▀▀▀ ▀▀▀ ▀ ▀ ▀ ▀ ▀
            """);
    }

    [Fact]
    public void ToConsoleString_InvertedColors()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);
        var normal = qr.ToConsoleString(new QRCodeConsoleOptions { QuietZoneModules = 0 });
        var inverted = qr.ToConsoleString(new QRCodeConsoleOptions { QuietZoneModules = 0, InvertColors = true });

        Assert.NotEqual(normal, inverted);
        InlineSnapshot.Validate(inverted, """
             ▄▄▄▄▄ ██ ▀ ▄█ ▄▄▄▄▄
             █   █ █▄ █ ▄█ █   █
             █▄▄▄█ ███▄█ █ █▄▄▄█
            ▄▄▄▄▄▄▄█ ▀▄▀ █▄▄▄▄▄▄▄
            ▄  ▀▄▄▄ ▄▄ ▄█▄ ███ ▀█
            █▀█▄▄ ▄▀▀▀ █▄█▀█▄█▀▀▄
            ▄▄▄█▄▄▄█  █▀▄▀▄▀▄▀▄▄▄
             ▄▄▄▄▄ █ ▀  ▀ ▄ ▀ ▄▀█
             █   █ █▄▀█▄█▄ ▄█▄ ▀▄
             █▄▄▄█ █ ▄ █▄█▀█▄█▀▀
                   ▀   ▀ ▀ ▀ ▀ ▀
            """);
    }

    [Fact]
    public void ToConsoleString_WithQuietZone()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);
        var withoutQuiet = qr.ToConsoleString(new QRCodeConsoleOptions { QuietZoneModules = 0 });
        var withQuiet = qr.ToConsoleString(new QRCodeConsoleOptions { QuietZoneModules = 2 });

        Assert.True(withQuiet.Length > withoutQuiet.Length);
    }

    [Fact]
    public void WriteTo_TextWriter_MatchesToConsoleString()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);
        var options = new QRCodeConsoleOptions { QuietZoneModules = 0 };

        var expected = qr.ToConsoleString(options);
        using var writer = new StringWriter();
        qr.WriteTo(writer, options);
        var actual = writer.ToString();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ToConsoleString_FullSnapshot()
    {
        var qr = QRCode.Create("HELLO", ErrorCorrectionLevel.L);
        var text = qr.ToConsoleString(new QRCodeConsoleOptions { QuietZoneModules = 0 });

        InlineSnapshot.Validate(text, """
            █▀▀▀▀▀█ ▄▀ ▄▀ █▀▀▀▀▀█
            █ ███ █ ▄▀ ▄  █ ███ █
            █ ▀▀▀ █ ▄▄█▀█ █ ▀▀▀ █
            ▀▀▀▀▀▀▀ ▀ █▄█ ▀▀▀▀▀▀▀
            █▀█▀▀▄▀▀▀▀  █▀ █ █▄█▄
             ▄▄▄▄▄▀▄▀▀█▀ ▀ ▄█▀  ▀
             ▀▀  ▀▀ ██▀█▄█▄ ▀▄▀▄▄
            █▀▀▀▀▀█ ▀▄█▄█▄█▀ ▄▀ █
            █ ███ █ █ ▄ █  █  █
            █ ▀▀▀ █ █ ▄▀ ▀ ▄█ █ ▄
            ▀▀▀▀▀▀▀ ▀  ▀ ▀  ▀ ▀
            """);
    }

    [Fact]
    public void WriteTo_DefaultOptions_MatchesToConsoleString()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);

        var expected = qr.ToConsoleString();
        using var writer = new StringWriter();
        qr.WriteTo(writer);
        var actual = writer.ToString();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ToConsoleString_DefaultOptions_UsesQuietZone2()
    {
        var qr = QRCode.Create("A", ErrorCorrectionLevel.L);
        var withDefault = qr.ToConsoleString();
        var withQuiet2 = qr.ToConsoleString(new QRCodeConsoleOptions { QuietZoneModules = 2 });

        Assert.Equal(withDefault, withQuiet2);
    }

    [Fact]
    public void ToConsoleString_InvertedSnapshot()
    {
        var qr = QRCode.Create("HELLO", ErrorCorrectionLevel.L);
        var text = qr.ToConsoleString(new QRCodeConsoleOptions { QuietZoneModules = 0, InvertColors = true });

        InlineSnapshot.Validate(text, """
             ▄▄▄▄▄ █▀▄█▀▄█ ▄▄▄▄▄
             █   █ █▀▄█▀██ █   █
             █▄▄▄█ █▀▀ ▄ █ █▄▄▄█
            ▄▄▄▄▄▄▄█▄█ ▀ █▄▄▄▄▄▄▄
             ▄ ▄▄▀▄▄▄▄██ ▄█ █ ▀ ▀
            █▀▀▀▀▀▄▀▄▄ ▄█▄█▀ ▄██▄
            █▄▄██▄▄█  ▄ ▀ ▀█▄▀▄▀▀
             ▄▄▄▄▄ █▄▀ ▀ ▀ ▄█▀▄█
             █   █ █ █▀█ ██ ██ ██
             █▄▄▄█ █ █▀▄█▄█▀ █ █▀
                   ▀ ▀▀ ▀ ▀▀ ▀ ▀▀
            """);
    }
}
