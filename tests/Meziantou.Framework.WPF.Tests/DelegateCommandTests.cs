using System.Windows.Threading;

namespace Meziantou.Framework.WPF.Tests;

public sealed class DelegateCommandTests
{
    [Fact]
    public void ExecuteAction()
    {
        var count = 0;
        var command = DelegateCommand.Create(() => count++);

        Assert.True(command.CanExecute(parameter: null));
        command.Execute(parameter: null);

        Assert.Equal(1, count);
    }

    [Fact]
    public void ExecuteActionWithParameter()
    {
        object? received = null;
        var command = DelegateCommand.Create(parameter => received = parameter);

        command.Execute("value");

        Assert.Equal("value", received);
    }

    [Fact]
    public void CanExecuteIsUsed()
    {
        var canExecute = false;
        var command = DelegateCommand.Create(() => { }, () => canExecute);

        Assert.False(command.CanExecute(parameter: null));

        canExecute = true;
        Assert.True(command.CanExecute(parameter: null));
    }

    [Fact]
    public void CanExecuteReceivesParameter()
    {
        object? received = null;
        var command = DelegateCommand.Create(_ => { }, parameter =>
        {
            received = parameter;
            return true;
        });

        Assert.True(command.CanExecute("value"));
        Assert.Equal("value", received);
    }

    [Fact]
    public void NullExecuteIsANoOp()
    {
        var command = DelegateCommand.Create((Action?)null);

        Assert.True(command.CanExecute(parameter: null));
        command.Execute(parameter: null);
    }

    [Fact]
    public void NullCanExecuteReturnsTrue()
    {
        var command = DelegateCommand.Create(() => { }, canExecute: null);

        Assert.True(command.CanExecute(parameter: null));
    }

    [Fact]
    public void NullAsyncExecuteIsANoOp()
    {
        var command = DelegateCommand.Create((Func<Task>?)null);

        Assert.True(command.CanExecute(parameter: null));
        command.Execute(parameter: null);
        Assert.True(command.CanExecute(parameter: null));
    }

    [Fact]
    public void AsyncCommandCannotExecuteWhileRunning()
    {
        var tcs = new TaskCompletionSource();
        var executions = 0;
        var command = DelegateCommand.Create(() =>
        {
            executions++;
            return tcs.Task;
        });

        using var finished = WatchForCompletion(command);

        Assert.True(command.CanExecute(parameter: null));
        command.Execute(parameter: null);

        Assert.False(command.CanExecute(parameter: null));

        // The re-entrant call must be ignored
        command.Execute(parameter: null);
        Assert.Equal(1, executions);

        tcs.SetResult();
        Assert.True(finished.Wait(TimeSpan.FromSeconds(30)), "The command did not complete");
        Assert.True(command.CanExecute(parameter: null));
    }

