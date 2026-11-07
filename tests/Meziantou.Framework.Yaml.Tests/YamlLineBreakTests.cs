using Meziantou.Framework.Yaml.Events;
using Meziantou.Framework.Yaml.Model;
using Scalar = Meziantou.Framework.Yaml.Events.Scalar;

namespace Meziantou.Framework.Yaml.Tests;

/// <summary>
/// YAML 1.1 treated NEL (U+0085), LS (U+2028), and PS (U+2029) as line breaks. YAML 1.2 excludes them, so a reader
/// keeps them as ordinary scalar content instead of folding them into a space.
/// </summary>
public sealed class YamlLineBreakTests
{
    public static TheoryData<char> NonBreakCharacters { get; } = ['\u0085', '\u2028', '\u2029'];

    [Theory]
    [MemberData(nameof(NonBreakCharacters))]
    public void DoubleQuotedScalarKeepsTheCharacter(char character)
    {
        var value = YamlSerializer.Deserialize<string>($"\"a{character}b\"");

        Assert.Equal($"a{character}b", value);
    }

    [Theory]
    [MemberData(nameof(NonBreakCharacters))]
    public void SingleQuotedScalarKeepsTheCharacter(char character)
    {
        var value = YamlSerializer.Deserialize<string>($"'a{character}b'");

        Assert.Equal($"a{character}b", value);
    }

    [Theory]
    [MemberData(nameof(NonBreakCharacters))]
    public void PlainScalarKeepsTheCharacter(char character)
    {
        var value = YamlSerializer.Deserialize<string>($"a{character}b");

        Assert.Equal($"a{character}b", value);
    }

    [Theory]
    [MemberData(nameof(NonBreakCharacters))]
    public void LiteralBlockScalarKeepsTheCharacter(char character)
    {
        var value = YamlSerializer.Deserialize<string>($"|-\n  a{character}b\n");

        Assert.Equal($"a{character}b", value);
    }

    [Theory]
    [MemberData(nameof(NonBreakCharacters))]
    public void RoundTripKeepsTheCharacter(char character)
    {
        var value = $"a{character}b";

        Assert.Equal(value, YamlSerializer.Deserialize<string>(YamlSerializer.Serialize(value)));
    }

    [Theory]
    [MemberData(nameof(NonBreakCharacters))]
    public void TheCharacterDoesNotStartANewLine(char character)
    {
        var scalar = ReadFirstScalar($"key: a{character}b\n");

        Assert.Equal($"a{character}b", scalar.Value, "scalar value");
        Assert.Equal(0, scalar.Start.Line, "start line");
        Assert.Equal(5, scalar.Start.Column, "start column");
        Assert.Equal(0, scalar.End.Line, "end line");
        Assert.Equal(8, scalar.End.Column, "end column");
    }

    [Fact]
    public void LineFeedStillStartsANewLine()
    {
        var scalar = ReadFirstScalar("key: a\n  b\n");

        Assert.Equal("a b", scalar.Value);
        Assert.Equal(0, scalar.Start.Line);
        Assert.Equal(1, scalar.End.Line);
    }

    private static Scalar ReadFirstScalar(string yaml)
    {
        var parser = Parser.CreateParser(new StringReader(yaml));
        while (parser.MoveNext())
        {
            if (parser.Current is Scalar scalar && !string.Equals(scalar.Value, "key", StringComparison.Ordinal))
            {
                return scalar;
            }
        }

        throw new InvalidOperationException("No scalar found.");
    }

    [Theory]
    [MemberData(nameof(NonBreakCharacters))]
    public void EmitterRoundTripKeepsTheCharacter(char character)
    {
        var value = $"a{character}b";
        var stream = new YamlStream { new YamlDocument { Contents = new YamlValue(value) } };

        using var writer = new StringWriter();
        stream.WriteTo(writer);
        var text = writer.ToString();

        var reloaded = YamlStream.Load(new EventReader(Parser.CreateParser(new StringReader(text))));

        Assert.Equal(value, ((YamlValue)reloaded[0].Contents!).Value);
    }
}
