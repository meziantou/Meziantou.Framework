namespace Meziantou.Framework.Tests;

public sealed class DataBinderTests
{
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
}
