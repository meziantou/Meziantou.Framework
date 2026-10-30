---
title: Engines and realms
description: What an engine and a realm are, when to create several, and how to use them from .NET and from several threads.
---

## Engines and realms

The API has two levels:

- A [JsEngine](cref:Meziantou.Framework.JavaScriptEngine.JsEngine) is an *agent* of the specification: it runs code on one thread at a time, owns the job queue of the
  promises and applies the limits of its [JsEngineOptions](cref:Meziantou.Framework.JavaScriptEngine.JsEngineOptions). It is disposable.
- A [JsRealm](cref:Meziantou.Framework.JavaScriptEngine.JsRealm) is a global environment created by `engine.CreateRealm()`: a global object (`realm.GlobalObject`) and
  its own copy of the built-ins. The scripts of a realm share their global variables.

Create a realm for each independent context, such as each document or each user script, and an engine for each
thread that runs code. The realms of an engine are isolated from each other, but can exchange objects:

<!-- ref:../../tests/Meziantou.Framework.JavaScriptEngine.Tests/JsEngineTests.cs;region=JavaScriptEngine_Realms;format=md-fenced-code-block;dedent=true -->
```csharp
using var engine = new JsEngine();
var first = engine.CreateRealm();
var second = engine.CreateRealm();

// Each realm has its own global object and its own built-ins
first.Execute("globalThis.owner = 'first'; Array.prototype.last = function () { return this.at(-1); };");
Assert.Equal("undefined", second.Execute("typeof owner").AsString());
Assert.Equal("undefined", second.Execute("typeof [].last").AsString());

// The objects of a realm can be given to the other realms of the same engine
using (engine.Enter())
{
    second.GlobalObject.SetProperty("firstGlobal", first.GlobalObject);
}

Assert.Equal("first", second.Execute("firstGlobal.owner").AsString());
Assert.False(second.Execute("firstGlobal.Array === Array").AsBoolean());
```
<!-- endref -->

- A change to the built-ins of a realm, such as a new method on `Array.prototype`, is not visible from the other
  realms.
- An object given to another realm keeps its own built-ins: `firstGlobal.Array` is not the `Array` of the second
  realm, so `instanceof Array` is false for its arrays (`Array.isArray` is true).
- `engine.Realms` lists the realms created by [CreateRealm](cref:Meziantou.Framework.JavaScriptEngine.JsEngine.CreateRealm).

The scripts can also create realms themselves with `ShadowRealm`.

## The scope of an engine

The methods of the objects ([GetProperty](cref:Meziantou.Framework.JavaScriptEngine.JsObject.GetProperty), [SetProperty](cref:Meziantou.Framework.JavaScriptEngine.JsObject.SetProperty), [DefineProperty](cref:Meziantou.Framework.JavaScriptEngine.JsObject.DefineProperty)...) and the factories of the realm
([CreateObject](cref:Meziantou.Framework.JavaScriptEngine.JsRealm.CreateObject), [CreateFunction](cref:Meziantou.Framework.JavaScriptEngine.JsRealm.CreateFunction)...) are used inside a scope of the engine, opened by `engine.Enter()` and closed by
disposing it. [JsRealm.Execute](cref:Meziantou.Framework.JavaScriptEngine.JsRealm.Execute), [JsEngine.Call](cref:Meziantou.Framework.JavaScriptEngine.JsEngine.Call) and [JsEngine.Construct](cref:Meziantou.Framework.JavaScriptEngine.JsEngine.Construct) enter the engine themselves:

<!-- ref:../../tests/Meziantou.Framework.JavaScriptEngine.Tests/JsEngineTests.cs;region=JavaScriptEngine_EnterScope;format=md-fenced-code-block;dedent=true -->
```csharp
using var engine = new JsEngine();
var realm = engine.CreateRealm();

// Execute, Call and Construct enter the engine themselves
var point = realm.Execute("({ x: 1, y: 2 })").AsObject();

// The members of the objects are used inside a scope of the engine
using (engine.Enter())
{
    point.SetProperty("z", 3);
    Assert.Equal(2, point.GetProperty("y").AsNumber());
    realm.GlobalObject.SetProperty("point", point);
}

Assert.Equal("1,2,3", realm.Execute("Object.values(point).join()").AsString());
```
<!-- endref -->

A .NET function called by a script already runs inside the scope: it does not need to enter the engine again.

## The current realm

`engine.CurrentRealm` is the realm of the running code. A function created by `realm.CreateFunction` runs in its own
realm: the objects that the built-ins create while it runs belong to that realm. [RunInRealm](cref:Meziantou.Framework.JavaScriptEngine.JsEngine.RunInRealm) runs a delegate with
another realm as the current realm.

## Threads

An engine is used by one thread at a time, and its objects belong to it. To run scripts in parallel, create one engine
per thread. A script compiled once with [JsPreparedScript](cref:Meziantou.Framework.JavaScriptEngine.JsPreparedScript) can run in all of them: see
[Reuse a compiled script](04-RunningCode/02-Scripts.md#reuse-a-compiled-script).

Do not give the objects of an engine to another engine. Engines share memory through `SharedArrayBuffer`: see
[Binary data](05-Interop/06-BinaryData.md).

## Lifetime

[JsEngine](cref:Meziantou.Framework.JavaScriptEngine.JsEngine) is disposable: dispose it when you no longer need it. The realms and the JavaScript objects are .NET
objects, collected by the garbage collector once nothing references them.
