// The examples of src/Meziantou.Framework.Fonts/readme.md. They read the fonts installed on the machine, so CI compiles
// them but does not run them.
//   dotnet run --project samples/Meziantou.Framework.Fonts.Samples
using Meziantou.Framework.Fonts;

#region Fonts_SystemFontCollection
// The fonts of the machine are scanned once per process; only the tables that describe a face are read.
var fonts = SystemFontCollection.Default;
if (fonts.Match("Arial", weight: 700, isItalic: false) is { } face)
{
    using var stream = File.OpenRead(face.FilePath);
    var font = FontLoader.Load(stream, new PooledFontEnvironment(FontResourceLimits.Default, 64 * 1024 * 1024))[face.FaceIndex];
    Console.WriteLine($"{font.FamilyName} {font.SubfamilyName}: {font.UnitsPerEm} units per em");
}
#endregion
