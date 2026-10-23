using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Css.Om;

namespace Meziantou.Framework.WebPlatform.Dom;

public partial class Document
{
    /// <summary>The printing of the document in progress (see <see cref="BeginPrint"/>), or null.</summary>
    internal PrintSession? PrintSession { get; private set; }

    /// <summary>
    /// Starts the printing of the document (https://drafts.csswg.org/css-page-3/): until the session is disposed, the styles of
    /// the document are computed with the <c>print</c> media type by another style engine, and its layout is the paginated
    /// layout of another layout engine (see <see cref="Css.Layout.PrintSession"/>). The engines of the screen are kept as they
    /// are: the document is not styled nor laid out again when the printing ends, and no transition starts.
    /// </summary>
    /// <exception cref="InvalidOperationException">The document is already being printed.</exception>
    internal PrintSession BeginPrint(PrintOptions options)
    {
        if (PrintSession is not null)
            throw new InvalidOperationException("The document is already being printed.");

        var session = new PrintSession(this, options, _styleEngine, _layoutEngine);
        PrintSession = session;
        session.Start();
        return session;
    }

    /// <summary>Replaces the style and layout engines of the document (the ones of the printing, or the screen ones when it ends).</summary>
    internal void SetEngines(StyleEngine? styleEngine, LayoutEngine? layoutEngine)
    {
        _styleEngine = styleEngine;
        _layoutEngine = layoutEngine;
    }

    /// <summary>Ends the printing of the document: the engines of the screen are used again.</summary>
    internal void EndPrint(PrintSession session, StyleEngine? styleEngine, LayoutEngine? layoutEngine)
    {
        if (!ReferenceEquals(PrintSession, session))
            return;

        PrintSession = null;
        SetEngines(styleEngine, layoutEngine);
    }

    /// <summary>Fetches the images and the font faces used by the styles of the printing (see <see cref="Css.Layout.PrintSession.FetchResources"/>).</summary>
    internal void UpdatePrintResources()
    {
        if (!IsFullyActive || BrowsingContext is null)
            return;

        CssImageFetching.FetchUsedImages(this, _fetchedCssImages ??= []);

        // https://html.spec.whatwg.org/multipage/iframe-embed-object.html#the-object-element: the object elements that only the
        // styles of the printing render load their resource (the tasks of the event loop run before the pages are painted)
        foreach (var node in Descendants())
        {
            if (node is HtmlObjectElement obj)
            {
                obj.UpdateIfRendered();
            }
        }
        if (_fonts is not null || ReadyState == DocumentReadyState.Complete)
        {
            Fonts.OnRenderingUpdate();
        }
    }

    /// <summary>Fetches the images used by the styles of the page contexts and of the page-margin boxes of the printing.</summary>
    internal void UpdatePrintPageResources(IEnumerable<ComputedStyle> styles)
    {
        if (!IsFullyActive || BrowsingContext is null)
            return;

        CssImageFetching.FetchImages(this, styles, _fetchedCssImages ??= []);
    }

    /// <summary>The environment of the screen, which the environment of the printing is derived from.</summary>
    internal MediaEnvironment GetScreenEnvironment() => StyleEngine.CreateEnvironment(this);
}
