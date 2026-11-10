namespace Meziantou.Framework.Diagrams.Tests;

public sealed class EventModelingDiagramTests
{
    [Fact]
    public void EventModeling()
    {
        #region Diagrams_EventModeling
        var diagram = new EventModelingDiagram();
        var items = new EventModelingDataBlock("Item", new EventModelingData("description: string\nprice: number"));
        diagram.DataBlocks.Add(items);
        diagram.AddFrame("01", EventModelingEntityKind.Ui, "CartUI");
        diagram.AddFrame("02", EventModelingEntityKind.Command, "AddItem").DataBlock = items;
        var added = diagram.AddFrame("03", EventModelingEntityKind.Event, "ItemAdded");
        added.DataBlock = items;

        // A reset frame infers no relation from the frame before it
        var external = diagram.AddFrame("04", EventModelingEntityKind.Event, "Inventory.InventoryChanged");
        external.IsReset = true;
        diagram.AddFrame("05", EventModelingEntityKind.Processor, "InventoryProcessor");
        diagram.AddFrame("06", EventModelingEntityKind.Command, "ChangeInventory");

        // A read model built from several events
        var cart = diagram.AddFrame("07", EventModelingEntityKind.ReadModel, "CartItems");
        cart.IsReset = true;
        cart.Sources.Add(added);
        cart.Sources.Add(external);
        #endregion

        Assert.Same(cart, diagram.Find("07"));
        DiagramAssert.Svg(diagram, "EventModeling");
    }

    [Fact]
    public void SwimlanesPerNamespaceAndInlineData()
    {
        var diagram = new EventModelingDiagram { Theme = DiagramTheme.Dark };
        diagram.AddFrame("1", EventModelingEntityKind.Event, "Cart.Created").Data = new EventModelingData("id: string", EventModelingDataFormat.Json);
        diagram.AddFrame("2", EventModelingEntityKind.Event, "Inventory.Changed");
        diagram.AddFrame("3", EventModelingEntityKind.Event, "Cart.Cleared");
        diagram.AddFrame("4", EventModelingEntityKind.ReadModel, "A.B.C");
        diagram.AddFrame("5", EventModelingEntityKind.Ui, "Screen.Main").Data = new EventModelingData(new string('x', 600));
        var svg = diagram.ToSvg();
        Assert.Contains("Stream: Cart", svg, StringComparison.Ordinal);
        Assert.Contains("Stream: Inventory", svg, StringComparison.Ordinal);
        Assert.Contains("UI/A: Screen", svg, StringComparison.Ordinal);
        Assert.Contains(">A.B.C<", svg, StringComparison.Ordinal);
        Assert.HasCount(2, svg.Split("Stream: Cart"));
        DiagramAssert.Svg(svg);
    }

    [Fact]
    public void FramesNeedAnIdentifierAndAnEntity()
    {
        Assert.Throws<ArgumentException>("id", () => new EventModelingFrame(string.Empty, EventModelingEntityKind.Event, "A"));
        Assert.Throws<ArgumentException>("entity", () => new EventModelingFrame("1", EventModelingEntityKind.Event, string.Empty));
    }
}
