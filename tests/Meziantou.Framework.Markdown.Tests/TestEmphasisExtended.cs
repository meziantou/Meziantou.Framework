using System.Diagnostics;

using Meziantou.Framework.Markdown.Parsers.Inlines;
using Meziantou.Framework.Markdown.Renderers;
using Meziantou.Framework.Markdown.Renderers.Html;
using Meziantou.Framework.Markdown.Syntax.Inlines;

namespace Meziantou.Framework.Markdown.Tests;

public class TestEmphasisExtended
{
    private sealed class EmphasisTestExtension : IMarkdownExtension
    {
        public void Setup(MarkdownPipelineBuilder pipeline)
        {
            var emphasisParser = pipeline.InlineParsers.Find<EmphasisInlineParser>();
            Debug.Assert(emphasisParser is not null);

            foreach (var emphasis in EmphasisTestDescriptors)
            {
                emphasisParser.EmphasisDescriptors.Add(
                    new EmphasisDescriptor(emphasis.Character, emphasis.Minimum, emphasis.Maximum, true));
            }
            emphasisParser.TryCreateEmphasisInlineList.Add((delimiterChar, delimiterCount) =>
            {
                return delimiterChar is '*' or '_'
                    ? null
                    : new CustomEmphasisInline() { DelimiterChar = delimiterChar, DelimiterCount = delimiterCount };
            });
        }

        public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
        {
            renderer.ObjectRenderers.Insert(0, new EmphasisRenderer());
        }

        private sealed class EmphasisRenderer : HtmlObjectRenderer<CustomEmphasisInline>
        {
            protected override void Write(HtmlRenderer renderer, CustomEmphasisInline obj)
            {
                var tag = EmphasisTestDescriptors.First(test => test.Character == obj.DelimiterChar).Tags[obj.DelimiterCount];

                renderer.Write(tag.OpeningTag);
                renderer.WriteChildren(obj);
                renderer.Write(tag.ClosingTag);
            }
        }
    }
    private sealed class Tag
    {
#pragma warning disable CS0649
        public int Level;
#pragma warning restore CS0649
        public string RawTag;
        public string OpeningTag;
        public string ClosingTag;

        public Tag(string tag)
        {
            RawTag = tag;
            OpeningTag = "<" + tag + ">";
            ClosingTag = "</" + tag + ">";
        }

        public static implicit operator Tag(string tag)
            => new Tag(tag);
    }
    private sealed class EmphasisTestDescriptor
    {
        public char Character;
        public int Minimum;
        public int Maximum;
        public Dictionary<int, Tag> Tags = new Dictionary<int, Tag>();

        private EmphasisTestDescriptor(char character, int min, int max)
        {
            Character = character;
            Minimum = min;
            Maximum = max;
        }
        public EmphasisTestDescriptor(char character, int min, int max, params Tag[] tags)
            : this(character, min, max)
        {
            Debug.Assert(tags.Length == max - min + 1);
            foreach (var tag in tags)
            {
                Tags.Add(min++, tag);
            }
        }
        public EmphasisTestDescriptor(char character, int min, int max, string tag)
            : this(character, min, max, new Tag(tag)) { }
    }
    private sealed class CustomEmphasisInline : EmphasisInline { }
    private static readonly EmphasisTestDescriptor[] EmphasisTestDescriptors = new[]
    {
        //                            Min Max
        new EmphasisTestDescriptor('"', 1, 1, "quotation"),
        new EmphasisTestDescriptor(',', 1, 2, "comma", "extra-comma"),
        new EmphasisTestDescriptor('!', 2, 3, "warning", "error"),
        new EmphasisTestDescriptor('=', 1, 3, "equal", "really-equal", "congruent"),
        new EmphasisTestDescriptor('1', 1, 1, "one-only"),
        new EmphasisTestDescriptor('2', 2, 2, "two-only"),
        new EmphasisTestDescriptor('3', 3, 3, "three-only"),
    };

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().Use<EmphasisTestExtension>().Build();

