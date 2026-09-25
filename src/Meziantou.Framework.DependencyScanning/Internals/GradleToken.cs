using System.Runtime.InteropServices;

namespace Meziantou.Framework.DependencyScanning.Internals;

/// <summary>A token of a Gradle build script.</summary>
/// <param name="Kind">The kind of token.</param>
/// <param name="Start">The offset of the first character of the token.</param>
/// <param name="End">The offset after the last character of the token.</param>
/// <param name="ContentStart">For a string, the offset of its first character after the opening quotes.</param>
/// <param name="ContentEnd">For a string, the offset of its closing quotes.</param>
/// <param name="IsVerbatim">For a string, whether its value is exactly its text: it is terminated, and has no escape sequence, interpolation or line break.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct GradleToken(GradleTokenKind Kind, int Start, int End, int ContentStart, int ContentEnd, bool IsVerbatim)
{
    public bool Is(string text, GradleTokenKind kind, string value)
    {
        return Kind == kind && text.AsSpan(Start, End - Start).SequenceEqual(value);
    }

    public string GetText(string text) => text[Start..End];

    public string GetContent(string text) => text[ContentStart..ContentEnd];
}
