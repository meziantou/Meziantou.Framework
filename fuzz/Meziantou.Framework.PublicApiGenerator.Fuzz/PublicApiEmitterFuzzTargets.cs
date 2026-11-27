using System.Text;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.PublicApiGenerator.Fuzz;

/// <summary>
/// The text processing of the generated files gives the same results as the straightforward implementations it replaced:
/// <list type="bullet">
/// <item>Qualifying the references that start with the last segment of the namespace with <c>global::</c> gives the text of the reference implementation, and qualifying the result again changes nothing.</item>
/// <item>Indenting a text gives the text of the implementation based on <see cref="StringReader.ReadLine"/>.</item>
/// <item>A text-only <see cref="DeclarationWriter"/> gives the text of a writer that keeps the segments, for any sequence of writes, nested writers and indentations.</item>
/// </list>
/// </summary>
public static class PublicApiEmitterFuzzTargets
{
    private static readonly string[] TextTokens =
    [
        "\n", "\r\n", "\r", " ", ".", "::", "global::", "\"", "'", "\\", "\\\"", "\\'", "_", "1",
        "System", "System.", "Generic", "Generic.", "Collections", "Demo", "Demo.", "namespace ", "public class ",
    ];

    private static readonly string[] WriterTokens =
    [
        "", " ", "int", "System.", "Demo", "{", "}", ";", "\n", "\r\n", "a\nb", "a\rb", "\"text\"", "'c'", "    ", "x",
    ];

    public static IReadOnlyList<FuzzTarget> Targets { get; } =
    [
        new("publicapi-emitter-text", EmitterText) { Seeds = GetEmitterTextSeeds, Dictionary = TextTokens },
        new("publicapi-declaration-writer", DeclarationWriterText) { Seeds = GetDeclarationWriterSeeds },
    ];

    // The first line is the namespace, the rest is the source of the types
    private static void EmitterText(ReadOnlySpan<byte> data)
    {
        var text = Encoding.UTF8.GetString(data);
        var lineBreakIndex = text.IndexOf('\n', StringComparison.Ordinal);
        var namespaceName = lineBreakIndex < 0 ? "System" : text[..lineBreakIndex];
        var source = lineBreakIndex < 0 ? text : text[(lineBreakIndex + 1)..];

        var conflictingNamespaceReference = PublicApiEmitter.GetConflictingNamespaceReference(namespaceName);
        var expectedReference = GetReferenceConflictingNamespaceIdentifier(namespaceName);
        FuzzAssert.Equal(expectedReference is null ? null : expectedReference + ".", conflictingNamespaceReference, "The conflicting reference is not the last segment of the namespace");
        if (conflictingNamespaceReference is not null)
        {
            var qualified = PublicApiEmitter.QualifyConflictingNamespaceReferences(source, conflictingNamespaceReference);
            FuzzAssert.Equal(ReferenceQualifyConflictingNamespaceReferences(source, expectedReference!), qualified, "The qualified source differs from the reference implementation");
            FuzzAssert.Equal(qualified, PublicApiEmitter.QualifyConflictingNamespaceReferences(qualified, conflictingNamespaceReference), "Qualifying the qualified source changes it");
        }

        for (var indentationLevel = 0; indentationLevel <= 2; indentationLevel++)
        {
            var actual = new StringBuilder();
            PublicApiEmitter.AppendIndented(actual, source, indentationLevel);
            FuzzAssert.Equal(ReferenceAppendIndented(source, indentationLevel), actual.ToString(), "The indented text differs from the reference implementation");
        }
    }

