namespace Meziantou.Framework.WebPlatform.Dom;

public partial class Document
{
    private PaintWorklet? _paintWorklet;

    /// <summary>
    /// Gets the paint worklet of the document (https://drafts.css-houdini.org/css-paint-api-1/#dom-css-paintworklet,
    /// <c>CSS.paintWorklet</c>): the scripts added to it register the classes that paint the <c>paint()</c> images of the
    /// document. The scripts need a script engine that supports the module scripts, and the images a rendering backend.
    /// </summary>
    [NotInIdl("CSS.paintWorklet returns the paint worklet of the document of the current global object")]
    public Worklet PaintWorklet => PaintWorkletCore;

    /// <summary>The paint worklet of the document, created on first use.</summary>
    internal PaintWorklet PaintWorkletCore => _paintWorklet ??= new PaintWorklet(this);

    /// <summary>The paint worklet of the document, or null when it was not used.</summary>
    internal PaintWorklet? PaintWorkletOrNull => _paintWorklet;

    private LayoutWorklet? _layoutWorklet;
    private AnimationWorklet? _animationWorklet;

    /// <summary>
    /// Gets the layout worklet of the document (https://drafts.css-houdini.org/css-layout-api-1/#dom-css-layoutworklet,
    /// <c>CSS.layoutWorklet</c>): the scripts added to it register the classes that lay out the <c>display: layout()</c>
    /// boxes of the document. The scripts need a script engine that supports the module scripts.
    /// </summary>
    [NotInIdl("CSS.layoutWorklet returns the layout worklet of the document of the current global object")]
    public Worklet LayoutWorklet => LayoutWorkletCore;

    /// <summary>The layout worklet of the document, created on first use.</summary>
    internal LayoutWorklet LayoutWorkletCore => _layoutWorklet ??= new LayoutWorklet(this);

    /// <summary>The layout worklet of the document, or null when it was not used.</summary>
    internal LayoutWorklet? LayoutWorkletOrNull => _layoutWorklet;

    /// <summary>
    /// Gets the animation worklet of the document (https://drafts.css-houdini.org/css-animation-worklet-1/#dom-css-animationworklet,
    /// <c>CSS.animationWorklet</c>): the scripts added to it register the animators that drive the <see cref="Animations.WorkletAnimation"/>
    /// objects of the document. The scripts need a script engine that supports the module scripts.
    /// </summary>
    [NotInIdl("CSS.animationWorklet returns the animation worklet of the document of the current global object")]
    public Worklet AnimationWorklet => AnimationWorkletCore;

    /// <summary>The animation worklet of the document, created on first use.</summary>
    internal AnimationWorklet AnimationWorkletCore => _animationWorklet ??= new AnimationWorklet(this);

    /// <summary>The animation worklet of the document, or null when it was not used.</summary>
    internal AnimationWorklet? AnimationWorkletOrNull => _animationWorklet;

    // The audio worklets of the audio contexts of the window of the document
    private List<Worklet>? _audioWorklets;

    // The realtime audio contexts of the window of the document, closed with the document
    private List<WeakReference<WebAudio.AudioContext>>? _audioContexts;

    /// <summary>Registers the audio worklet of an audio context, terminated with the document.</summary>
    internal void RegisterAudioWorklet(Worklet worklet) => (_audioWorklets ??= []).Add(worklet);

    /// <summary>Registers a realtime audio context, closed when the document is destroyed.</summary>
    internal void RegisterAudioContext(WebAudio.AudioContext context)
    {
        var contexts = _audioContexts ??= [];
        if (contexts.Count >= 16 && (contexts.Count & (contexts.Count - 1)) == 0)
        {
            contexts.RemoveAll(reference => !reference.TryGetTarget(out _));
        }

        contexts.Add(new WeakReference<WebAudio.AudioContext>(context));
    }

    /// <summary>Closes the realtime audio contexts of the document, when it is destroyed.</summary>
    internal void CloseAudioContexts()
    {
        if (_audioContexts is not { } contexts)
            return;

        _audioContexts = null;
        foreach (var reference in contexts)
        {
            if (reference.TryGetTarget(out var context))
            {
                context.OnDocumentDestroyed();
            }
        }
    }

    /// <summary>Terminates the global scopes of the worklets of the document (https://html.spec.whatwg.org/multipage/worklets.html#worklets-lifetime), when it is destroyed.</summary>
    internal void TerminateWorklets()
    {
        _paintWorklet?.Terminate();
        _layoutWorklet?.Terminate();
        _animationWorklet?.Terminate();
        if (_audioWorklets is { } audioWorklets)
        {
            foreach (var worklet in audioWorklets)
            {
                worklet.Terminate();
            }
        }
    }
}
