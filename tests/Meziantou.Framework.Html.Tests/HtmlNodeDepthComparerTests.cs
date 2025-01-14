using FluentAssertions;
using Xunit;

namespace Meziantou.Framework.Html.Tests;

public class HtmlNodeDepthComparerTests
{
    [Fact]
    public void Compare_Equals()
    {
        var document = new HtmlDocument();
        document.LoadHtml("<p><span id='id1'>1</span><span id='id2'>2</span></p>");
        var element1 = document.SelectSingleNode("//span[@id='id1']");
        var element2 = document.SelectSingleNode("//span[@id='id2']");

        var comparer = new HtmlNodeDepthComparer();
        Assert.Equal(0, comparer.Compare(element1, element2));
    }

    [Fact]
    public void Compare_DirectionAscending_LessThan()
    {
        var document = new HtmlDocument();
        document.LoadHtml("<p><span id='id1'>1</span><p><span id='id2'>2</span></p></p>");
        var element1 = document.SelectSingleNode("//span[@id='id1']");
        var element2 = document.SelectSingleNode("//span[@id='id2']");

        var comparer = new HtmlNodeDepthComparer
        {
            Direction = ListSortDirection.Ascending,
        };
        Assert.Equal(-1, comparer.Compare(element1, element2));
        Assert.Equal(1, comparer.Compare(element2, element1));
    }

    [Fact]
    public void Compare_DirectionDescending_LessThan()
    {
        var document = new HtmlDocument();
        document.LoadHtml("<p><span id='id1'>1</span><p><span id='id2'>2</span></p></p>");
        var element1 = document.SelectSingleNode("//span[@id='id1']");
        var element2 = document.SelectSingleNode("//span[@id='id2']");

        var comparer = new HtmlNodeDepthComparer
        {
            Direction = ListSortDirection.Descending,
        };
        Assert.Equal(1, comparer.Compare(element1, element2));
        Assert.Equal(-1, comparer.Compare(element2, element1));
    }
}
