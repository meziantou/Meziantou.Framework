namespace Meziantou.Framework.Diagrams.Tests;

public sealed class PacketDiagramTests
{
    [Fact]
    public void PacketDiagram()
    {
        #region Diagrams_PacketDiagram
        var diagram = new PacketDiagram { Title = "UDP packet" };
        diagram.AddField(16, "Source port");
        diagram.AddField(16, "Destination port");
        diagram.AddField(16, "Length");
        diagram.AddField(16, "Checksum");
        diagram.AddField(64, "Data (variable length)");
        #endregion

        Assert.Equal([0, 16, 32, 48, 64], diagram.Fields.Select(field => field.Start));
        Assert.Equal(127, diagram.Fields[^1].End);
        DiagramAssert.Svg(diagram, "PacketDiagram");
    }

    [Fact]
    public void OptionsAndSplitFields()
    {
        var diagram = new PacketDiagram { BitsPerRow = 16, BitWidth = 24, RowHeight = 40, DescendingBitOrder = true, Theme = DiagramTheme.Dark };
        diagram.AddField(4, "Version");
        diagram.AddField(20, "Field across two rows");
        diagram.AddField(1, "F");
        DiagramAssert.Svg(diagram);
    }

    [Fact]
    public void WithoutBits()
    {
        var diagram = new PacketDiagram { ShowBits = false };
        diagram.AddField(8, "Type");
        diagram.AddField(8, "Code");
        DiagramAssert.Svg(diagram);
    }

    [Fact]
    public void FieldsAreValid()
    {
        Assert.Throws<ArgumentOutOfRangeException>("bits", () => new PacketDiagram().AddField(0, "x"));
        Assert.Throws<ArgumentOutOfRangeException>("end", () => new PacketField(4, 3, "x"));
        Assert.Throws<ArgumentOutOfRangeException>("start", () => new PacketField(-1, 3, "x"));
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new PacketDiagram { BitsPerRow = 0 });
    }
}
