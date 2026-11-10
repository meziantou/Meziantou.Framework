namespace Meziantou.Framework.Diagrams.Tests;

public sealed class IshikawaDiagramTests
{
    [Fact]
    public void IshikawaDiagram()
    {
        #region Diagrams_IshikawaDiagram
        var diagram = new IshikawaDiagram { Effect = "Blurry photo" };
        var process = diagram.AddCause("Process");
        process.AddCause("Out of focus");
        process.AddCause("Shutter speed too slow");
        diagram.AddCause("User").AddCause("Shaky hands");
        var equipment = diagram.AddCause("Equipment");
        var lens = equipment.AddCause("Lens");
        lens.AddCause("Damaged lens");
        lens.AddCause("Dirty lens");
        equipment.AddCause("Sensor").AddCause("Dirty sensor");
        var environment = diagram.AddCause("Environment");
        environment.AddCause("Subject moved too quickly");
        environment.AddCause("Too dark");
        #endregion

        Assert.Equal(["Process", "Out of focus", "Shutter speed too slow", "User", "Shaky hands", "Equipment", "Lens", "Damaged lens", "Dirty lens", "Sensor", "Dirty sensor", "Environment", "Subject moved too quickly", "Too dark"], diagram.EnumerateCauses().Select(cause => cause.Label.Text));
        DiagramAssert.Svg(diagram, "IshikawaDiagram");
    }

    [Fact]
    public void DeepCausesAndLongTexts()
    {
        var diagram = new IshikawaDiagram { Theme = DiagramTheme.Dark, Title = "Late delivery", Effect = "The order is delivered several days late\nto the customer" };
        var supplier = diagram.AddCause("Supplier");
        var stock = supplier.AddCause("Out of stock");
        var forecast = stock.AddCause("Wrong forecast of the demand for the season");
        forecast.AddCause("Old data").AddCause("No export since the migration");
        diagram.AddCause("Carrier");
        diagram.AddCause("Warehouse\nsecond line").AddCause("Short staffed");
        DiagramAssert.Svg(diagram);
    }

    [Fact]
    public void EffectOnly()
    {
        DiagramAssert.Svg(new IshikawaDiagram { Effect = "Effect" });
    }

    [Fact]
    public void EmptyDiagram()
    {
        DiagramAssert.Svg(new IshikawaDiagram());
    }

    [Fact]
    public void LabelIsRequired()
    {
        Assert.Throws<ArgumentNullException>("label", () => new IshikawaCause(null!));
        var cause = new IshikawaCause("Cause");
        Assert.Throws<ArgumentNullException>("value", () => cause.Label = null!);
        Assert.Equal("Cause", cause.ToString());
    }

    [Fact]
    public void EnumerateCausesVisitsSharedCausesOnce()
    {
        var diagram = new IshikawaDiagram { Effect = "Effect" };
        var shared = new IshikawaCause("Shared");
        diagram.AddCause("A").Causes.Add(shared);
        diagram.AddCause("B").Causes.Add(shared);
        Assert.Equal(["A", "Shared", "B"], diagram.EnumerateCauses().Select(cause => cause.Label.Text));
    }
}
