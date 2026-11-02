namespace Meziantou.Framework.Tests;

public sealed class DataBinderTests
{
    [Fact]
    public void Documentation_Eval()
    {
        #region DataBinder_Eval
        var order = new
        {
            Number = 42,
            Customer = new { Name = "Ada" },
            Lines = new[] { "Keyboard", "Mouse" },
            Settings = new Dictionary<string, string>(StringComparer.Ordinal) { ["theme"] = "dark" },
        };

        // Properties are separated by dots, and names are case-insensitive
        Assert.Equal("Ada", DataBinder.Eval(order, "Customer.Name"));
        Assert.Equal("Ada", DataBinder.Eval(order, "customer.name"));

        // Indexes use [] or (): a number for arrays and lists, a quoted string for other indexers
        Assert.Equal("Mouse", DataBinder.Eval(order, "Lines[1]"));
        Assert.Equal("dark", DataBinder.Eval(order, "Settings['theme']"));

        // The overload with a format applies a composite format string to the value
        Assert.Equal("Order #42", DataBinder.Eval(order, "Number", "Order #{0}"));
        #endregion
    }

    [Fact]
    public void BasicExpression()
    {
        var obj = new { A = "test" };
        var actual = DataBinder.Eval(obj, "A");
        Assert.Equal("test", actual);
    }

    [Fact]
    public void NestedExpression()
    {
        var obj = new { A = new { B = new[] { "a", "b", "c" } } };
        var actual = DataBinder.Eval(obj, "A.B[1]");
        Assert.Equal("b", actual);
    }

    [Fact]
    public void MissingProperty()
    {
        var obj = new { A = "test" };
        Assert.Throws<ArgumentException>(() => DataBinder.Eval(obj, "B"));
    }

    [Fact]
    public void NonIndexableObjectg()
    {
        var obj = new { A = 0 };
        Assert.Throws<ArgumentException>(() => DataBinder.Eval(obj, "A[0]"));
    }

    [Fact]
    public void QuotedIndexContainingDotsAndBrackets()
    {
        var obj = new
        {
            Items = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["a.b"] = "dot",
                ["a]b"] = "bracket",
                ["x"] = "quote",
            },
        };

        Assert.Equal("dot", DataBinder.Eval(obj, "Items[\"a.b\"]"));
        Assert.Equal("dot", DataBinder.Eval(obj, "Items['a.b']"));
        Assert.Equal("bracket", DataBinder.Eval(obj, "Items[\"a]b\"]"));
        Assert.Equal(3, DataBinder.Eval(obj, "Items[\"a.b\"].Length"));
        Assert.Equal("quote", DataBinder.Eval(obj, "Items[ 'x' ]"));
    }

    [Theory]
    [InlineData("Items[\"]")]
    [InlineData("Items[']")]
    [InlineData("Items[\"a]")]
    public void UnterminatedQuotedIndex(string expression)
    {
        var obj = new { Items = new Dictionary<string, string>(StringComparer.Ordinal) };
        Assert.Throws<ArgumentException>(() => DataBinder.Eval(obj, expression));
    }

    [Fact]
    public void IndexContainingDot()
    {
        var obj = new { Items = new Dictionary<string, int>(StringComparer.Ordinal) { ["1.5"] = 2 } };
        Assert.Equal(2, DataBinder.Eval(obj, "Items[1.5]"));
    }
}
