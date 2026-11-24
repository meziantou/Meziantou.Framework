using System.Runtime.InteropServices;
using Meziantou.Framework.JavaScriptEngine.Compiler;

namespace Meziantou.Framework.JavaScriptEngine;

// The state of the inline caches that belongs to the engine.
public sealed partial class JsEngine
{
    // The inline caches of the templates shared by several engines (a JsPreparedScript): the entries reference the objects
    // of the engine, so they cannot be stored in the template
    private Dictionary<FunctionTemplate, PropertyCacheEntry?[]>? _sharedTemplateCaches;

    // The value of InlineCache.DetachedEpoch the epoch of the engine includes
    private int _seenDetachedEpoch = InlineCache.DetachedEpoch;

    /// <summary>
    /// The epoch of the inline caches of the engine: a cache that depends on other objects than the receiver is valid while
    /// it does not change (<see cref="InlineCache"/>). The engine runs on one thread at a time: no other engine changes it.
    /// </summary>
    internal int InlineCacheEpoch { get; set; }

    /// <summary>Gets the shapes of the new function objects (created on first use).</summary>
    internal Interpreter.ClosureShapes ClosureShapes => field ??= new(ShapeRoots);

    /// <summary>Gets the shapes of the new arguments objects (created on first use).</summary>
    internal Interpreter.ArgumentsShapes ArgumentsShapes => field ??= new(ShapeRoots);

    /// <summary>Gets the shape of the new RegExp objects: their own lastIndex (created on first use).</summary>
    internal Shape RegExpShape => field ??= ShapeRoots.GetRoot(ShapeKind.Ordinary).AddProperty(JsRegExpObject.LastIndexKey, PropertyFlags.Writable) ?? throw new InvalidOperationException("The shape of a RegExp object cannot be created");

    /// <summary>Gets the inline caches of the code of a template for the frames of this engine.</summary>
    internal PropertyCacheEntry?[] GetInlineCaches(FunctionTemplate template)
    {
        var caches = template.Caches;
        if (!template.Source.IsShared || caches.Length == 0)
            return caches;

        _sharedTemplateCaches ??= [];
        ref var engineCaches = ref CollectionsMarshal.GetValueRefOrAddDefault(_sharedTemplateCaches, template, out _);
        return engineCaches ??= new PropertyCacheEntry?[caches.Length];
    }

    /// <summary>Invalidates the inline caches of the engine when an object was changed while no engine was current.</summary>
    private void SyncDetachedInlineCacheInvalidations()
    {
        var detached = InlineCache.DetachedEpoch;
        if (detached != _seenDetachedEpoch)
        {
            _seenDetachedEpoch = detached;
            InlineCacheEpoch++;
        }
    }
}