    [Fact]
    public void AsyncCommandResetsStateWhenTheTaskFails()
    {
        var previousContext = SynchronizationContext.Current;
        var context = new CapturingSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            var tcs = new TaskCompletionSource();
            var command = DelegateCommand.Create(() => tcs.Task);

            using var finished = WatchForCompletion(command);

            command.Execute(parameter: null);
            Assert.False(command.CanExecute(parameter: null));

            tcs.SetException(new InvalidOperationException("boom"));
            Assert.True(finished.Wait(TimeSpan.FromSeconds(30)), "The command did not complete");
            Assert.True(command.CanExecute(parameter: null));

            // Execute is `async void`: the failure is rethrown on the synchronization context and cannot be observed
            // by the caller. This is the documented behavior of the asynchronous DelegateCommand.Create overloads.
            var exception = Assert.Single(context.Exceptions);
            Assert.Equal("boom", exception.Message);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    [Fact]
    public void AsyncCommandOverridesCanExecuteWhileRunning()
    {
        var tcs = new TaskCompletionSource();
        var command = DelegateCommand.Create(() => tcs.Task, () => true);

        using var finished = WatchForCompletion(command);

        command.Execute(parameter: null);
        Assert.False(command.CanExecute(parameter: null));

        tcs.SetResult();
        Assert.True(finished.Wait(TimeSpan.FromSeconds(30)), "The command did not complete");
        Assert.True(command.CanExecute(parameter: null));
    }

    [Fact]
    public void CanExecuteChangedIsRaisedSynchronouslyWithoutDispatcher()
    {
        var command = DelegateCommand.Create(() => { });
        var raised = false;
        command.CanExecuteChanged += (_, _) => raised = true;

        command.RaiseCanExecuteChanged();

        Assert.True(raised);
    }

    [Fact]
    public void CanExecuteChangedWithoutHandlerDoesNotThrow()
    {
        var command = DelegateCommand.Create(() => { });

        command.RaiseCanExecuteChanged();
    }

    // Regression test: the commands used to capture Dispatcher.CurrentDispatcher, which *creates* a dispatcher for the
    // calling thread. A command created on a thread pool thread was therefore bound to a dispatcher that never pumps
    // messages, and RaiseCanExecuteChanged blocked forever on Dispatcher.Invoke.
    [Fact(Timeout = 95000)]
    public async Task CanExecuteChangedDoesNotBlockWhenTheCommandIsCreatedOnTheThreadPool()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var command = await Task.Run(() => DelegateCommand.Create(() => { }), cancellationToken);

        var raised = false;
        command.CanExecuteChanged += (_, _) => raised = true;

        using var done = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            command.RaiseCanExecuteChanged();
            done.Set();
        })
        {
            IsBackground = true,
        };
        thread.Start();

        Assert.True(done.Wait(TimeSpan.FromSeconds(30), cancellationToken), "RaiseCanExecuteChanged blocked");
        Assert.True(raised);
    }

    [Fact(Timeout = 95000)]
    public async Task CanExecuteChangedIsRaisedOnTheDispatcherThread()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var dispatcher = await StartDispatcherThreadAsync(cancellationToken);
        try
        {
            var command = dispatcher.Invoke(() => DelegateCommand.Create(() => { }));

            var raisedOnThreadId = 0;
            using var raised = new ManualResetEventSlim();
            command.CanExecuteChanged += (_, _) =>
            {
                raisedOnThreadId = Environment.CurrentManagedThreadId;
                raised.Set();
            };

            Assert.NotEqual(dispatcher.Thread.ManagedThreadId, Environment.CurrentManagedThreadId);
            command.RaiseCanExecuteChanged();

            Assert.True(raised.Wait(TimeSpan.FromSeconds(30), cancellationToken), "The event was not raised");
            Assert.Equal(dispatcher.Thread.ManagedThreadId, raisedOnThreadId);
        }
        finally
        {
            dispatcher.InvokeShutdown();
        }
    }

    [Fact(Timeout = 95000)]
    public async Task CanExecuteChangedIsRaisedSynchronouslyOnTheDispatcherThread()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var dispatcher = await StartDispatcherThreadAsync(cancellationToken);
        try
        {
            var raised = dispatcher.Invoke(() =>
            {
                var command = DelegateCommand.Create(() => { });
                var handled = false;
                command.CanExecuteChanged += (_, _) => handled = true;

                command.RaiseCanExecuteChanged();
                return handled;
            });

            Assert.True(raised, "The event was not raised synchronously");
        }
        finally
        {
            dispatcher.InvokeShutdown();
        }
    }

    /// <summary>Signals once the async command has raised CanExecuteChanged both before and after running its task.</summary>
    private static ManualResetEventSlim WatchForCompletion(IDelegateCommand command)
    {
        var completed = new ManualResetEventSlim();
        var count = 0;
        command.CanExecuteChanged += (_, _) =>
        {
            if (Interlocked.Increment(ref count) == 2)
            {
                completed.Set();
            }
        };

        return completed;
    }

    /// <summary>Captures the exceptions an <c>async void</c> method rethrows instead of letting them crash the process.</summary>
    private sealed class CapturingSynchronizationContext : SynchronizationContext
    {
        private readonly List<Exception> _exceptions = [];

        public IReadOnlyList<Exception> Exceptions
        {
            get
            {
                lock (_exceptions)
                {
                    return [.. _exceptions];
                }
            }
        }

        public override void Post(SendOrPostCallback d, object? state)
        {
            try
            {
                d(state);
            }
            catch (Exception ex)
            {
                lock (_exceptions)
                {
                    _exceptions.Add(ex);
                }
            }
        }

        public override void Send(SendOrPostCallback d, object? state)
        {
            Post(d, state);
        }
    }

    private static async Task<Dispatcher> StartDispatcherThreadAsync(CancellationToken cancellationToken)
    {
        Dispatcher? dispatcher = null;
        var thread = new Thread(() =>
        {
            Volatile.Write(ref dispatcher, Dispatcher.CurrentDispatcher);
            Dispatcher.Run();
        })
        {
            IsBackground = true,
        };
        thread.Start();

        while (Volatile.Read(ref dispatcher) is null)
        {
            await Task.Delay(1, cancellationToken);
        }

        return Volatile.Read(ref dispatcher)!;
    }
}
