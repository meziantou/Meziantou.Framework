using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Syntax.Inlines;

namespace Meziantou.Framework.Markdown.Extensions.GenericAttributes;

/// <summary>
/// With trivia, the attributes <c>{...}</c> as written in the text, so that the roundtrip renderer writes them back. The other
/// renderers write nothing for it: the attributes are attached to another object. It is an empty container, so that the inline
/// parsing does not attach the next attributes to it.
/// </summary>
internal sealed class GenericAttributesInline : ContainerInline
{
    public StringSlice SourceText { get; set; }
}
