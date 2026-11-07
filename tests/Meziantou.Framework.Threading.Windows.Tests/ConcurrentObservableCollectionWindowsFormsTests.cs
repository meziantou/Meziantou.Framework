using System.Runtime.ExceptionServices;
using System.Windows.Forms;
using Meziantou.Framework.Collections.Concurrent;

namespace Meziantou.Framework.Threading.Windows.Tests;

public sealed class ConcurrentObservableCollectionWindowsFormsTests
{
    [Fact(Timeout = 95000)]
    public void AsObservableCanBeAccessedWhenTheSynchronizationContextInstanceChanged()
    {
        RunOnWindowsFormsThread(() =>
        {
            var contextAtCreation = Assert.IsType<WindowsFormsSynchronizationContext>(SynchronizationContext.Current);
            var collection = new ConcurrentObservableCollection<string>();
            var observable = collection.AsObservable;

            // Windows Forms uses another WindowsFormsSynchronizationContext instance for the same thread when the
            // context is copied, which is what made the collection consider the UI thread a foreign thread
            var newContext = contextAtCreation.CreateCopy();
            Assert.NotSame(contextAtCreation, newContext);
            SynchronizationContext.SetSynchronizationContext(newContext);

            collection.AddRange("a", "b");

            Assert.Equal(["a", "b"], observable.ToList());
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 95000)]
    public void ItemsAddedFromAnotherThreadAreNotifiedOnTheUIThread()
    {
        RunOnWindowsFormsThread(() =>
        {
            var contextAtCreation = Assert.IsType<WindowsFormsSynchronizationContext>(SynchronizationContext.Current);
            var collection = new ConcurrentObservableCollection<string>();
            var observable = collection.AsObservable;

            SynchronizationContext.SetSynchronizationContext(contextAtCreation.CreateCopy());

            var thread = new Thread(() => collection.AddRange("a", "b"));
            thread.Start();
            thread.Join();

            // The notifications are posted to the message loop, so the observable collection is still empty
            Assert.Empty(observable);

            Application.DoEvents();

            Assert.Equal(["a", "b"], observable.ToList());
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 95000)]
    public void AsObservableCannotBeAccessedFromAnotherUIThread()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        IReadOnlyObservableCollection<string>? observable = null;
        RunOnWindowsFormsThread(() => observable = new ConcurrentObservableCollection<string>().AsObservable, cancellationToken);

        // Another UI thread runs its own message loop, so it must not be able to read the collection
        RunOnWindowsFormsThread(() => Assert.Throws<InvalidOperationException>(() => observable!.Count), cancellationToken);
    }

    private static void RunOnWindowsFormsThread(Action action, CancellationToken cancellationToken)
    {
        Exception? failure = null;
        using var completed = new ManualResetEventSlim(initialState: false);

        var thread = new Thread(() =>
        {
            try
            {
                // A Windows Forms application installs the context on its UI thread when the first control is created
                using var control = new Control();
                _ = control.Handle;
                if (SynchronizationContext.Current is not WindowsFormsSynchronizationContext)
                {
                    SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                }

                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                completed.Set();
            }
        })
        {
            IsBackground = true,
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        completed.Wait(cancellationToken);
        thread.Join();

        if (failure is not null)
        {
            ExceptionDispatchInfo.Throw(failure);
        }
    }
}