    // Each byte is an operation, applied to a writer that keeps the segments and to a text-only writer
    private static void DeclarationWriterText(ReadOnlySpan<byte> data)
    {
        var newLine = data.Length > 0 && data[0] % 2 == 1 ? "\r\n" : "\n";
        var writers = new List<(DeclarationWriter Segments, DeclarationWriter Text)>
        {
            (new DeclarationWriter(newLine), new DeclarationWriter(newLine, textOnly: true)),
        };

        foreach (var value in data)
        {
            var (segments, text) = writers[^1];
            var argument = value >> 3;
            switch (value & 7)
            {
                case 0:
                    segments.Text(WriterTokens[argument % WriterTokens.Length]);
                    text.Text(WriterTokens[argument % WriterTokens.Length]);
                    break;

                case 1:
                    segments.Keyword(WriterTokens[argument % WriterTokens.Length]);
                    text.Keyword(WriterTokens[argument % WriterTokens.Length]);
                    break;

                case 2:
                    segments.WriteLine();
                    text.WriteLine();
                    break;

                case 3:
                    segments.Indentation = argument % 4;
                    text.Indentation = argument % 4;
                    break;

                case 4:
                    writers.Add((segments.CreateWriter(), text.CreateWriter()));
                    break;

                // A writer that keeps the segments, written to a text-only writer (e.g. the parameters formatted for the public formatter)
                case 5:
                    writers.Add((new DeclarationWriter(newLine), new DeclarationWriter(newLine)));
                    break;

                case 6:
                    if (writers.Count > 1)
                    {
                        writers.RemoveAt(writers.Count - 1);
                        writers[^1].Segments.Write(segments);
                        writers[^1].Text.Write(text);
                    }

                    break;

                default:
                    var character = "{}; \nx\"t"[argument % 8];
                    FuzzAssert.Equal(segments.EndsWith(character), text.EndsWith(character), $"EndsWith({FuzzAssert.Format(character.ToString())}) differs");
                    FuzzAssert.Equal(segments.ContainsText(character), text.ContainsText(character), $"ContainsText({FuzzAssert.Format(character.ToString())}) differs");
                    break;
            }

            AssertSameText(writers[^1].Segments, writers[^1].Text);
        }

        while (writers.Count > 1)
        {
            var (segments, text) = writers[^1];
            writers.RemoveAt(writers.Count - 1);
            writers[^1].Segments.Write(segments);
            writers[^1].Text.Write(text);
            AssertSameText(writers[^1].Segments, writers[^1].Text);
        }
    }

    private static void AssertSameText(DeclarationWriter segments, DeclarationWriter text)
    {
        FuzzAssert.Equal(segments.GetText(), text.GetText(), "The text-only writer gives another text");
        FuzzAssert.Equal(segments.IsEmpty, text.IsEmpty, "IsEmpty differs");
    }

    private static string? GetReferenceConflictingNamespaceIdentifier(string namespaceName)
    {
        if (string.IsNullOrEmpty(namespaceName))
            return null;

        var lastSegment = namespaceName[(namespaceName.LastIndexOf('.', StringComparison.Ordinal) + 1)..];
        return lastSegment.Length == 0 ? null : lastSegment;
    }

    private static string ReferenceAppendIndented(string text, int indentationLevel)
    {
        var sb = new StringBuilder();
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length > 0)
            {
                sb.Append(' ', indentationLevel * 4);
            }

