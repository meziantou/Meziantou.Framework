using System.Collections.Generic;
using Bunit;
using Xunit;

namespace Meziantou.AspNetCore.Components.Tests
{
    public sealed class RepeaterTests
    {
        [Fact]
        public void NullDataShowLoadingIndicator()
        {
            using var ctx = new TestContext();
            var cut = ctx.RenderComponent<Repeater<string>>(parameter => parameter
                .Add(p => p.Items, value: null)
                .Add(p => p.LoadingTemplate, "<p>Loading...</p>")
                .Add(p => p.EmptyTemplate, "<p>empty</p>"));

            cut.MarkupMatches("<p>Loading...</p>");
        }

        [Fact]
        public void EmptyTemplate()
        {
            using var ctx = new TestContext();
            var cut = ctx.RenderComponent<Repeater<string>>(parameter => parameter
                .Add(p => p.Items, value: new List<string>())
                .Add(p => p.LoadingTemplate, "<p>loading...</p>")
                .Add(p => p.EmptyTemplate, "<p>empty</p>"));

            cut.MarkupMatches("<p>empty</p>");
        }

        [Fact]
        public void ItemTemplate()
        {
            using var ctx = new TestContext();
            var cut = ctx.RenderComponent<Repeater<string>>(parameter => parameter
                .Add(p => p.Items, value: new List<string>() { "a", "b" })
                .Add(p => p.EmptyTemplate, "<p>empty</p>")
                .Add(p => p.ItemTemplate, item => $"{item}"));

            cut.MarkupMatches("ab");
        }

        [Fact]
        public void ItemTemplateAndSeparatorTemplate()
        {
            using var ctx = new TestContext();
            var cut = ctx.RenderComponent<Repeater<string>>(parameter => parameter
                .Add(p => p.Items, value: new List<string>() { "a", "b" })
                .Add(p => p.EmptyTemplate, "<p>empty</p>")
                .Add(p => p.ItemTemplate, item => $"{item}")
                .Add(p => p.ItemSeparatorTemplate, ","));

            cut.MarkupMatches("a,b");
        }

        [Fact]
        public void ContainerTemplate()
        {
            using var ctx = new TestContext();
            var cut = ctx.RenderComponent<Repeater<string>>(parameter => parameter
                .Add(p => p.Items, value: new List<string>() { "a", "b" })
                .Add(p => p.EmptyTemplate, "<p>empty</p>")
                .Add(p => p.ItemTemplate, item => $"{item}")
                .Add(p => p.RepeaterContainerTemplate, itemsTemplate => builder =>
                {
                    builder.OpenElement(0, "div");
                    builder.AddContent(1, itemsTemplate);
                    builder.CloseElement();
                }));

            cut.MarkupMatches("<div>ab</div>");
        }
    }
}
