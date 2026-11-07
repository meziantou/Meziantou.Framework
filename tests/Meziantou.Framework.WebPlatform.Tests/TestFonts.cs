namespace Meziantou.Framework.WebPlatform.Tests;

internal static class TestFonts
{
    /// <summary>The data of the Ahem font bundled with the library (a valid TrueType font).</summary>
    public static byte[] Ahem { get; } = File.ReadAllBytes(Path.Combine(TestPaths.Root, "src", "Meziantou.Framework.WebPlatform", "Css", "Fonts", "Bundled", "Ahem.ttf"));
}
