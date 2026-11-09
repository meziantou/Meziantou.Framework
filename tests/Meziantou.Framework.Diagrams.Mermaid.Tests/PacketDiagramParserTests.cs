namespace Meziantou.Framework.Diagrams.Mermaid.Tests;

public sealed class PacketDiagramParserTests
{
    private static PacketDiagram Parse(string text) => (PacketDiagram)MermaidDocument.Parse(text).Diagram;

    [Fact]
    public void Fields()
    {
        #region Mermaid_PacketDiagram
        var diagram = (PacketDiagram)MermaidDocument.Parse("""
            packet
              title UDP packet
              0-15: "Source port"
              16-31: "Destination port"
              +16: "Length"
              +16: "Checksum"
              64-127: "Data (variable length)"
            """).Diagram;
        #endregion

        Assert.Equal("UDP packet", diagram.Title);
        Assert.Equal([(0, 15), (16, 31), (32, 47), (48, 63), (64, 127)], diagram.Fields.Select(field => (field.Start, field.End)));
        Assert.Equal("Length", diagram.Fields[2].Label.Text);
    }

    [Fact]
    public void ConfigurationAndBeta()
    {
        var diagram = Parse("""
            ---
            config:
              packet:
                bitsPerRow: 16
                bitWidth: 20
                rowHeight: 40
                showBits: false
                bitOrder: descending
            ---
            packet-beta
              accTitle: Accessible
              0: 'single \'bit\''
              1-7: "escaped \"quote\"\tand tab"
            """);
        Assert.Equal((16, 20.0, 40.0, false, true), (diagram.BitsPerRow, diagram.BitWidth, diagram.RowHeight, diagram.ShowBits, diagram.DescendingBitOrder));
        Assert.Equal("Accessible", diagram.AccessibleTitle);
        Assert.Equal("single 'bit'", diagram.Fields[0].Label.Text);
        Assert.Equal("escaped \"quote\"\tand tab", diagram.Fields[1].Label.Text);
    }

    [Theory]
    [InlineData("packet\n  1-2: \"x\"", "is not contiguous: it should start at 0.")]
    [InlineData("packet\n  0-15: \"x\"\n  15: \"y\"", "is not contiguous: it should start at 16.")]
    [InlineData("packet\n  5-2: \"x\"", "its end must be greater than its start.")]
    [InlineData("packet\n  +0: \"x\"", "a field cannot have zero bits.")]
    [InlineData("packet\n  0: x", "a quoted label is expected.")]
    [InlineData("packet\n  01: \"x\"", "it cannot start with 0.")]
    [InlineData("packet\n  0-99999999: \"x\"", "rows.")]
    [InlineData("packet\n  0 \"x\"", "':' is expected.")]
    public void InvalidPacketDiagrams(string text, string expected)
    {
        var exception = Assert.Throws<MermaidParseException>(() => MermaidDocument.Parse(text));
        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Write()
    {
        var diagram = new PacketDiagram { Title = "Header", Description = "The header", BitsPerRow = 8, ShowBits = false, DescendingBitOrder = true };
        diagram.AddField(1, "Flag \"F\"");
        diagram.AddField(7, "Line\nbreak");
        diagram.AddField(24, "Payload");

        var text = MermaidWriter.Write(diagram);
        var parsed = MermaidDocument.Parse(text);
        Assert.Equal(text, MermaidWriter.Write(parsed.Diagram));
        var result = (PacketDiagram)parsed.Diagram;
        Assert.Equal(("Header", "The header", 8, false, true), (result.Title, result.Description, result.BitsPerRow, result.ShowBits, result.DescendingBitOrder));
        Assert.Equal([(0, 0, "Flag \"F\""), (1, 7, "Line\nbreak"), (8, 31, "Payload")], result.Fields.Select(field => (field.Start, field.End, field.Label.Text)));
    }

    [Fact]
    public void WriteRejectsGaps()
    {
        var diagram = new PacketDiagram();
        diagram.Fields.Add(new PacketField(4, 7, "x"));
        Assert.Throws<ArgumentException>("diagram", () => MermaidWriter.Write(diagram));
    }
}
