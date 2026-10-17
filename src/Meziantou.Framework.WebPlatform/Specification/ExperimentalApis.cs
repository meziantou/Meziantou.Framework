namespace Meziantou.Framework.WebPlatform;

/// <summary>
/// The diagnostic identifiers of the <see cref="System.Diagnostics.CodeAnalysis.ExperimentalAttribute"/> of the public APIs
/// that implement tentative features: proposals that no specification defines yet (pull requests, explainers, issues), whose
/// web-platform-tests are tentative. Each feature has its own identifier, documented in docs/experimental-apis.md.
/// </summary>
/// <remarks>The IDL that tools/Meziantou.Framework.WebPlatform.SpecGen/IdlModel.cs adds for the proposals names these constants (<c>ExperimentalApi</c>): SpecGen checks that the .NET types and members of this IDL have the matching attribute.</remarks>
internal static class ExperimentalApis
{
    /// <summary>The URL of the documentation of the diagnostics.</summary>
    public const string UrlFormat = "https://github.com/meziantou/Meziantou.Framework/blob/main/src/Meziantou.Framework.WebPlatform/docs/experimental-apis.md#{0}";

    /// <summary>The HTML insertion methods (https://github.com/whatwg/html/pull/12753): <c>appendHTML()</c>, <c>prependHTML()</c>, <c>beforeHTML()</c>, <c>afterHTML()</c>, <c>replaceWithHTML()</c> and their unsafe variants.</summary>
    public const string HtmlInsertionMethods = "WEBPLATEXP0001";

    /// <summary>The streaming HTML insertion methods (https://github.com/whatwg/html/pull/12758): <c>streamHTML()</c>, <c>streamAppendHTML()</c>...</summary>
    public const string StreamingHtmlInsertion = "WEBPLATEXP0002";

    /// <summary>The declarative partial updates (https://github.com/WICG/declarative-partial-updates/blob/main/fragment-include-explainer.md).</summary>
    public const string DeclarativePartialUpdates = "WEBPLATEXP0003";

    /// <summary>The @result and @private rules of CSS Mixins (https://github.com/w3c/csswg-drafts/issues/14243).</summary>
    public const string CssMixinResultAndPrivateRules = "WEBPLATEXP0004";

    /// <summary><c>Element.matchContainer()</c> and <c>ContainerQueryList</c> (https://github.com/w3c/csswg-drafts/pull/13551).</summary>
    public const string MatchContainer = "WEBPLATEXP0005";

    /// <summary>The class prefix selector of Selectors 5.</summary>
    public const string ClassPrefixSelector = "WEBPLATEXP0006";

    /// <summary>The proposals of the File System Standard (https://github.com/whatwg/fs/pull/9, https://github.com/whatwg/fs/pull/10, https://github.com/whatwg/fs/blob/main/proposals/MultipleReadersWriters.md).</summary>
    public const string FileSystemProposals = "WEBPLATEXP0007";

    /// <summary>The <c>pseudoTarget</c> of the UI events (https://github.com/w3c/uievents/pull/413).</summary>
    public const string UIEventPseudoTarget = "WEBPLATEXP0008";

    /// <summary><c>ShadowRoot.getSelection()</c>, <c>elementFromPoint()</c> and <c>elementsFromPoint()</c> (https://github.com/w3c/csswg-drafts/issues/556).</summary>
    public const string ShadowRootExtensions = "WEBPLATEXP0009";

    /// <summary>The <c>oncontentvisibilityautostatechange</c> event handler, which no IDL defines.</summary>
    public const string ContentVisibilityAutoStateChangeEventHandler = "WEBPLATEXP0010";

    /// <summary>The error stack accessor (https://github.com/tc39/proposal-error-stack-accessor).</summary>
    public const string ErrorStackAccessor = "WEBPLATEXP0011";
}
