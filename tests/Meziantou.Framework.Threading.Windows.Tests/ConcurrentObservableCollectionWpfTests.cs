using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Threading;
using Meziantou.Framework.Collections.Concurrent;

// The project enables both WPF and Windows Forms, so the types they both define must be disambiguated
using ListBox = System.Windows.Controls.ListBox;
using Size = System.Windows.Size;

namespace Meziantou.Framework.Threading.Windows.Tests;

public sealed class ConcurrentObservableCollectionWpfTests
{
    [Fact(Timeout = 95000)]
    public void AsObservableCanBeBoundToAListBoxWhenTheSynchronizationContextInstanceChanged()
    {
        RunOnDispatcherThread(_ =>
        {
            // A view model is created while WPF has a DispatcherSynchronizationContext installed on the UI thread
            var contextAtCreation = Assert.IsType<DispatcherSynchronizationContext>(SynchronizationContext.Current);
            var collection = new ConcurrentObservableCollection<string>();
            var listBox = new ListBox { ItemsSource = collection.AsObservable };

            // WPF uses another DispatcherSynchronizationContext instance for the same Dispatcher when it pushes a frame
            // (Dispatcher.Run, ShowDialog, a nested message loop) or copies the context, which is what made the
            // collection consider the UI thread a foreign thread
            var newContext = contextAtCreation.CreateCopy();
            Assert.NotSame(contextAtCreation, newContext);
            SynchronizationContext.SetSynchronizationContext(newContext);

            collection.AddRange("a", "b");

            // The layout pass reads the collection through the WPF binding engine
            listBox.Measure(new Size(1000, 1000));
            listBox.Arrange(new Rect(0, 0, 1000, 1000));

            Assert.Equal(["a", "b"], listBox.Items.Cast<string>());
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 95000)]
    public void ItemsAddedFromAnotherThreadAreNotifiedOnTheDispatcherThread()
    {
        RunOnDispatcherThread(dispatcher =>
        {
            var contextAtCreation = Assert.IsType<DispatcherSynchronizationContext>(SynchronizationContext.Current);
            var collection = new ConcurrentObservableCollection<string>();
            var listBox = new ListBox { ItemsSource = collection.AsObservable };

            SynchronizationContext.SetSynchronizationContext(contextAtCreation.CreateCopy());

            var thread = new Thread(() => collection.AddRange("a", "b"));
            thread.Start();
            thread.Join();

            // The notifications are posted to the Dispatcher, so the bound collection is still empty
            Assert.Empty(listBox.Items);

            DoEvents(dispatcher);

            listBox.Measure(new Size(1000, 1000));
            listBox.Arrange(new Rect(0, 0, 1000, 1000));

            Assert.Equal(["a", "b"], listBox.Items.Cast<string>());
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 95000)]
    public void AsObservableCannotBeAccessedFromAnotherDispatcherThread()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        IReadOnlyObservableCollection<string>? observable = null;
        RunOnDispatcherThread(_ => observable = new ConcurrentObservableCollection<string>().AsObservable, cancellationToken);

        // Another UI thread has its own Dispatcher, so it must not be able to read the collection
        RunOnDispatcherThread(_ => Assert.Throws<InvalidOperationException>(() => observable!.Count), cancellationToken);
    }

    /// <summary>Runs the pending dispatcher operations, like <c>Application.DoEvents</c> does.</summary>
    private static void DoEvents(Dispatcher dispatcher)
    {
        var frame = new DispatcherFrame();
        _ = dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void RunOnDispatcherThread(Action<Dispatcher> action, CancellationToken cancellationToken)
    {
        Dispatcher? dispatcher = null;
        using var dispatcherCreated = new ManualResetEventSlim(initialState: false);

        var thread = new Thread(() =>
        {
            Volatile.Write(ref dispatcher, Dispatcher.CurrentDispatcher);
            dispatcherCreated.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        dispatcherCreated.Wait(cancellationToken);

        var currentDispatcher = Volatile.Read(ref dispatcher)!;

        // An operation failing on the dispatcher thread would otherwise take the process down instead of failing the test
        Exception? unhandledException = null;
        currentDispatcher.UnhandledException += (sender, e) =>
        {
            unhandledException = e.Exception;
            e.Handled = true;
        };

        try
        {
            currentDispatcher.Invoke(() => action(currentDispatcher));
        }
        finally
        {
            currentDispatcher.InvokeShutdown();
            thread.Join();
        }

        if (unhandledException is not null)
        {
            ExceptionDispatchInfo.Throw(unhandledException);
        }
    }
}
