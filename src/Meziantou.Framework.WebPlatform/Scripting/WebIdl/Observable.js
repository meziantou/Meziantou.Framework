// The Observable API (https://wicg.github.io/observable/): the Observable and Subscriber interfaces, and the steps of
// EventTarget.prototype.when(). It is implemented in JavaScript because its algorithms are made of callbacks, promises,
// iterators and abort signals, whose microtask timing is the one of the engine.
//
// The script is evaluated once per realm, when one of its members is first used. It is a function called with the hooks
// of the host, and returns the interface objects and when(). The internal slots of the objects are stored in records
// (objects without prototype) associated with the objects by a WeakMap shared by the realms of the agent, so that an
// Observable of an iframe is an Observable for the operators of the parent document.
(function (hooks) {
  'use strict';

  // ------------------------------------------------------------------------------------------------------------------
  // Intrinsics
  // ------------------------------------------------------------------------------------------------------------------

  const apply = Reflect.apply;
  const ownKeys = Reflect.ownKeys;
  const ObjectCreate = Object.create;
  const defineProperty = Object.defineProperty;
  const getOwnPropertyDescriptor = Object.getOwnPropertyDescriptor;
  const getter = (o, k) => getOwnPropertyDescriptor(o, k).get;
  const MathTrunc = Math.trunc;
  const TypeError_ = TypeError;
  const RangeError_ = RangeError;
  const Promise_ = Promise;
  const promiseThen = Promise.prototype.then;
  const promiseRejectFunction = Promise.reject;
  const weakMapGet = WeakMap.prototype.get;
  const weakMapSet = WeakMap.prototype.set;
  const arrayPush = Array.prototype.push;
  const arraySlice = Array.prototype.slice;
  const arrayIndexOf = Array.prototype.indexOf;
  const arraySplice = Array.prototype.splice;
  const SymbolAsyncIterator = Symbol.asyncIterator;
  const SymbolIterator = Symbol.iterator;
  const SymbolToStringTag = Symbol.toStringTag;

  const { AbortController: AbortController_, AbortSignal: AbortSignal_, EventTarget: EventTarget_ } = hooks.interfaces;
  const abortControllerSignal = getter(AbortController_.prototype, 'signal');
  const abortControllerAbort = AbortController_.prototype.abort;
  const abortSignalAborted = getter(AbortSignal_.prototype, 'aborted');
  const abortSignalReason = getter(AbortSignal_.prototype, 'reason');
  const abortSignalAny = AbortSignal_.any;
  const addEventListener = EventTarget_.prototype.addEventListener;
  const addAbortAlgorithm = hooks.addAbortAlgorithm;
  const hostReportException = hooks.reportException;
  const isFullyActive = hooks.isFullyActive;
  const Error_ = Error;
  const stringSplit = String.prototype.split;
  const stringTrim = String.prototype.trim;
  const stringSlice = String.prototype.slice;
  const stringStartsWith = String.prototype.startsWith;
  const stringEndsWith = String.prototype.endsWith;
  const stringLastIndexOf = String.prototype.lastIndexOf;
  const NumberParseInt = Number.parseInt;

  // https://html.spec.whatwg.org/multipage/webappapis.html#report-an-exception: the host locates an error object where it
  // was created, and another value at the first frame of the stack that is in a script (this script has no URL)
  function reportException(value) {
    hostReportException(value, callerLocation());
  }

  // Parses the frames of the stack ("at name (url:line:column)" or "at url:line:column"): [url, line, column] or undefined
  function callerLocation() {
    const stack = new Error_().stack;
    if (typeof stack !== 'string') return undefined;
    const lines = apply(stringSplit, stack, ['\n']);
    for (let i = 0; i < lines.length; i++) {
      let frame = apply(stringTrim, lines[i], []);
      if (!apply(stringStartsWith, frame, ['at '])) continue;
      frame = apply(stringSlice, frame, [3]);
      const open = apply(stringLastIndexOf, frame, [' (']);
      if (open >= 0 && apply(stringEndsWith, frame, [')'])) frame = apply(stringSlice, frame, [open + 2, -1]);
      const columnSeparator = apply(stringLastIndexOf, frame, [':']);
      if (columnSeparator <= 0) continue;
      const lineSeparator = apply(stringLastIndexOf, frame, [':', columnSeparator - 1]);
      if (lineSeparator <= 0) continue;
      const url = apply(stringSlice, frame, [0, lineSeparator]);
      const line = NumberParseInt(apply(stringSlice, frame, [lineSeparator + 1, columnSeparator]), 10);
      const column = NumberParseInt(apply(stringSlice, frame, [columnSeparator + 1]), 10);
      if (url === '<anonymous>' || url === '' || !(line > 0)) continue;
      return [url, line, column];
    }

    return undefined;
  }

  const push = (list, item) => { apply(arrayPush, list, [item]); };
  const copy = (list) => apply(arraySlice, list, []);
  const remove = (list, item) => {
    const index = apply(arrayIndexOf, list, [item]);
    if (index >= 0) apply(arraySplice, list, [index, 1]);
  };

  // ------------------------------------------------------------------------------------------------------------------
  // Internal slots
  // ------------------------------------------------------------------------------------------------------------------

  const internals = hooks.internals;
  const isObject = (value) => (typeof value === 'object' && value !== null) || typeof value === 'function';

  function createRecord(object, kind) {
    const record = ObjectCreate(null);
    record.kind = kind;
    record.object = object;
    apply(weakMapSet, internals, [object, record]);
    return record;
  }

  function getRecord(object, kind) {
    if (!isObject(object)) return undefined;
    const record = apply(weakMapGet, internals, [object]);
    return record !== undefined && record.kind === kind ? record : undefined;
  }

  // The brand check of an operation or attribute (https://webidl.spec.whatwg.org/#dfn-create-operation-function)
  function thisRecord(object, kind) {
    const record = getRecord(object, kind);
    if (record === undefined) throw new TypeError_(`Illegal invocation: 'this' does not implement interface ${kind}`);
    return record;
  }

  // ------------------------------------------------------------------------------------------------------------------
  // Promises and abort signals
  // ------------------------------------------------------------------------------------------------------------------

  const noop = () => {};

  // A promise with its resolving functions (resolving or rejecting a settled promise does nothing)
  function newPromise() {
    const deferred = ObjectCreate(null);
    deferred.promise = new Promise_((resolve, reject) => {
      deferred.resolve = resolve;
      deferred.reject = reject;
    });
    return deferred;
  }

  // https://webidl.spec.whatwg.org/#a-promise-resolved-with
  const promiseResolvedWith = (value) => new Promise_((resolve) => resolve(value));
  const promiseRejectedWith = (reason) => apply(promiseRejectFunction, Promise_, [reason]);
  const performPromiseThen = (promise, onFulfilled, onRejected) => apply(promiseThen, promise, [onFulfilled, onRejected]);
  const uponPromise = (promise, onFulfilled, onRejected) => { performPromiseThen(performPromiseThen(promise, onFulfilled, onRejected), undefined, noop); };

  // Runs the steps of an operation that returns a promise: the exceptions are converted to rejected promises
  function promiseOperation(steps) {
    try {
      return steps();
    } catch (error) {
      return promiseRejectedWith(error);
    }
  }

  const newAbortController = () => new AbortController_();
  const controllerSignal = (controller) => apply(abortControllerSignal, controller, []);
  const signalAbort = (controller, reason) => { apply(abortControllerAbort, controller, [reason]); };
  const signalAbortWithoutReason = (controller) => { apply(abortControllerAbort, controller, []); };
  const isAborted = (signal) => apply(abortSignalAborted, signal, []);
  const abortReason = (signal) => apply(abortSignalReason, signal, []);

  // https://dom.spec.whatwg.org/#create-a-dependent-abort-signal
  const dependentSignal = (signals) => apply(abortSignalAny, AbortSignal_, [signals]);

  function isAbortSignal(value) {
    if (!isObject(value)) return false;
    try {
      apply(abortSignalAborted, value, []);
      return true;
    } catch {
      return false;
    }
  }

  // ------------------------------------------------------------------------------------------------------------------
  // WebIDL conversions (https://webidl.spec.whatwg.org/#es-type-mapping)
  // ------------------------------------------------------------------------------------------------------------------

  function requireArguments(count, required, operation) {
    if (count < required) throw new TypeError_(`Failed to execute '${operation}': ${required} argument${required > 1 ? 's' : ''} required, but only ${count} present`);
  }

  function convertDictionary(value, context) {
    if (value !== undefined && value !== null && !isObject(value)) throw new TypeError_(`${context} is not an object`);
    return value;
  }

  const getMember = (dictionary, key) => dictionary === undefined || dictionary === null ? undefined : dictionary[key];

  function convertCallback(value, context) {
    if (typeof value !== 'function') throw new TypeError_(`${context} is not a function`);
    return value;
  }

  function convertOptionalCallback(value, context) {
    return value === undefined ? undefined : convertCallback(value, context);
  }

  // unsigned long long (https://webidl.spec.whatwg.org/#es-unsigned-long-long), without [EnforceRange] or [Clamp]
  function convertUnsignedLongLong(value) {
    let x = +value;
    if (x !== x || x === Infinity || x === -Infinity) return 0;
    x = MathTrunc(x) % 18446744073709551616;
    if (x < 0) x += 18446744073709551616;
    return x === 0 ? 0 : x;
  }

  // SubscribeOptions: the signal is undefined when the member is not present
  function convertSubscribeOptions(value) {
    convertDictionary(value, 'options');
    const signal = getMember(value, 'signal');
    if (signal !== undefined && !isAbortSignal(signal)) throw new TypeError_("Failed to read the 'signal' property from 'SubscribeOptions': the value is not an AbortSignal");
    const result = ObjectCreate(null);
    result.signal = signal;
    return result;
  }

  // ObserverUnion: (ObservableSubscriptionCallback or SubscriptionObserver), converted to an internal observer that
  // invokes the callbacks with the "report" exception behavior
  function convertObserverUnion(value) {
    const observer = newInternalObserver();
    if (typeof value === 'function') {
      observer.next = (v) => invokeAndReport(value, [v]);
      return observer;
    }

    convertDictionary(value, 'observer');
    // The members of a dictionary are read in lexicographical order
    const complete = convertOptionalCallback(getMember(value, 'complete'), "The 'complete' member of the observer");
    const error = convertOptionalCallback(getMember(value, 'error'), "The 'error' member of the observer");
    const next = convertOptionalCallback(getMember(value, 'next'), "The 'next' member of the observer");
    if (next !== undefined) observer.next = (v) => invokeAndReport(next, [v]);
    if (error !== undefined) observer.error = (e) => invokeAndReport(error, [e]);
    if (complete !== undefined) observer.complete = () => invokeAndReport(complete, []);
    return observer;
  }

  // Invokes a callback function with the "report" exception behavior (https://webidl.spec.whatwg.org/#invoke-a-callback-function)
  function invokeAndReport(callback, args) {
    try {
      apply(callback, undefined, args);
    } catch (error) {
      reportException(error);
    }
  }

  // ------------------------------------------------------------------------------------------------------------------
  // Subscriber (https://wicg.github.io/observable/#subscriber-api)
  // ------------------------------------------------------------------------------------------------------------------

  // https://wicg.github.io/observable/#internal-observer: the error steps default to the default error algorithm
  function newInternalObserver() {
    const observer = ObjectCreate(null);
    observer.next = noop;
    observer.error = reportException;
    observer.complete = noop;
    return observer;
  }

  class Subscriber {
    constructor() {
      throw new TypeError_('Illegal constructor');
    }

    next(value) {
      const subscriber = thisRecord(this, 'Subscriber');
      requireArguments(arguments.length, 1, "next' on 'Subscriber");
      subscriberNext(subscriber, value);
    }

    error(error) {
      const subscriber = thisRecord(this, 'Subscriber');
      requireArguments(arguments.length, 1, "error' on 'Subscriber");
      subscriberError(subscriber, error);
    }

    complete() {
      subscriberComplete(thisRecord(this, 'Subscriber'));
    }

    addTeardown(teardown) {
      const subscriber = thisRecord(this, 'Subscriber');
      requireArguments(arguments.length, 1, "addTeardown' on 'Subscriber");
      convertCallback(teardown, 'The teardown');
      if (!isFullyActive()) return;
      if (subscriber.active) {
        push(subscriber.teardowns, teardown);
      } else {
        invokeAndReport(teardown, []);
      }
    }

    get active() { return thisRecord(this, 'Subscriber').active; }

    get signal() { return controllerSignal(thisRecord(this, 'Subscriber').controller); }
  }

  defineInterface(Subscriber, 'Subscriber');

  function createSubscriber(observable) {
    const subscriber = createRecord(ObjectCreate(Subscriber.prototype), 'Subscriber');
    subscriber.observable = observable;
    subscriber.observers = [];
    subscriber.teardowns = [];
    subscriber.controller = newAbortController();
    subscriber.active = true;
    return subscriber;
  }

  const subscriberSignal = (subscriber) => controllerSignal(subscriber.controller);

  // https://wicg.github.io/observable/#dom-subscriber-next
  function subscriberNext(subscriber, value) {
    if (!subscriber.active || !isFullyActive()) return;

    // A copy of the observers: a next steps can subscribe another observer
    const observers = copy(subscriber.observers);
    for (let i = 0; i < observers.length; i++) {
      observers[i].next(value);
    }
  }

  // https://wicg.github.io/observable/#dom-subscriber-error
  function subscriberError(subscriber, error) {
    if (!subscriber.active) {
      reportException(error);
      return;
    }

    if (!isFullyActive()) return;
    closeSubscription(subscriber, true, error);
    const observers = copy(subscriber.observers);
    for (let i = 0; i < observers.length; i++) {
      observers[i].error(error);
    }
  }

  // https://wicg.github.io/observable/#dom-subscriber-complete
  function subscriberComplete(subscriber) {
    if (!subscriber.active || !isFullyActive()) return;
    closeSubscription(subscriber, false);
    const observers = copy(subscriber.observers);
    for (let i = 0; i < observers.length; i++) {
      observers[i].complete();
    }
  }

  // https://wicg.github.io/observable/#close-a-subscription
  function closeSubscription(subscriber, hasReason, reason) {
    if (!subscriber.active) return;
    subscriber.active = false;

    // The weak subscriber of the Observable is shared only while it is active: an inactive one does not keep its observers alive
    if (subscriber.observable.weakSubscriber === subscriber) subscriber.observable.weakSubscriber = null;
    if (hasReason) {
      signalAbort(subscriber.controller, reason);
    } else {
      signalAbortWithoutReason(subscriber.controller);
    }

    const teardowns = subscriber.teardowns;
    for (let i = teardowns.length - 1; i >= 0; i--) {
      // A teardown can make the document inactive
      if (!isFullyActive()) return;
      invokeAndReport(teardowns[i], []);
    }
  }

  // ------------------------------------------------------------------------------------------------------------------
  // Observable (https://wicg.github.io/observable/#observable-api)
  // ------------------------------------------------------------------------------------------------------------------

  class Observable {
    constructor(callback) {
      requireArguments(arguments.length, 1, "construct 'Observable");
      convertCallback(callback, 'The subscribe callback');
      const observable = createRecord(this, 'Observable');
      observable.callback = callback;
      observable.nativeCallback = undefined;
      observable.weakSubscriber = null;
    }

    subscribe(observer = undefined, options = undefined) {
      const observable = thisRecord(this, 'Observable');
      const internalObserver = convertObserverUnion(observer);
      const subscribeOptions = convertSubscribeOptions(options);
      subscribeTo(observable, internalObserver, subscribeOptions);
    }

    static from(value) {
      requireArguments(arguments.length, 1, "from' on 'Observable");
      return convertToObservable(value).object;
    }

    takeUntil(value) {
      const sourceObservable = thisRecord(this, 'Observable');
      requireArguments(arguments.length, 1, "takeUntil' on 'Observable");
      return takeUntil(sourceObservable, convertToObservable(value));
    }

    map(mapper) {
      const sourceObservable = thisRecord(this, 'Observable');
      requireArguments(arguments.length, 1, "map' on 'Observable");
      return map(sourceObservable, convertCallback(mapper, 'The mapper'));
    }

    filter(predicate) {
      const sourceObservable = thisRecord(this, 'Observable');
      requireArguments(arguments.length, 1, "filter' on 'Observable");
      return filter(sourceObservable, convertCallback(predicate, 'The predicate'));
    }

    take(amount) {
      const sourceObservable = thisRecord(this, 'Observable');
      requireArguments(arguments.length, 1, "take' on 'Observable");
      return take(sourceObservable, convertUnsignedLongLong(amount));
    }

    drop(amount) {
      const sourceObservable = thisRecord(this, 'Observable');
      requireArguments(arguments.length, 1, "drop' on 'Observable");
      return drop(sourceObservable, convertUnsignedLongLong(amount));
    }

    flatMap(mapper) {
      const sourceObservable = thisRecord(this, 'Observable');
      requireArguments(arguments.length, 1, "flatMap' on 'Observable");
      return flatMap(sourceObservable, convertCallback(mapper, 'The mapper'));
    }

    switchMap(mapper) {
      const sourceObservable = thisRecord(this, 'Observable');
      requireArguments(arguments.length, 1, "switchMap' on 'Observable");
      return switchMap(sourceObservable, convertCallback(mapper, 'The mapper'));
    }

    inspect(inspectorUnion = undefined) {
      const sourceObservable = thisRecord(this, 'Observable');
      return inspect(sourceObservable, inspectorUnion);
    }

    catch(callback) {
      const sourceObservable = thisRecord(this, 'Observable');
      requireArguments(arguments.length, 1, "catch' on 'Observable");
      return catchOperator(sourceObservable, convertCallback(callback, 'The callback'));
    }

    finally(callback) {
      const sourceObservable = thisRecord(this, 'Observable');
      requireArguments(arguments.length, 1, "finally' on 'Observable");
      return finallyOperator(sourceObservable, convertCallback(callback, 'The callback'));
    }

    toArray(options = undefined) {
      return promiseOperation(() => toArray(thisRecord(this, 'Observable'), convertSubscribeOptions(options)));
    }

    forEach(callback, options = undefined) {
      const count = arguments.length;
      return promiseOperation(() => {
        const observable = thisRecord(this, 'Observable');
        requireArguments(count, 1, "forEach' on 'Observable");
        return forEach(observable, convertCallback(callback, 'The callback'), convertSubscribeOptions(options));
      });
    }

    every(predicate, options = undefined) {
      const count = arguments.length;
      return promiseOperation(() => {
        const observable = thisRecord(this, 'Observable');
        requireArguments(count, 1, "every' on 'Observable");
        return every(observable, convertCallback(predicate, 'The predicate'), convertSubscribeOptions(options));
      });
    }

    first(options = undefined) {
      return promiseOperation(() => first(thisRecord(this, 'Observable'), convertSubscribeOptions(options)));
    }

    last(options = undefined) {
      return promiseOperation(() => last(thisRecord(this, 'Observable'), convertSubscribeOptions(options)));
    }

    find(predicate, options = undefined) {
      const count = arguments.length;
      return promiseOperation(() => {
        const observable = thisRecord(this, 'Observable');
        requireArguments(count, 1, "find' on 'Observable");
        return find(observable, convertCallback(predicate, 'The predicate'), convertSubscribeOptions(options));
      });
    }

    some(predicate, options = undefined) {
      const count = arguments.length;
      return promiseOperation(() => {
        const observable = thisRecord(this, 'Observable');
        requireArguments(count, 1, "some' on 'Observable");
        return some(observable, convertCallback(predicate, 'The predicate'), convertSubscribeOptions(options));
      });
    }

    reduce(reducer, initialValue = undefined, options = undefined) {
      const count = arguments.length;
      return promiseOperation(() => {
        const observable = thisRecord(this, 'Observable');
        requireArguments(count, 1, "reduce' on 'Observable");
        convertCallback(reducer, 'The reducer');
        // An optional argument whose value is undefined is missing (https://webidl.spec.whatwg.org/#es-overloads)
        const hasInitialValue = initialValue !== undefined;
        return reduce(observable, reducer, hasInitialValue, initialValue, convertSubscribeOptions(options));
      });
    }
  }

  defineInterface(Observable, 'Observable');

  // A new Observable whose subscribe callback is a set of steps that take a subscriber
  function createNativeObservable(steps) {
    const observable = createRecord(ObjectCreate(Observable.prototype), 'Observable');
    observable.callback = undefined;
    observable.nativeCallback = steps;
    observable.weakSubscriber = null;
    return observable;
  }

  // The observer of an operator that passes the values, the error and the completion to a subscriber
  function passThroughObserver(subscriber) {
    const observer = newInternalObserver();
    observer.next = (value) => subscriberNext(subscriber, value);
    observer.error = (error) => subscriberError(subscriber, error);
    observer.complete = () => subscriberComplete(subscriber);
    return observer;
  }

  function signalOptions(signal) {
    const options = ObjectCreate(null);
    options.signal = signal;
    return options;
  }

  // https://wicg.github.io/observable/#observable-subscribe-to-an-observable
  function subscribeTo(observable, internalObserver, options) {
    if (!isFullyActive()) return;

    // A subscription of an Observable whose producer is still active shares the subscriber of the producer
    const signal = options.signal;
    const existing = observable.weakSubscriber;
    if (existing !== null && existing.active) {
      push(existing.observers, internalObserver);
      if (signal !== undefined) {
        if (isAborted(signal)) {
          remove(existing.observers, internalObserver);
        } else {
          addAbortAlgorithm(signal, () => removeObserver(existing, internalObserver, signal));
        }
      }

      return;
    }

    const subscriber = createSubscriber(observable);
    push(subscriber.observers, internalObserver);
    observable.weakSubscriber = subscriber;
    if (signal !== undefined) {
      if (isAborted(signal)) {
        closeSubscription(subscriber, true, abortReason(signal));
      } else {
        addAbortAlgorithm(signal, () => removeObserver(subscriber, internalObserver, signal));
      }
    }

    if (observable.callback !== undefined) {
      try {
        apply(observable.callback, undefined, [subscriber.object]);
      } catch (error) {
        subscriberError(subscriber, error);
      }
    } else {
      observable.nativeCallback(subscriber);
    }
  }

  function removeObserver(subscriber, internalObserver, signal) {
    if (!subscriber.active) return;
    remove(subscriber.observers, internalObserver);
    if (subscriber.observers.length === 0) closeSubscription(subscriber, true, abortReason(signal));
  }

  // ------------------------------------------------------------------------------------------------------------------
  // Conversion to an Observable (https://wicg.github.io/observable/#convert-to-an-observable)
  // ------------------------------------------------------------------------------------------------------------------

  // https://tc39.es/ecma262/#sec-getmethod
  function getMethod(value, key) {
    const method = value[key];
    if (method === undefined || method === null) return undefined;
    if (typeof method !== 'function') throw new TypeError_(`The ${typeof key === 'symbol' ? key.description : key} method is not callable`);
    return method;
  }

  // The next method of an async iterator is read by the first IteratorNext: the errors of its getter reject the promise
  // of the next value, with microtask timing, like the other errors of the async iteration
  function createIteratorRecord(iterator, lazyNextMethod = false) {
    if (!isObject(iterator)) throw new TypeError_('The iterator is not an object');
    const record = ObjectCreate(null);
    record.iterator = iterator;
    record.hasNextMethod = !lazyNextMethod;
    record.nextMethod = lazyNextMethod ? undefined : iterator.next;
    record.done = false;
    return record;
  }

  // https://tc39.es/ecma262/#sec-getiterator
  function getIterator(value, async) {
    if (async) {
      const method = getMethod(value, SymbolAsyncIterator);
      if (method === undefined) {
        const syncMethod = getMethod(value, SymbolIterator);
        if (syncMethod === undefined) throw new TypeError_('The object is not iterable');
        return createAsyncFromSyncIterator(createIteratorRecord(apply(syncMethod, value, [])));
      }

      return createIteratorRecord(apply(method, value, []), true);
    }

    const method = getMethod(value, SymbolIterator);
    if (method === undefined) throw new TypeError_('The object is not iterable');
    return createIteratorRecord(apply(method, value, []));
  }

  // https://tc39.es/ecma262/#sec-iteratornext
  function iteratorNext(record) {
    let result;
    try {
      if (!record.hasNextMethod) {
        record.nextMethod = record.iterator.next;
        record.hasNextMethod = true;
      }

      result = apply(record.nextMethod, record.iterator, []);
    } catch (error) {
      record.done = true;
      throw error;
    }

    return result;
  }

  // https://tc39.es/ecma262/#sec-iteratorclose with a normal completion: the errors are thrown
  function iteratorClose(record) {
    const returnMethod = getMethod(record.iterator, 'return');
    if (returnMethod === undefined) return;
    const result = apply(returnMethod, record.iterator, []);
    if (!isObject(result)) throw new TypeError_('The result of the iterator return() method is not an Object');
  }

  // https://webidl.spec.whatwg.org/#async-iterator-close: the return() method is called with the reason, and its errors
  // reject a promise that nobody handles
  function asyncIteratorClose(record, reason) {
    let result;
    try {
      const returnMethod = getMethod(record.iterator, 'return');
      if (returnMethod === undefined) return;
      result = apply(returnMethod, record.iterator, [reason]);
    } catch (error) {
      promiseRejectedWith(error);
      return;
    }

    performPromiseThen(promiseResolvedWith(result), (value) => {
      if (!isObject(value)) throw new TypeError_('The result of the async iterator return() method is not an Object');
    });
  }

  function createIterResultObject(value, done) {
    return { value, done };
  }

  // https://tc39.es/ecma262/#sec-createasyncfromsynciterator
  function createAsyncFromSyncIterator(syncRecord) {
    const syncIterator = syncRecord.iterator;
    const syncNextMethod = syncRecord.nextMethod;
    const closeSyncIterator = () => {
      try {
        const returnMethod = syncIterator.return;
        if (returnMethod !== undefined && returnMethod !== null) apply(returnMethod, syncIterator, []);
      } catch {
        // The completion of the iterator closing is ignored when the original completion is a throw
      }
    };
    const continuation = (result, closeOnRejection) => {
      const done = !!result.done;
      const value = result.value;
      let valueWrapper;
      try {
        valueWrapper = promiseResolvedWith(value);
      } catch (error) {
        if (!done && closeOnRejection) closeSyncIterator();
        throw error;
      }

      return performPromiseThen(valueWrapper, (v) => createIterResultObject(v, done), (reason) => {
        if (!done && closeOnRejection) closeSyncIterator();
        throw reason;
      });
    };
    const asyncIterator = {
      next(value) {
        return promiseOperation(() => {
          const result = arguments.length > 0 ? apply(syncNextMethod, syncIterator, [value]) : apply(syncNextMethod, syncIterator, []);
          if (!isObject(result)) throw new TypeError_('The iterator result is not an object');
          return continuation(result, true);
        });
      },
      return(value) {
        return promiseOperation(() => {
          const returnMethod = syncIterator.return;
          if (returnMethod === undefined || returnMethod === null) return promiseResolvedWith(createIterResultObject(value, true));
          const result = arguments.length > 0 ? apply(returnMethod, syncIterator, [value]) : apply(returnMethod, syncIterator, []);
          if (!isObject(result)) throw new TypeError_('The result of the iterator return() method is not an Object');
          return continuation(result, false);
        });
      },
    };
    return createIteratorRecord(asyncIterator);
  }

  // https://wicg.github.io/observable/#convert-to-an-observable: returns the record of the Observable
  function convertToObservable(value) {
    if (!isObject(value)) throw new TypeError_('The value cannot be converted to an Observable');
    const existing = getRecord(value, 'Observable');
    if (existing !== undefined) return existing;

    // From async iterable
    const asyncIteratorMethod = getMethod(value, SymbolAsyncIterator);
    if (asyncIteratorMethod !== undefined) {
      return createNativeObservable((subscriber) => {
        const signal = subscriberSignal(subscriber);
        if (isAborted(signal)) return;
        let iteratorRecord;
        try {
          iteratorRecord = getIterator(value, true);
        } catch (error) {
          subscriberError(subscriber, error);
          return;
        }

        if (isAborted(signal)) return;
        // The iterator is not closed when the iteration has completed
        addAbortAlgorithm(signal, () => {
          if (!iteratorRecord.done) asyncIteratorClose(iteratorRecord, abortReason(signal));
        });
        asyncIterableNext(subscriber, iteratorRecord);
      });
    }

    // From iterable
    const iteratorMethod = getMethod(value, SymbolIterator);
    if (iteratorMethod !== undefined) {
      return createNativeObservable((subscriber) => {
        const signal = subscriberSignal(subscriber);
        if (isAborted(signal)) return;
        let iteratorRecord;
        try {
          iteratorRecord = getIterator(value, false);
        } catch (error) {
          subscriberError(subscriber, error);
          return;
        }

        if (isAborted(signal)) return;
        // The iterator is not closed when the iteration has completed (e.g. the abort of the completed subscription)
        addAbortAlgorithm(signal, () => {
          if (!iteratorRecord.done) iteratorClose(iteratorRecord);
        });
        while (true) {
          let done;
          let nextValue;
          try {
            // https://tc39.es/ecma262/#sec-iteratorstepvalue
            const result = iteratorNext(iteratorRecord);
            if (!isObject(result)) {
              iteratorRecord.done = true;
              throw new TypeError_('The iterator result is not an object');
            }

            try {
              done = !!result.done;
              if (!done) nextValue = result.value;
            } catch (error) {
              iteratorRecord.done = true;
              throw error;
            }
          } catch (error) {
            subscriberError(subscriber, error);
            break;
          }

          if (done) {
            iteratorRecord.done = true;
            subscriberComplete(subscriber);
            return;
          }

          subscriberNext(subscriber, nextValue);
          if (isAborted(signal)) break;
        }
      });
    }

    // From Promise
    if (isPromise(value)) {
      return createNativeObservable((subscriber) => {
        uponPromise(value, (v) => {
          subscriberNext(subscriber, v);
          subscriberComplete(subscriber);
        }, (reason) => subscriberError(subscriber, reason));
      });
    }

    throw new TypeError_('The value cannot be converted to an Observable');
  }

  // https://tc39.es/ecma262/#sec-ispromise
  function isPromise(value) {
    try {
      apply(promiseThen, value, [undefined, undefined]);
      return true;
    } catch {
      return false;
    }
  }

  // The nextAlgorithm of the conversion of an async iterable
  function asyncIterableNext(subscriber, iteratorRecord) {
    if (isAborted(subscriberSignal(subscriber))) return;
    let nextPromise;
    try {
      nextPromise = promiseResolvedWith(iteratorNext(iteratorRecord));
    } catch (error) {
      nextPromise = promiseRejectedWith(error);
    }

    uponPromise(nextPromise, (iteratorResult) => {
      if (!isObject(iteratorResult)) {
        subscriberError(subscriber, new TypeError_('The iterator result is not an object'));
        return;
      }

      let done;
      try {
        done = !!iteratorResult.done;
      } catch (error) {
        subscriberError(subscriber, error);
        return;
      }

      if (done) {
        iteratorRecord.done = true;
        subscriberComplete(subscriber);
        return;
      }

      let value;
      try {
        value = iteratorResult.value;
      } catch (error) {
        subscriberError(subscriber, error);
        return;
      }

      subscriberNext(subscriber, value);
      asyncIterableNext(subscriber, iteratorRecord);
    }, (reason) => subscriberError(subscriber, reason));
  }

  // Converts the result of a callback to an Observable, or errors the subscriber
  function tryConvertToObservable(subscriber, value) {
    try {
      return convertToObservable(value);
    } catch (error) {
      subscriberError(subscriber, error);
      return undefined;
    }
  }

  // ------------------------------------------------------------------------------------------------------------------
  // Observable-returning operators (https://wicg.github.io/observable/#observable-returning-operators)
  // ------------------------------------------------------------------------------------------------------------------

  function takeUntil(sourceObservable, notifier) {
    return createNativeObservable((subscriber) => {
      const notifierObserver = newInternalObserver();
      notifierObserver.next = () => subscriberComplete(subscriber);
      notifierObserver.error = () => subscriberComplete(subscriber);
      const options = signalOptions(subscriberSignal(subscriber));
      subscribeTo(notifier, notifierObserver, options);
      if (!subscriber.active) return;
      subscribeTo(sourceObservable, passThroughObserver(subscriber), options);
    }).object;
  }

  function map(sourceObservable, mapper) {
    return createNativeObservable((subscriber) => {
      let idx = 0;
      const sourceObserver = passThroughObserver(subscriber);
      sourceObserver.next = (value) => {
        let mappedValue;
        try {
          mappedValue = apply(mapper, undefined, [value, idx]);
        } catch (error) {
          subscriberError(subscriber, error);
          return;
        }

        idx++;
        subscriberNext(subscriber, mappedValue);
      };
      subscribeTo(sourceObservable, sourceObserver, signalOptions(subscriberSignal(subscriber)));
    }).object;
  }

  function filter(sourceObservable, predicate) {
    return createNativeObservable((subscriber) => {
      let idx = 0;
      const sourceObserver = passThroughObserver(subscriber);
      sourceObserver.next = (value) => {
        let matches;
        try {
          matches = !!apply(predicate, undefined, [value, idx]);
        } catch (error) {
          subscriberError(subscriber, error);
          return;
        }

        idx++;
        if (matches) subscriberNext(subscriber, value);
      };
      subscribeTo(sourceObservable, sourceObserver, signalOptions(subscriberSignal(subscriber)));
    }).object;
  }

  function take(sourceObservable, amount) {
    return createNativeObservable((subscriber) => {
      let remaining = amount;
      if (remaining === 0) {
        subscriberComplete(subscriber);
        return;
      }

      const sourceObserver = passThroughObserver(subscriber);
      sourceObserver.next = (value) => {
        subscriberNext(subscriber, value);
        remaining--;
        if (remaining === 0) subscriberComplete(subscriber);
      };
      subscribeTo(sourceObservable, sourceObserver, signalOptions(subscriberSignal(subscriber)));
    }).object;
  }

  function drop(sourceObservable, amount) {
    return createNativeObservable((subscriber) => {
      let remaining = amount;
      const sourceObserver = passThroughObserver(subscriber);
      sourceObserver.next = (value) => {
        if (remaining > 0) {
          remaining--;
          return;
        }

        subscriberNext(subscriber, value);
      };
      subscribeTo(sourceObservable, sourceObserver, signalOptions(subscriberSignal(subscriber)));
    }).object;
  }

  function flatMap(sourceObservable, mapper) {
    return createNativeObservable((subscriber) => {
      const state = ObjectCreate(null);
      state.idx = 0;
      state.outerSubscriptionHasCompleted = false;
      state.queue = [];
      state.queueHead = 0;
      state.activeInnerSubscription = false;
      const sourceObserver = newInternalObserver();
      sourceObserver.next = (value) => {
        if (state.activeInnerSubscription) {
          push(state.queue, value);
        } else {
          state.activeInnerSubscription = true;
          flatMapProcessNextValue(value, subscriber, mapper, state);
        }
      };
      sourceObserver.error = (error) => subscriberError(subscriber, error);
      sourceObserver.complete = () => {
        state.outerSubscriptionHasCompleted = true;
        if (!state.activeInnerSubscription && state.queue.length === state.queueHead) subscriberComplete(subscriber);
      };
      subscribeTo(sourceObservable, sourceObserver, signalOptions(subscriberSignal(subscriber)));
    }).object;
  }

  // https://wicg.github.io/observable/#flatmap-process-next-value-steps
  function flatMapProcessNextValue(value, subscriber, mapper, state) {
    let mappedResult;
    try {
      mappedResult = apply(mapper, undefined, [value, state.idx]);
    } catch (error) {
      subscriberError(subscriber, error);
      return;
    }

    state.idx++;
    const innerObservable = tryConvertToObservable(subscriber, mappedResult);
    if (innerObservable === undefined) return;
    const innerObserver = newInternalObserver();
    innerObserver.next = (v) => subscriberNext(subscriber, v);
    innerObserver.error = (error) => subscriberError(subscriber, error);
    innerObserver.complete = () => {
      if (state.queue.length !== state.queueHead) {
        const nextValue = state.queue[state.queueHead];
        state.queue[state.queueHead] = undefined;
        state.queueHead++;
        if (state.queueHead === state.queue.length) {
          state.queue = [];
          state.queueHead = 0;
        }

        flatMapProcessNextValue(nextValue, subscriber, mapper, state);
      } else {
        state.activeInnerSubscription = false;
        if (state.outerSubscriptionHasCompleted) subscriberComplete(subscriber);
      }
    };
    subscribeTo(innerObservable, innerObserver, signalOptions(subscriberSignal(subscriber)));
  }

  function switchMap(sourceObservable, mapper) {
    return createNativeObservable((subscriber) => {
      const state = ObjectCreate(null);
      state.idx = 0;
      state.outerSubscriptionHasCompleted = false;
      state.activeInnerAbortController = null;
      const sourceObserver = newInternalObserver();
      sourceObserver.next = (value) => {
        if (state.activeInnerAbortController !== null) signalAbortWithoutReason(state.activeInnerAbortController);
        state.activeInnerAbortController = newAbortController();
        switchMapProcessNextValue(value, subscriber, mapper, state);
      };
      sourceObserver.error = (error) => subscriberError(subscriber, error);
      sourceObserver.complete = () => {
        state.outerSubscriptionHasCompleted = true;
        if (state.activeInnerAbortController === null) subscriberComplete(subscriber);
      };
      subscribeTo(sourceObservable, sourceObserver, signalOptions(subscriberSignal(subscriber)));
    }).object;
  }

  // https://wicg.github.io/observable/#switchmap-process-next-value-steps
  function switchMapProcessNextValue(value, subscriber, mapper, state) {
    let mappedResult;
    try {
      mappedResult = apply(mapper, undefined, [value, state.idx]);
    } catch (error) {
      subscriberError(subscriber, error);
      return;
    }

    state.idx++;
    const innerObservable = tryConvertToObservable(subscriber, mappedResult);
    if (innerObservable === undefined) return;
    const innerObserver = newInternalObserver();
    innerObserver.next = (v) => subscriberNext(subscriber, v);
    innerObserver.error = (error) => subscriberError(subscriber, error);
    innerObserver.complete = () => {
      if (state.outerSubscriptionHasCompleted) {
        subscriberComplete(subscriber);
      } else {
        state.activeInnerAbortController = null;
      }
    };
    const signal = dependentSignal([controllerSignal(state.activeInnerAbortController), subscriberSignal(subscriber)]);
    subscribeTo(innerObservable, innerObserver, signalOptions(signal));
  }

  function inspect(sourceObservable, inspectorUnion) {
    let subscribeCallback;
    let nextCallback;
    let errorCallback;
    let completeCallback;
    let abortCallback;
    if (typeof inspectorUnion === 'function') {
      nextCallback = inspectorUnion;
    } else {
      convertDictionary(inspectorUnion, 'inspectorUnion');
      // The members of a dictionary are read in lexicographical order
      abortCallback = convertOptionalCallback(getMember(inspectorUnion, 'abort'), "The 'abort' member of the inspector");
      completeCallback = convertOptionalCallback(getMember(inspectorUnion, 'complete'), "The 'complete' member of the inspector");
      errorCallback = convertOptionalCallback(getMember(inspectorUnion, 'error'), "The 'error' member of the inspector");
      nextCallback = convertOptionalCallback(getMember(inspectorUnion, 'next'), "The 'next' member of the inspector");
      subscribeCallback = convertOptionalCallback(getMember(inspectorUnion, 'subscribe'), "The 'subscribe' member of the inspector");
    }

    return createNativeObservable((subscriber) => {
      if (subscribeCallback !== undefined) {
        try {
          apply(subscribeCallback, undefined, []);
        } catch (error) {
          subscriberError(subscriber, error);
          return;
        }
      }

      const signal = subscriberSignal(subscriber);
      let removeAbortAlgorithm = noop;
      if (abortCallback !== undefined) {
        removeAbortAlgorithm = addAbortAlgorithm(signal, () => invokeAndReport(abortCallback, [abortReason(signal)]));
      }

      const sourceObserver = newInternalObserver();
      sourceObserver.next = (value) => {
        if (nextCallback !== undefined) {
          try {
            apply(nextCallback, undefined, [value]);
          } catch (error) {
            removeAbortAlgorithm();
            subscriberError(subscriber, error);
            return;
          }
        }

        subscriberNext(subscriber, value);
      };
      sourceObserver.error = (error) => {
        removeAbortAlgorithm();
        if (errorCallback !== undefined) {
          try {
            apply(errorCallback, undefined, [error]);
          } catch (e) {
            subscriberError(subscriber, e);
            return;
          }
        }

        subscriberError(subscriber, error);
      };
      sourceObserver.complete = () => {
        removeAbortAlgorithm();
        if (completeCallback !== undefined) {
          try {
            apply(completeCallback, undefined, []);
          } catch (error) {
            subscriberError(subscriber, error);
            return;
          }
        }

        subscriberComplete(subscriber);
      };
      subscribeTo(sourceObservable, sourceObserver, signalOptions(signal));
    }).object;
  }

  function catchOperator(sourceObservable, callback) {
    return createNativeObservable((subscriber) => {
      const sourceObserver = passThroughObserver(subscriber);
      sourceObserver.error = (error) => {
        let result;
        try {
          result = apply(callback, undefined, [error]);
        } catch (e) {
          subscriberError(subscriber, e);
          return;
        }

        const innerObservable = tryConvertToObservable(subscriber, result);
        if (innerObservable === undefined) return;
        subscribeTo(innerObservable, passThroughObserver(subscriber), signalOptions(subscriberSignal(subscriber)));
      };
      subscribeTo(sourceObservable, sourceObserver, signalOptions(subscriberSignal(subscriber)));
    }).object;
  }

  function finallyOperator(sourceObservable, callback) {
    return createNativeObservable((subscriber) => {
      // https://wicg.github.io/observable/#dom-subscriber-addteardown
      if (isFullyActive()) {
        if (subscriber.active) {
          push(subscriber.teardowns, callback);
        } else {
          invokeAndReport(callback, []);
        }
      }

      subscribeTo(sourceObservable, passThroughObserver(subscriber), signalOptions(subscriberSignal(subscriber)));
    }).object;
  }

  // ------------------------------------------------------------------------------------------------------------------
  // Promise-returning operators (https://wicg.github.io/observable/#promise-returning-operators)
  // ------------------------------------------------------------------------------------------------------------------

  // Rejects the promise when the signal aborts; returns false when the signal is already aborted
  function rejectOnAbort(p, signal) {
    if (isAborted(signal)) {
      p.reject(abortReason(signal));
      return false;
    }

    addAbortAlgorithm(signal, () => p.reject(abortReason(signal)));
    return true;
  }

  // The internal options of the operators that can stop the subscription: a signal dependent on a controller of the
  // operator and on the signal of the options
  function internalOptions(controller, options) {
    const signals = [controllerSignal(controller)];
    if (options.signal !== undefined) push(signals, options.signal);
    return signalOptions(dependentSignal(signals));
  }

  function toArray(observable, options) {
    const p = newPromise();
    if (options.signal !== undefined && !rejectOnAbort(p, options.signal)) return p.promise;
    const values = [];
    const observer = newInternalObserver();
    observer.next = (value) => push(values, value);
    observer.error = (error) => p.reject(error);
    observer.complete = () => p.resolve(values);
    subscribeTo(observable, observer, options);
    return p.promise;
  }

  function forEach(observable, callback, options) {
    const p = newPromise();
    const visitorCallbackController = newAbortController();
    const internal = internalOptions(visitorCallbackController, options);
    if (!rejectOnAbort(p, internal.signal)) return p.promise;
    let idx = 0;
    const observer = newInternalObserver();
    observer.next = (value) => {
      try {
        apply(callback, undefined, [value, idx]);
      } catch (error) {
        p.reject(error);
        signalAbort(visitorCallbackController, error);
        return;
      }

      idx++;
    };
    observer.error = (error) => p.reject(error);
    observer.complete = () => p.resolve(undefined);
    subscribeTo(observable, observer, internal);
    return p.promise;
  }

  function every(observable, predicate, options) {
    const p = newPromise();
    const controller = newAbortController();
    const internal = internalOptions(controller, options);
    if (!rejectOnAbort(p, internal.signal)) return p.promise;
    let idx = 0;
    const observer = newInternalObserver();
    observer.next = (value) => {
      let passed;
      try {
        passed = !!apply(predicate, undefined, [value, idx]);
      } catch (error) {
        p.reject(error);
        signalAbort(controller, error);
        return;
      }

      idx++;
      if (!passed) {
        p.resolve(false);
        signalAbortWithoutReason(controller);
      }
    };
    observer.error = (error) => p.reject(error);
    observer.complete = () => p.resolve(true);
    subscribeTo(observable, observer, internal);
    return p.promise;
  }

  function first(observable, options) {
    const p = newPromise();
    const controller = newAbortController();
    const internal = internalOptions(controller, options);
    if (!rejectOnAbort(p, internal.signal)) return p.promise;
    const observer = newInternalObserver();
    observer.next = (value) => {
      p.resolve(value);
      signalAbortWithoutReason(controller);
    };
    observer.error = (error) => p.reject(error);
    observer.complete = () => p.reject(new RangeError_('No values in Observable'));
    subscribeTo(observable, observer, internal);
    return p.promise;
  }

  function last(observable, options) {
    const p = newPromise();
    if (options.signal !== undefined && !rejectOnAbort(p, options.signal)) return p.promise;
    let lastValue;
    let hasLastValue = false;
    const observer = newInternalObserver();
    observer.next = (value) => {
      hasLastValue = true;
      lastValue = value;
    };
    observer.error = (error) => p.reject(error);
    observer.complete = () => {
      if (hasLastValue) {
        p.resolve(lastValue);
      } else {
        p.reject(new RangeError_('No values in Observable'));
      }
    };
    subscribeTo(observable, observer, options);
    return p.promise;
  }

  function find(observable, predicate, options) {
    const p = newPromise();
    const controller = newAbortController();
    const internal = internalOptions(controller, options);
    if (!rejectOnAbort(p, internal.signal)) return p.promise;
    let idx = 0;
    const observer = newInternalObserver();
    observer.next = (value) => {
      let passed;
      try {
        passed = !!apply(predicate, undefined, [value, idx]);
      } catch (error) {
        p.reject(error);
        signalAbort(controller, error);
        return;
      }

      idx++;
      if (passed) {
        p.resolve(value);
        signalAbortWithoutReason(controller);
      }
    };
    observer.error = (error) => p.reject(error);
    observer.complete = () => p.resolve(undefined);
    subscribeTo(observable, observer, internal);
    return p.promise;
  }

  function some(observable, predicate, options) {
    const p = newPromise();
    const controller = newAbortController();
    const internal = internalOptions(controller, options);
    if (!rejectOnAbort(p, internal.signal)) return p.promise;
    let idx = 0;
    const observer = newInternalObserver();
    observer.next = (value) => {
      let passed;
      try {
        passed = !!apply(predicate, undefined, [value, idx]);
      } catch (error) {
        p.reject(error);
        signalAbort(controller, error);
        return;
      }

      idx++;
      if (passed) {
        p.resolve(true);
        signalAbortWithoutReason(controller);
      }
    };
    observer.error = (error) => p.reject(error);
    observer.complete = () => p.resolve(false);
    subscribeTo(observable, observer, internal);
    return p.promise;
  }

  function reduce(observable, reducer, hasInitialValue, initialValue, options) {
    const p = newPromise();
    const controller = newAbortController();
    const internal = internalOptions(controller, options);
    if (!rejectOnAbort(p, internal.signal)) return p.promise;
    let idx = 0;
    let hasAccumulator = hasInitialValue;
    let accumulator = initialValue;
    const observer = newInternalObserver();
    observer.next = (value) => {
      if (!hasAccumulator) {
        hasAccumulator = true;
        accumulator = value;
        idx++;
        return;
      }

      let result;
      try {
        result = apply(reducer, undefined, [accumulator, value, idx]);
      } catch (error) {
        p.reject(error);
        signalAbort(controller, error);
        return;
      }

      idx++;
      accumulator = result;
    };
    observer.error = (error) => p.reject(error);
    observer.complete = () => {
      if (hasAccumulator) {
        p.resolve(accumulator);
      } else {
        p.reject(new TypeError_('No values in Observable and no initial value'));
      }
    };
    subscribeTo(observable, observer, internal);
    return p.promise;
  }

  // ------------------------------------------------------------------------------------------------------------------
  // EventTarget integration (https://wicg.github.io/observable/#event-target-integration)
  // ------------------------------------------------------------------------------------------------------------------

  // The steps of when(), called with an EventTarget (the brand check is done by the host)
  function when(eventTarget, args) {
    requireArguments(args.length, 1, "when' on 'EventTarget");
    const type = `${args[0]}`;
    const options = args.length > 1 ? args[1] : undefined;
    convertDictionary(options, 'options');
    // The members of a dictionary are read in lexicographical order
    const capture = !!getMember(options, 'capture');
    const passiveValue = getMember(options, 'passive');
    const hasPassive = passiveValue !== undefined;
    const passive = !!passiveValue;
    if (!isFullyActive()) return undefined;
    return createNativeObservable((subscriber) => {
      const signal = subscriberSignal(subscriber);
      if (isAborted(signal)) return;
      const listenerOptions = { capture, once: false, signal };
      if (hasPassive) listenerOptions.passive = passive;
      apply(addEventListener, eventTarget, [type, (event) => subscriberNext(subscriber, event), listenerOptions]);
    }).object;
  }

  // ------------------------------------------------------------------------------------------------------------------
  // WebIDL interface objects
  // ------------------------------------------------------------------------------------------------------------------

  // Makes the members of a class follow WebIDL: the operations and the attributes are enumerable, the class has a
  // @@toStringTag. The lengths of the functions are the number of required arguments: the optional arguments have a default value.
  function defineInterface(constructor, name) {
    const prototype = constructor.prototype;
    for (const key of ownKeys(prototype)) {
      if (key === 'constructor') continue;
      const descriptor = getOwnPropertyDescriptor(prototype, key);
      descriptor.enumerable = typeof key === 'string';
      defineProperty(prototype, key, descriptor);
    }

    for (const key of ownKeys(constructor)) {
      if (key === 'length' || key === 'name' || key === 'prototype') continue;
      const descriptor = getOwnPropertyDescriptor(constructor, key);
      descriptor.enumerable = true;
      defineProperty(constructor, key, descriptor);
    }

    defineProperty(prototype, SymbolToStringTag, { value: name, writable: false, enumerable: false, configurable: true });
  }

  return {
    Observable,
    Subscriber,
    when,
  };
});
