using static Meziantou.Framework.Markdown.Tests.TestRoundtrip;

namespace Meziantou.Framework.Markdown.Tests.RoundtripSpecs.Inlines;

public class TestEmphasisInline
{
    [Theory]
    [InlineData("_t_")]
    [InlineData("_t_t")]
    [InlineData("t_t_")]
    [InlineData("_t t_")]
    [InlineData("_t\tt_")]
    [InlineData("*t*")]
    [InlineData("t*t*")]
    [InlineData("*t*t")]
    [InlineData("*t t*")]
    [InlineData("*t\tt*")]

    [InlineData(" _t_")]
    [InlineData(" _t_t")]
    [InlineData(" t_t_")]
    [InlineData(" _t t_")]
    [InlineData(" _t\tt_")]
    [InlineData(" *t*")]
    [InlineData(" t*t*")]
    [InlineData(" *t*t")]
    [InlineData(" *t t*")]
    [InlineData(" *t\tt*")]

    [InlineData("_t_t ")]
    [InlineData("t_t_ ")]
    [InlineData("_t t_ ")]
    [InlineData("_t\tt_ ")]
    [InlineData("*t* ")]
    [InlineData("t*t* ")]
    [InlineData("*t*t ")]
    [InlineData("*t t* ")]
    [InlineData("*t\tt* ")]

    [InlineData(" _t_t ")]
    [InlineData(" t_t_ ")]
    [InlineData(" _t t_ ")]
    [InlineData(" _t\tt_ ")]
    [InlineData(" *t* ")]
    [InlineData(" t*t* ")]
    [InlineData(" *t*t ")]
    [InlineData(" *t t* ")]
    [InlineData(" *t\tt* ")]

    [InlineData("_t_\t")]
    [InlineData("_t_t\t")]
    [InlineData("t_t_\t")]
    [InlineData("_t t_\t")]
    [InlineData("_t\tt_\t")]
    [InlineData("*t*\t")]
    [InlineData("t*t*\t")]
    [InlineData("*t*t\t")]
    [InlineData("*t t*\t")]
    [InlineData("*t\tt*\t")]
    public void Test_Emphasis(string value)
    {
        RoundTrip(value);
    }

    [Theory]
    [InlineData("__t__")]
    [InlineData("__t__t")]
    [InlineData("t__t__")]
    [InlineData("__t t__")]
    [InlineData("__t\tt__")]
    [InlineData("**t**")]
    [InlineData("**t**t")]
    [InlineData("t**t**")]
    [InlineData("**t\tt**")]

    [InlineData(" __t__")]
    [InlineData(" __t__t")]
    [InlineData(" t__t__")]
    [InlineData(" __t t__")]
    [InlineData(" __t\tt__")]
    [InlineData(" **t**")]
    [InlineData(" **t**t")]
    [InlineData(" t**t**")]
    [InlineData(" **t\tt**")]

    [InlineData("__t__ ")]
    [InlineData("__t__t ")]
    [InlineData("t__t__ ")]
    [InlineData("__t t__ ")]
    [InlineData("__t\tt__ ")]
    [InlineData("**t** ")]
    [InlineData("**t**t ")]
    [InlineData("t**t** ")]
    [InlineData("**t\tt** ")]

    [InlineData(" __t__ ")]
    [InlineData(" __t__t ")]
    [InlineData(" t__t__ ")]
    [InlineData(" __t t__ ")]
    [InlineData(" __t\tt__ ")]
    [InlineData(" **t** ")]
    [InlineData(" **t** t")]
    [InlineData(" t**t** ")]
    [InlineData(" **t\tt** ")]

    [InlineData("__t__\t")]
    [InlineData("__t__t\t")]
    [InlineData("t__t__\t ")]
    [InlineData("__t t__\t ")]
    [InlineData("__t\tt__\t ")]
    [InlineData("**t**\t ")]
    [InlineData("**t**t\t ")]
    [InlineData("t**t**\t ")]
    [InlineData("**t\tt**\t ")]

    [InlineData(" __t__\t ")]
    [InlineData(" __t__t\t ")]
    [InlineData(" t__t__\t ")]
    [InlineData(" __t t__\t ")]
    [InlineData(" __t\tt__\t ")]
    [InlineData(" **t**\t ")]
    [InlineData(" **t**\t t")]
    [InlineData(" t**t**\t ")]
    [InlineData(" **t\tt**\t ")]
    public void Test_StrongEmphasis(string value)
    {
        RoundTrip(value);
    }
}