            sb.AppendLine(line);
        }

        return sb.ToString();
    }

    // The implementation of PublicApiEmitter.QualifyConflictingNamespaceReferences before it was optimized
    private static string ReferenceQualifyConflictingNamespaceReferences(string source, string conflictingNamespaceIdentifier)
    {
        var sb = new StringBuilder(source.Length);
        var inString = false;
        var inChar = false;
        var escapeNext = false;
        for (var i = 0; i < source.Length; i++)
        {
            if (!inString && !inChar && ReferenceTryGetConflictingIdentifierLength(source, i, conflictingNamespaceIdentifier, out var identifierLength))
            {
                sb.Append("global::");
                sb.Append(source, i, identifierLength + 1);
                i += identifierLength;
                continue;
            }

            var ch = source[i];
            sb.Append(ch);

            if (inString)
            {
                if (escapeNext)
                {
                    escapeNext = false;
                }
                else if (ch == '\\')
                {
                    escapeNext = true;
                }
                else if (ch == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (inChar)
            {
                if (escapeNext)
                {
                    escapeNext = false;
                }
                else if (ch == '\\')
                {
                    escapeNext = true;
                }
                else if (ch == '\'')
                {
                    inChar = false;
                }

                continue;
            }

            if (ch == '"')
            {
                inString = true;
                continue;
            }

            if (ch == '\'')
            {
                inChar = true;
            }
        }

        return sb.ToString();
    }

    private static bool ReferenceTryGetConflictingIdentifierLength(string source, int index, string conflictingNamespaceIdentifier, out int identifierLength)
    {
        identifierLength = 0;
        if (index >= source.Length || (!char.IsLetter(source[index]) && source[index] != '_'))
            return false;

        var currentIndex = index + 1;
        while (currentIndex < source.Length && (char.IsLetterOrDigit(source[currentIndex]) || source[currentIndex] == '_'))
        {
            currentIndex++;
        }

        if (currentIndex >= source.Length || source[currentIndex] != '.')
            return false;

        var identifier = source[index..currentIndex];
        if (!string.Equals(identifier, conflictingNamespaceIdentifier, StringComparison.Ordinal))
            return false;

        if (index >= "global::".Length &&
            source[index - 8] == 'g' &&
            source[index - 7] == 'l' &&
            source[index - 6] == 'o' &&
            source[index - 5] == 'b' &&
            source[index - 4] == 'a' &&
            source[index - 3] == 'l' &&
            source[index - 2] == ':' &&
            source[index - 1] == ':')
        {
            return false;
        }

        if (index == 0)
        {
            identifierLength = identifier.Length;
            return true;
        }

        var previousCharacter = source[index - 1];
        if (char.IsLetterOrDigit(previousCharacter) || previousCharacter is '_' or '.')
            return false;

        identifierLength = identifier.Length;
        return true;
    }

    /// <summary>Sources like the generated ones: references inside and outside string and character literals, already qualified references, identifiers that only end with the segment, and every kind of line break.</summary>
    private static IEnumerable<FuzzSeed> GetEmitterTextSeeds()
    {
        yield return FuzzSeed.FromText("system", "System\npublic sealed class Demo : System.IDisposable\n{\n    public string Name { get => throw null; }\n    [System.Obsolete(\"Use System.Text instead\")]\n    public char Separator = '.';\n    public void Dispose() { }\n}\n");
        yield return FuzzSeed.FromText("generic", "System.Collections.Generic\npublic class Generic.List<T> : Generic.IList<T>, global::Generic.ICollection<T>, MyGeneric.X, a.Generic.B, _Generic.C, Generic1.D\n");
        yield return FuzzSeed.FromText("escaped-literals", "Demo\nvoid M(string a = \"\\\" Demo.X\", char b = '\\'', char c = '\\\\') => Demo.Y.Z;\n");
        yield return FuzzSeed.FromText("global-namespace", "\nDemo.X x;\n");
        yield return FuzzSeed.FromText("empty-last-segment", "Demo.\nDemo.X x;\n");
        yield return FuzzSeed.FromText("line-breaks", "Demo\r\nline1\r\n\r\nline2\rline3\n\nDemo.X\r");
        yield return FuzzSeed.FromText("no-line-break", "System.Int32 System.Value");
    }

    /// <summary>Operation sequences: writes, line breaks, indentations, nested writers written back with another indentation, and queries.</summary>
    private static IEnumerable<FuzzSeed> GetDeclarationWriterSeeds()
    {
        yield return FuzzSeed.FromText("lines", "\u0010\u0002\u0018\u0002\u0002\u0050\u0007\u000F");
        yield return FuzzSeed.FromText("nested-indented", "\u0001\u000B\u0004\u0020\u0002\u0028\u0002\u0002\u0030\u0006\u0002\u0017\u0006");
        yield return FuzzSeed.FromText("nested-segments", "\u0005\u0013\u0040\u0002\u0058\u0006\u0007\u002F\u0004\u0004\u0048\u0002");
        yield return FuzzSeed.FromText("crlf-multiline-tokens", "\u0001\u0050\u0058\u0002\u0004\u000B\u0060\u0068\u0006\u0037");
    }
}
