namespace Meziantou.Framework.InlineSnapshotTesting;

[Flags]
public enum CSharpStringFormats
{
    /// <summary>
    /// Quoted string literals start and end with a single double quote character (") on the same line.
    /// Quoted string literals are best suited for strings that fit on a single line and don't include 
    /// any escape sequences.
    /// </summary>
    /// <see href="https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/strings/?WT.mc_id=DT-MVP-5003978#quoted-string-literals"/>
    Quoted = 0x1,

    /// <summary>
    /// Verbatim string literals are more convenient for multi-line strings, strings that contain backslash 
    /// characters, or embedded double quotes. Verbatim strings preserve new line characters as part of the 
    /// string text. Use double quotation marks to embed a quotation mark inside a verbatim string.
    /// </summary>
    /// <see href="https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/strings/?WT.mc_id=DT-MVP-5003978#verbatim-string-literals"/>
    Verbatim = 0x2,

    /// <summary>
    /// Beginning with C# 11, you can use raw string literals to more easily create strings that are 
    /// multi-line, or use any characters requiring escape sequences. Raw string literals remove the
    /// need to ever use escape sequences. You can write the string, including whitespace formatting,
    /// how you want it to appear in output.
    /// </summary>
    /// <see href="https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/strings/?WT.mc_id=DT-MVP-5003978#raw-string-literals"/>
    Raw = 0x4,

    /// <summary>
    /// Beginning with C# 11, you can use raw string literals to more easily create strings that are 
    /// multi-line, or use any characters requiring escape sequences. Raw string literals remove the
    /// need to ever use escape sequences. You can write the string, including whitespace formatting,
    /// how you want it to appear in output.
    /// </summary>
    /// <see href="https://learn.microsoft.com/en-us/dotnet/csharp/programming-guide/strings/?WT.mc_id=DT-MVP-5003978#raw-string-literals"/>
    LeftAlignedRaw = 0x8,

    /// <summary>Determine the allowed syntax from the PDB file. If an incompatible mode is provided, it won't be used.</summary>
    /// <remarks>This is only valid with Portable PDB, and the compiler options must be set.</remarks>
    DetermineFeatureFromPdb = 0x10,

    Default = DetermineFeatureFromPdb | Quoted | Verbatim | Raw,
}
