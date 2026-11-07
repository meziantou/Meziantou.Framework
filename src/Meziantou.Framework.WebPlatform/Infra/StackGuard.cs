using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Infra;

/// <summary>
/// Keeps the deep recursions of the rendering (e.g. the layout of deeply nested boxes, whose recursion uses tens of kilobytes
/// of stack per level) from overflowing the stack of the thread, which kills the process: when the stack is nearly exhausted,
/// the recursion continues on a new thread with a large stack while the current thread waits. A browser lays out a deep
/// tree on its main thread (8 MB of stack), while the code of this library runs on the thread of the caller (e.g. a thread
/// pool thread, 1.5 MB on macOS, 1 MB on Windows).
/// </summary>
internal static class StackGuard
{
    // The stack of the thread that continues a recursion (as the threads of the workers)
    private const int StackSize = 16 * 1024 * 1024;

    /// <summary>Whether the stack of the current thread has enough space for another level of a recursion.</summary>
    public static bool HasSufficientStack => RuntimeHelpers.TryEnsureSufficientExecutionStack();

    /// <summary>
    /// Runs <paramref name="function"/> on a new thread with a large stack and returns its result (or rethrows its exception)
    /// once it completes. The current thread waits, so the objects are not shared concurrently; the thread-static state that
    /// the rendering reads (the current and entry global objects, the page break rules of the flow being collected) is
    /// copied to the new thread.
    /// </summary>
    public static T RunOnNewStack<T>(Func<T> function)
    {
        var currentGlobalDocument = Document.CurrentGlobalDocumentOrNull;
        var entryGlobalDocument = Document.EntryGlobalDocument;
        var currentWorkerGlobal = GlobalScope.CurrentWorkerGlobal;
        var pageBreakRules = PageBreakRules.Current;

        T result = default!;
        ExceptionDispatchInfo? exception = null;
        var thread = new Thread(() =>
        {
            Document.CurrentGlobalDocumentOrNull = currentGlobalDocument;
            Document.EntryGlobalDocument = entryGlobalDocument;
            GlobalScope.CurrentWorkerGlobal = currentWorkerGlobal;
            PageBreakRules.Current = pageBreakRules;
            try
            {
                result = function();
            }
#pragma warning disable CA1031 // The exception is rethrown by the waiting thread
            catch (Exception ex)
#pragma warning restore CA1031
            {
                exception = ExceptionDispatchInfo.Capture(ex);
            }
        }, StackSize)
        {
            IsBackground = true,
            Name = "Deep recursion",
        };

        thread.Start();
        thread.Join();
        exception?.Throw();
        return result;
    }
}
