using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.Cascade;

/// <summary>
/// The application of a mixin by an @apply rule (https://drafts.csswg.org/css-mixins-1/#apply-rule): the rules of the
/// @result rules of the mixin replace the @apply rule, and the values of their declarations are substituted in the frame of
/// the application (see <see cref="CssFunctionFrame.CreateMixin"/>), evaluated for each element.
/// </summary>
internal sealed class CssMixinApplication
{
    /// <summary>The applied mixin.</summary>
    public required CssMixinRule Mixin { get; init; }

    /// <summary>The root of the tree of the @mixin rule: the tree-scoped references of its body are looked up from it.</summary>
    public required Node DefinitionScope { get; init; }

    /// <summary>The arguments of the @apply rule (substituted in the calling context).</summary>
    public required List<string> Arguments { get; init; }

    /// <summary>The root of the tree of the style sheet of the @apply rule (the calling context of an application that has no <see cref="Parent"/>).</summary>
    public required Node? TreeScope { get; init; }

    /// <summary>The application of the mixin whose @result rule contains the @apply rule (the calling context), or null.</summary>
    public CssMixinApplication? Parent { get; init; }

    /// <summary>The rules of the contents block of the @apply rule, or null when it has no contents block.</summary>
    public List<CssRule>? Contents { get; init; }

    /// <summary>The style sheet of the @apply rule (its contents block is parsed again in it).</summary>
    public CssStyleSheet? ContentsSheet { get; init; }
}
