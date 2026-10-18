using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Jint;
using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.WebPlatform.Tests.Jint;

/// <summary>Prioritized Task Scheduling (https://wicg.github.io/scheduling-apis/).</summary>
public sealed class SchedulerTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();
    private readonly FakeTimeProvider _clock = new();
    private readonly JintScriptEngine _engine = new();
    private readonly List<string> _errors = [];

    public void Dispose()
    {
        _contexts.Dispose();
        _engine.Dispose();
    }

    private Document Load(string html, bool scripting = true)
    {
        var context = _contexts.Create(new BrowsingContextOptions
        {
            ScriptEngine = scripting ? _engine : null,
            TimeProvider = _clock,
            UnhandledScriptError = (_, error) => _errors.Add(error.Message),
        });

        var document = context.Navigate("https://example.com/", _ => html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    /// <summary>Runs a script that appends to the <c>log</c> array, runs the tasks and the timers that are due, and returns the log.</summary>
    private string Run(string script)
    {
        var document = Load("<body><script>const log = []; const done = () => { document.title = log.join(); };\n" + script + "</script>");
        for (var i = 0; i < 20; i++)
        {
            _clock.Advance(TimeSpan.FromMilliseconds(10));
            _contexts.EventLoop.RunUntilIdle();
        }

        Assert.Empty(_errors);
        return document.Title;
    }

    [Fact]
    public void PostTask_RunsTheTasksInPriorityOrder()
    {
        var result = Run("""
            const post = (id, priority) => scheduler.postTask(() => { log.push(id); }, { priority });
            Promise.all([post('b1', 'background'), post('b2', 'background'), post('v1', 'user-visible'), post('v2'), post('u1', 'user-blocking'), post('u2', 'user-blocking')]).then(done);
            """);

        Assert.Equal("u1,u2,v1,v2,b1,b2", result);
    }

    [Fact]
    public void PostTask_ResolvesWithTheResultAndRejectsWithTheErrorOrTheAbortReason()
    {
        var result = Run("""
            (async () => {
              log.push(await scheduler.postTask(() => 1));
              log.push(await scheduler.postTask(async () => 2));
              const error = new Error('failed');
              log.push(await scheduler.postTask(() => { throw error; }).catch(e => e === error));
              const controller = new TaskController();
              const task = scheduler.postTask(() => { log.push('not run'); }, { signal: controller.signal });
              controller.abort();
              log.push(await task.catch(e => e.name));
              log.push(await scheduler.postTask(() => {}, { signal: AbortSignal.abort('reason') }).catch(e => e));
              done();
            })();
            """);

        Assert.Equal("1,2,true,AbortError,reason", result);
    }

    [Fact]
    public async Task PostTask_FromDotNet()
    {
        var document = Load("<body>", scripting: false);
        var scheduler = document.DefaultView!.Scheduler;
        var order = new List<string>();

        var background = scheduler.PostTask(() => { order.Add("background"); return 1; }, new SchedulerPostTaskOptions { Priority = TaskPriority.Background });
        var delayed = scheduler.PostTask(() => { order.Add("delayed"); return 2; }, new SchedulerPostTaskOptions { Priority = TaskPriority.UserBlocking, Delay = 50 });
        var failed = scheduler.PostTask(() => throw new InvalidOperationException("failed"));
        var blocking = scheduler.PostTask(() => { order.Add("blocking"); return 3; }, new SchedulerPostTaskOptions { Priority = TaskPriority.UserBlocking });
        Assert.False(blocking.IsCompleted);
        _contexts.EventLoop.RunUntilIdle();

        Assert.Equal(["blocking", "background"], order);
        Assert.Equal(1, await background);
        Assert.Equal(3, await blocking);
        Assert.IsType<InvalidOperationException>(failed.Exception!.InnerException);
        Assert.False(delayed.IsCompleted);

        _clock.Advance(TimeSpan.FromMilliseconds(50));
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(2, await delayed);
    }

    [Fact]
    public void PostTask_IsOrderedWithTheOtherTasks()
    {
        var document = Load("<body>", scripting: false);
        var scheduler = document.DefaultView!.Scheduler;
        var eventLoop = _contexts.EventLoop;
        var order = new List<string>();

        _ = scheduler.PostTask(() => { order.Add("background"); return null; }, new SchedulerPostTaskOptions { Priority = TaskPriority.Background });
        eventLoop.QueueTask(() => order.Add("task 1"));
        _ = scheduler.PostTask(() => { order.Add("user-visible"); return null; });
        eventLoop.QueueTask(() => order.Add("task 2"));
        _ = scheduler.PostTask(() => { order.Add("user-blocking"); return null; }, new SchedulerPostTaskOptions { Priority = TaskPriority.UserBlocking });
        eventLoop.RunUntilIdle();

        // The user-blocking tasks precede the other tasks, the user-visible tasks keep their place, and the background
        // tasks run when no other task is queued
        Assert.Equal(["user-blocking", "task 1", "user-visible", "task 2", "background"], order);
    }

    [Fact]
    public void Yield_InheritsThePriorityOfTheTask()
    {
        var result = Run("""
            const tasks = [];
            tasks.push(scheduler.postTask(async () => {
              log.push('y0');
              for (let i = 1; i < 3; i++) {
                await scheduler.yield();
                log.push('y' + i);
              }
            }, { priority: 'background' }));
            tasks.push(scheduler.postTask(() => { log.push('v1'); }));
            tasks.push(scheduler.postTask(() => { log.push('b1'); }, { priority: 'background' }));
            Promise.all(tasks).then(done);
            """);

        // The continuations precede the tasks of their priority
        Assert.Equal("v1,y0,y1,y2,b1", result);
    }

    [Fact]
    public void Yield_WithoutTask_IsAUserVisibleContinuationThatPrecedesTheTimers()
    {
        var result = Run("""
            setTimeout(async () => {
              log.push('t1');
              scheduler.postTask(() => { log.push('u'); }, { priority: 'user-blocking' });
              scheduler.postTask(() => { log.push('v'); });
              await scheduler.yield();
              log.push('y');
            });
            setTimeout(() => { log.push('t2'); setTimeout(done); });
            """);

        Assert.Equal("t1,u,y,t2,v", result);
    }

    [Fact]
    public void Yield_InheritsTheStateOfTheReactionsAndOfTheMicrotasks()
    {
        var result = Run("""
            (async () => {
              // The reaction is created without scheduling state, and run by a user-blocking task
              await scheduler.postTask(() => {});
              let resolve;
              const reaction = new Promise(r => { resolve = r; }).then(async () => {
                await scheduler.yield();
                log.push('reaction');
              });
              const task = scheduler.postTask(() => {
                resolve();
                queueMicrotask(async () => {
                  await scheduler.yield();
                  log.push('microtask');
                });
              }, { priority: 'user-blocking' });
              const other = scheduler.postTask(() => { log.push('task'); }, { priority: 'user-blocking' });
              await Promise.all([reaction, task, other]);
              done();
            })();
            """);

        Assert.Equal("microtask,task,reaction", result);
    }

    [Fact]
    public void Yield_IsAbortedByTheSignalOfTheTask()
    {
        var result = Run("""
            const controller = new TaskController();
            scheduler.postTask(async () => {
              scheduler.postTask(() => { controller.abort(); }, { priority: 'user-blocking' });
              const pending = scheduler.yield();
              log.push(controller.signal.aborted);
              log.push(await pending.then(() => 'resolved', e => e.name));
              done();
            }, { signal: controller.signal, priority: 'background' });
            """);

        Assert.Equal("false,AbortError", result);
    }

    [Fact]
    public void TaskController_SetPriority_ChangesThePriorityOfTheTasks()
    {
        var result = Run("""
            const controller = new TaskController({ priority: 'background' });
            const signal = controller.signal;
            const dependent = TaskSignal.any([], { priority: signal });
            const fixed = TaskSignal.any([], { priority: 'background' });
            signal.onprioritychange = e => { log.push(`signal ${e.previousPriority}>${signal.priority} ${e instanceof TaskPriorityChangeEvent}`); };
            dependent.onprioritychange = e => {
              log.push(`dependent ${e.previousPriority}>${dependent.priority}`);
              try { controller.setPriority('user-visible'); } catch (error) { log.push(error.name); }
            };
            const tasks = [
              scheduler.postTask(() => { log.push('signal'); }, { signal }),
              scheduler.postTask(() => { log.push('dependent'); }, { signal: dependent }),
              scheduler.postTask(() => { log.push('fixed'); }, { signal: fixed }),
              scheduler.postTask(() => { log.push('priority'); }, { signal, priority: 'background' }),
              scheduler.postTask(() => { log.push('visible'); }),
            ];
            controller.setPriority('user-blocking');
            controller.setPriority('user-blocking');
            log.push(signal instanceof TaskSignal, signal instanceof AbortSignal, controller instanceof AbortController, fixed.priority);
            Promise.all(tasks).then(done);
            """);

        Assert.Equal("signal background>user-blocking true,dependent background>user-blocking,NotAllowedError,true,true,true,background,signal,dependent,visible,fixed,priority", result);
    }

    [Fact]
    public void Scheduler_OfADocumentThatIsNotFullyActive_SchedulesNothing()
    {
        var result = Run("""
            const frame = document.body.appendChild(document.createElement('iframe'));
            const frameScheduler = frame.contentWindow.scheduler;
            const frameDomException = frame.contentWindow.DOMException;
            frameScheduler.postTask(() => { log.push('task'); });
            frameScheduler.yield().then(() => { log.push('continuation'); });
            frame.remove();
            (async () => {
              log.push(await frameScheduler.postTask(() => {}).catch(e => e.name + ' ' + (e instanceof frameDomException)));
              log.push(await frameScheduler.yield().catch(e => e.name));
              await new Promise(resolve => setTimeout(resolve, 10));
              done();
            })();
            """);

        Assert.Equal("NotSupportedError true,NotSupportedError", result);
    }

    [Fact]
    public void Scheduler_IsReplaceable()
    {
        var result = Run("""
            log.push(scheduler instanceof Scheduler, scheduler === window.scheduler);
            scheduler = 1;
            log.push(scheduler);
            done();
            """);

        Assert.Equal("true,true,1", result);
    }
}
