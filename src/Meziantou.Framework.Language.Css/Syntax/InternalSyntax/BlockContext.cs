namespace Meziantou.Framework.Language.Css.Syntax.InternalSyntax;

/// <summary>Where a list of statements is, which decides how its rules are read and what it may hold.</summary>
internal enum BlockContext
{
    /// <summary>The top level of a style sheet: rules, but no declarations.</summary>
    TopLevel,

    /// <summary>The block of a conditional group rule at the top level, such as <c>@media</c>: rules, but no declarations.</summary>
    Group,

    /// <summary>The block of a top-level <c>@scope</c> rule: declarations, and rules whose selectors may be relative.</summary>
    Scope,

    /// <summary>The block of a style rule, or of a group rule nested in one: declarations, nested style rules, and nested group rules.</summary>
    StyleRule,

    /// <summary>The block of <c>@keyframes</c>: keyframe rules only.</summary>
    Keyframes,

    /// <summary>A block that holds declarations only, such as the block of <c>@font-face</c> or of a keyframe rule.</summary>
    DeclarationsOnly,

    /// <summary>The block of <c>@page</c>: declarations and margin rules.</summary>
    Page,

    /// <summary>The block of <c>@font-feature-values</c>: declarations and feature blocks.</summary>
    FontFeatureValues,

    /// <summary>The block of <c>@function</c>: declarations and conditional group rules.</summary>
    Function,

    /// <summary>The block of an at-rule this parser does not know: anything, checked for nothing.</summary>
    Unknown,
}