    [Theory]
    [InlineData("*foo**",         "<em>foo</em>*")]
    [InlineData("**foo*",         "*<em>foo</em>")]
    [InlineData("***foo***",      "<em><strong>foo</strong></em>")]
    [InlineData("**_foo_**",      "<strong><em>foo</em></strong>")]
    [InlineData("_**foo**_",      "<em><strong>foo</strong></em>")]
    [InlineData("\"foo\"",        "<quotation>foo</quotation>")]
    [InlineData("\"\"foo\"\"",    "<quotation><quotation>foo</quotation></quotation>")]
    [InlineData("\"foo\"\"",      "<quotation>foo</quotation>&quot;")]
    [InlineData("\"\"foo\"",      "&quot;<quotation>foo</quotation>")]
    [InlineData(", foo",          ", foo")]
    [InlineData(", foo,",         ", foo,")]
    [InlineData(",some, foo,",    "<comma>some</comma> foo,")]
    [InlineData(",,foo,,",        "<extra-comma>foo</extra-comma>")]
    [InlineData(",foo,,",         "<comma>foo</comma>,")]
    [InlineData(",,,foo,,,",      "<comma><extra-comma>foo</extra-comma></comma>")]
    [InlineData("*foo*&_foo_",     "<em>foo</em>&amp;<em>foo</em>")]
    [InlineData("!1!",            "!1!")]
    [InlineData("!!2!!",          "<warning>2</warning>")]
    [InlineData("!!!3!!!",        "<error>3</error>")]
    [InlineData("!!!34!!!!",      "<error>34</error>!")]
    [InlineData("!!!!43!!!",      "!<error>43</error>")]
    [InlineData("!!!!44!!!!",     "!<error>44!</error>")] // This is a new case - should the second ! be before or after </error>?
    [InlineData("!!!!!5!!!!!",    "<warning><error>5</error></warning>")]
    [InlineData("!!!!!!6!!!!!!",  "<error><error>6</error></error>")]
    [InlineData("!! !mixed!!!",   "!! !mixed!!!")] // can't open the delimiter because of the whitespace
    [InlineData("=",              "=")]
    [InlineData("==",             "==")]
    [InlineData("====",           "====")]
    [InlineData("=a",             "=a")]
    [InlineData("=a=",            "<equal>a</equal>")]
    [InlineData("==a=",           "=<equal>a</equal>")]
    [InlineData("==a==",          "<really-equal>a</really-equal>")]
    [InlineData("==a===",         "<really-equal>a</really-equal>=")]
    [InlineData("===a===",        "<congruent>a</congruent>")]
    [InlineData("====a====",      "<equal><congruent>a</congruent></equal>")]
    [InlineData("=====a=====",    "<really-equal><congruent>a</congruent></really-equal>")]
    [InlineData("1",              "1")]
    [InlineData("1 1",            "1 1")]
    [InlineData("1Foo1",          "<one-only>Foo</one-only>")]
    [InlineData("1121",           "1<one-only>2</one-only>")]
    [InlineData("22322",          "<two-only>3</two-only>")]
    [InlineData("2223222",        "2<two-only>32</two-only>")]
    [InlineData("22223222",       "22<two-only>32</two-only>")]
    [InlineData("22223223222",    "22223<two-only>3</two-only>2")]
    [InlineData("2232",           "2232")]
    [InlineData("333",            "333")]
    [InlineData("3334333",        "<three-only>4</three-only>")]
    [InlineData("33334333",       "3<three-only>4</three-only>")]
    [InlineData("33343333",       "<three-only>4</three-only>3")]
    [InlineData("122122",         "<one-only>22</one-only>22")]
    [InlineData("221221",         "<two-only>1</two-only>1")]
    [InlineData("122foo221",      "<one-only><two-only>foo</two-only></one-only>")]
    [InlineData("122foo122",      "<one-only>22foo</one-only>22")]
    [InlineData("!!!!!Attention:!! \"==1+1== 2\",but ===333 and 222===, mod 111!!!",
        "<error><warning>Attention:</warning> <quotation><really-equal><one-only>+</one-only></really-equal> 2</quotation><comma>but <congruent>333 and 222</congruent></comma> mod 111</error>")]
    public void TestEmphasis(string markdown, string expectedHtml)
    {
        TestParser.TestSpec(markdown, "<p>" + expectedHtml + "</p>", Pipeline);
    }
}
