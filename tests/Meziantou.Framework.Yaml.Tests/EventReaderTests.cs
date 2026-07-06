using Meziantou.Framework.Yaml.Events;

namespace Meziantou.Framework.Yaml.Tests;
public sealed class EventReaderTests
{
    [Fact]
    public void PeekAllowExpectAndSkip_WorkAsExpected()
    {
        const string Yaml = "root:\n  list:\n    - a\n    - b\n  value: 3\n";
        var parser = Parser.CreateParser(new StringReader(Yaml));
        var reader = new EventReader(parser);

        Assert.NotNull(reader.Peek<StreamStart>());
        reader.Expect<StreamStart>();

        Assert.NotNull(reader.Peek<DocumentStart>());
        reader.Expect<DocumentStart>();

        reader.Expect<MappingStart>();
        var key = reader.Expect<Scalar>();
        Assert.Equal("root", key.Value);

        // Skip the "root" mapping value entirely (it contains nested items).
        reader.Skip();

        reader.Expect<MappingEnd>();
        reader.Expect<DocumentEnd>();
        reader.Expect<StreamEnd>();
    }

    [Fact]
    public void Skip_UntilDepth_StopsAtRequestedDepth()
    {
        const string Yaml = "a:\n  b:\n    c: 1\n";
        var parser = Parser.CreateParser(new StringReader(Yaml));
        var reader = new EventReader(parser);

        reader.Expect<StreamStart>();
        reader.Expect<DocumentStart>();
        reader.Expect<MappingStart>();

        var depthAtRootMapping = reader.CurrentDepth;
        reader.Expect<Scalar>(); // 'a'
        reader.Expect<MappingStart>();
        reader.Expect<Scalar>(); // 'b'
        reader.Expect<MappingStart>();
        Assert.True(reader.CurrentDepth > depthAtRootMapping);

        reader.Skip(depthAtRootMapping);
        Assert.Equal(depthAtRootMapping, reader.CurrentDepth);

        // We should now be positioned to read the end of the root mapping/doc/stream.
        reader.Expect<MappingEnd>();
        reader.Expect<DocumentEnd>();
        reader.Expect<StreamEnd>();
    }

    [Fact]
    public void AcceptAtEndOfStream_ThrowsEndOfStreamException()
    {
        var parser = Parser.CreateParser(new StringReader(string.Empty));
        var reader = new EventReader(parser);

        reader.Expect<StreamStart>();
        reader.Expect<StreamEnd>();

        Assert.Throws<EndOfStreamException>(() => reader.Accept<StreamStart>());
    }
}
