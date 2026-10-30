---
title: Events
description: Check that an action raises an event, get its sender and arguments, or check that it raises none.
---

## Check that an event is raised

[Raise](cref:Meziantou.Framework.Assertions.Assert.Raise) attaches a handler to the event, runs the action, detaches the handler, and returns the event that was raised:

<!-- ref:../../tests/Meziantou.Framework.Assertions.Tests/AssertRaiseTests.cs;region=Assert_Raise;format=md-fenced-code-block;dedent=true -->
```csharp
using var component = new Component();

// Attach a handler, run the action, detach the handler, and return the first event raised
var raised = Assert.Raise(
    handler => component.Disposed += handler,
    handler => component.Disposed -= handler,
    () => component.Dispose());

Assert.Same(component, raised.Sender);
Assert.Same(EventArgs.Empty, raised.Arguments);
```
<!-- endref -->

The first two delegates attach and detach the handler: C# cannot pass an event as an argument, only a delegate that
subscribes to it. The returned [RaisedEvent\<TEventArgs\>](cref:Meziantou.Framework.Assertions.RaisedEvent{TEventArgs}) has the [Sender](cref:Meziantou.Framework.Assertions.RaisedEvent{TEventArgs}.Sender) and the [Arguments](cref:Meziantou.Framework.Assertions.RaisedEvent{TEventArgs}.Arguments) of the event.

## Events with event arguments

For an `EventHandler<TEventArgs>` event, use [Raise\<TEventArgs\>](cref:Meziantou.Framework.Assertions.Assert.Raise). For an event of another delegate type, such as
`PropertyChangedEventHandler`, attach `handler.Invoke`: it converts the handler to the delegate type of the event, and
detaching `handler.Invoke` removes it.

<!-- ref:../../tests/Meziantou.Framework.Assertions.Tests/AssertRaiseTests.cs;region=Assert_RaiseGeneric;format=md-fenced-code-block;dedent=true -->
```csharp
var collection = new ObservableCollection<string>();
var notifier = (INotifyPropertyChanged)collection;

// An event whose delegate is not an EventHandler<T>: adapt it with handler.Invoke
var raised = Assert.Raise<PropertyChangedEventArgs>(
    handler => notifier.PropertyChanged += handler.Invoke,
    handler => notifier.PropertyChanged -= handler.Invoke,
    () => collection.Add("Alice"));

Assert.Equal("Count", raised.Arguments.PropertyName);

Assert.DoesNotRaise<PropertyChangedEventArgs>(
    handler => notifier.PropertyChanged += handler.Invoke,
    handler => notifier.PropertyChanged -= handler.Invoke,
    () => _ = collection.Contains("Bob"));
```
<!-- endref -->

| Assertion | Passes when the action raises |
| --- | --- |
| `Raise<T>(attach, detach, action)` | an event whose arguments are exactly of type `T` |
| `RaiseAny<T>(attach, detach, action)` | an event whose arguments are of type `T` or a derived type |
| `DoesNotRaise<T>(attach, detach, action)` | no event whose arguments are exactly of type `T` |
| `DoesNotRaiseAny<T>(attach, detach, action)` | no event whose arguments are of type `T` or a derived type |

- When the event is raised several times, [Raise](cref:Meziantou.Framework.Assertions.Assert.Raise) returns the first event whose arguments match: here, the
  `PropertyChanged` event of `Count`, raised before the one of `Item[]`.
- An event raised with `null` arguments matches any type of arguments. [DoesNotRaise](cref:Meziantou.Framework.Assertions.Assert.DoesNotRaise) fails for it too.
- Without a type parameter, the assertions take an `EventHandler` event and its `EventArgs`.

## Asynchronous actions

When the action returns a `Task` or a `ValueTask`, the assertion awaits it before it detaches the handler, and returns
a task: await the assertion, as for [exceptions](08-Exceptions.md#asynchronous-code).

The names [Raises](cref:Meziantou.Framework.Assertions.Assert.Raises), [RaisesAny](cref:Meziantou.Framework.Assertions.Assert.RaisesAny), [RaisesAsync](cref:Meziantou.Framework.Assertions.Assert.RaisesAsync), [RaisesAnyAsync](cref:Meziantou.Framework.Assertions.Assert.RaisesAnyAsync), [NotRaisedAny](cref:Meziantou.Framework.Assertions.Assert.NotRaisedAny) and [NotRaisedAnyAsync](cref:Meziantou.Framework.Assertions.Assert.NotRaisedAnyAsync) are also
available.
