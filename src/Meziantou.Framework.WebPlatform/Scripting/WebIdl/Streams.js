// The Streams Standard (https://streams.spec.whatwg.org/), the stream interfaces of the Encoding Standard
// (https://encoding.spec.whatwg.org/#interface-mixin-genericTransformStream) and Blob.stream()
// (https://w3c.github.io/FileAPI/#blob-get-stream), implemented in JavaScript with the primitives of the engine.
//
// The script is evaluated once per realm, when a stream interface is first used. It is a function called with the hooks
// of the host, and returns the interface objects and the functions used by the platform objects. The internal slots of
// the objects are stored in records (objects without prototype) associated with the objects by a WeakMap, so they are not
// observable. The intrinsics are captured when the script is evaluated, so the algorithms are not affected by the
// objects patched by the user code (e.g. Promise.prototype.then).
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
  const getPrototypeOf = Object.getPrototypeOf;
  const getter = (o, k) => getOwnPropertyDescriptor(o, k).get;
  const MathMin = Math.min;
  const MathTrunc = Math.trunc;
  const NumberIsInteger = Number.isInteger;
  const TypeError_ = TypeError;
  const RangeError_ = RangeError;
  const Promise_ = Promise;
  const promiseThen = Promise.prototype.then;
  const promiseRejectFunction = Promise.reject;
  const WeakMap_ = WeakMap;
  const weakMapGet = WeakMap.prototype.get;
  const weakMapSet = WeakMap.prototype.set;
  const SymbolAsyncIterator = Symbol.asyncIterator;
  const SymbolIterator = Symbol.iterator;
  const SymbolToStringTag = Symbol.toStringTag;
  const ArrayBuffer_ = ArrayBuffer;
  const arrayBufferSlice = ArrayBuffer.prototype.slice;
  const arrayBufferByteLength = getter(ArrayBuffer.prototype, 'byteLength');
  const arrayBufferDetached = getter(ArrayBuffer.prototype, 'detached');
  const arrayBufferResizable = getter(ArrayBuffer.prototype, 'resizable');
  const arrayBufferTransfer = ArrayBuffer.prototype.transfer;
  // %SharedArrayBuffer% (the global object has no SharedArrayBuffer property when the agent cluster is not cross-origin isolated)
  const sharedArrayBufferByteLength = typeof hooks.SharedArrayBuffer === 'function' ? getter(hooks.SharedArrayBuffer.prototype, 'byteLength') : null;
  const Uint8Array_ = Uint8Array;
  const DataView_ = DataView;
  const typedArrayPrototype = getPrototypeOf(Uint8Array.prototype);
  const typedArrayName = getter(typedArrayPrototype, SymbolToStringTag);
  const typedArrayBuffer = getter(typedArrayPrototype, 'buffer');
  const typedArrayByteOffset = getter(typedArrayPrototype, 'byteOffset');
  const typedArrayByteLength = getter(typedArrayPrototype, 'byteLength');
  const typedArrayLength = getter(typedArrayPrototype, 'length');
  const typedArraySet = typedArrayPrototype.set;
  const dataViewBuffer = getter(DataView.prototype, 'buffer');
  const dataViewByteOffset = getter(DataView.prototype, 'byteOffset');
  const dataViewByteLength = getter(DataView.prototype, 'byteLength');
  const AsyncIteratorPrototype = getPrototypeOf(getPrototypeOf(async function* () {}).prototype);
  const typedArrayConstructors = ObjectCreate(null);
  const elementSizes = ObjectCreate(null);
  for (const [name, size] of [['Int8Array', 1], ['Uint8Array', 1], ['Uint8ClampedArray', 1], ['Int16Array', 2], ['Uint16Array', 2], ['Int32Array', 4], ['Uint32Array', 4], ['Float16Array', 2], ['Float32Array', 4], ['Float64Array', 8], ['BigInt64Array', 8], ['BigUint64Array', 8]]) {
    if (typeof globalThis[name] === 'function') {
      typedArrayConstructors[name] = globalThis[name];
      elementSizes[name] = size;
    }
  }

  const { AbortController: AbortController_, AbortSignal: AbortSignal_, TextEncoder: TextEncoder_, TextDecoder: TextDecoder_ } = hooks.interfaces;
  const abortControllerSignal = getter(AbortController_.prototype, 'signal');
  const abortControllerAbort = AbortController_.prototype.abort;
  const abortSignalAborted = getter(AbortSignal_.prototype, 'aborted');
  const abortSignalReason = getter(AbortSignal_.prototype, 'reason');
  const addAbortAlgorithm = hooks.addAbortAlgorithm;
  const textDecoderEncoding = getter(TextDecoder_.prototype, 'encoding');
  const textDecoderFatal = getter(TextDecoder_.prototype, 'fatal');
  const textDecoderIgnoreBOM = getter(TextDecoder_.prototype, 'ignoreBOM');
  const textDecoderDecode = TextDecoder_.prototype.decode;

  // ------------------------------------------------------------------------------------------------------------------
  // Internal slots
  // ------------------------------------------------------------------------------------------------------------------

  // The records are shared by the realms of the agent (e.g. a stream of an iframe can be read with the methods of the
  // parent document). An object created by an algorithm is created in the realm of the stream it is created for (the
  // relevant realm), whose interfaces are in record.realm.
  const internals = hooks.internals;
  const isObject = (value) => (typeof value === 'object' && value !== null) || typeof value === 'function';

  // Creates the record of the internal slots of an object
  function createRecord(object, kind, objectRealm = realm) {
    const record = ObjectCreate(null);
    record.kind = kind;
    record.object = object;
    record.realm = objectRealm;
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

  // A FIFO list (the queues and the lists of requests of the specification)
  class List {
    constructor() {
      this.items = [];
      this.head = 0;
    }

    get size() { return this.items.length - this.head; }

    append(item) { this.items[this.items.length] = item; }

    peek() { return this.items[this.head]; }

    shift() {
      const item = this.items[this.head];
      this.items[this.head] = undefined;
      this.head++;
      if (this.head === this.items.length) {
        this.items = [];
        this.head = 0;
      } else if (this.head >= 1024) {
        const items = [];
        for (let i = this.head; i < this.items.length; i++) items[items.length] = this.items[i];
        this.items = items;
        this.head = 0;
      }

      return item;
    }

    toArray() {
      const result = [];
      for (let i = this.head; i < this.items.length; i++) result[result.length] = this.items[i];
      return result;
    }
  }

  // ------------------------------------------------------------------------------------------------------------------
  // Promises (https://webidl.spec.whatwg.org/#es-promise-manipulation)
  // ------------------------------------------------------------------------------------------------------------------

  const noop = () => {};

  // A promise with its resolving functions and its state
  function newPromise() {
    const deferred = ObjectCreate(null);
    deferred.state = 'pending';
    deferred.promise = new Promise_((resolve, reject) => {
      deferred.resolveFunction = resolve;
      deferred.rejectFunction = reject;
    });
    return deferred;
  }

  function resolvePromise(deferred, value) {
    if (deferred.state !== 'pending') return;
    deferred.state = 'fulfilled';
    deferred.resolveFunction(value);
  }

  function rejectPromise(deferred, reason) {
    if (deferred.state !== 'pending') return;
    deferred.state = 'rejected';
    deferred.rejectFunction(reason);
  }

  function resolvedDeferred(value) {
    const deferred = newPromise();
    resolvePromise(deferred, value);
    return deferred;
  }

  function rejectedDeferred(reason) {
    const deferred = newPromise();
    rejectPromise(deferred, reason);
    setPromiseIsHandledToTrue(deferred.promise);
    return deferred;
  }

  // https://webidl.spec.whatwg.org/#a-promise-resolved-with: always a new promise (unlike Promise.resolve, which returns
  // the value when it is a promise), as the conversion of the value returned by a callback to a promise type
  const promiseResolvedWith = (value) => new Promise_((resolve) => resolve(value));
  const promiseRejectedWith = (reason) => apply(promiseRejectFunction, Promise_, [reason]);
  const performPromiseThen = (promise, onFulfilled, onRejected) => apply(promiseThen, promise, [onFulfilled, onRejected]);
  const setPromiseIsHandledToTrue = (promise) => { performPromiseThen(promise, undefined, noop); };
  const uponPromise = (promise, onFulfilled, onRejected) => { performPromiseThen(performPromiseThen(promise, onFulfilled, onRejected), undefined, noop); };
  const uponFulfillment = (promise, onFulfilled) => uponPromise(promise, onFulfilled);
  const uponRejection = (promise, onRejected) => uponPromise(promise, undefined, onRejected);
  const resolvedPromise = promiseResolvedWith(undefined);
  const queueMicrotask = (callback) => { performPromiseThen(resolvedPromise, callback); };

  // https://webidl.spec.whatwg.org/#wait-for-all
  function waitForAll(promises) {
    const result = newPromise();
    const total = promises.length;
    if (total === 0) {
      queueMicrotask(() => resolvePromise(result, []));
      return result.promise;
    }

    let fulfilledCount = 0;
    const values = [];
    for (let index = 0; index < total; index++) {
      const promiseIndex = index;
      uponPromise(promises[index], (value) => {
        values[promiseIndex] = value;
        fulfilledCount++;
        if (fulfilledCount === total) resolvePromise(result, values);
      }, (reason) => rejectPromise(result, reason));
    }

    return result.promise;
  }

  // Invokes a callback function whose return type is a promise (https://webidl.spec.whatwg.org/#invoke-a-callback-function)
  function promiseCall(callback, thisArg, args) {
    try {
      return promiseResolvedWith(apply(callback, thisArg, args));
    } catch (error) {
      return promiseRejectedWith(error);
    }
  }

  // Runs the steps of an operation that returns a promise: the exceptions are converted to rejected promises
  function promiseOperation(steps) {
    try {
      return steps();
    } catch (error) {
      return promiseRejectedWith(error);
    }
  }

  // ------------------------------------------------------------------------------------------------------------------
  // WebIDL conversions (https://webidl.spec.whatwg.org/#es-type-mapping)
  // ------------------------------------------------------------------------------------------------------------------

  function convertObject(value, context) {
    if (!isObject(value)) throw new TypeError_(`${context} is not an object`);
    return value;
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

  const convertUnrestrictedDouble = (value) => +value;
  const convertBoolean = (value) => !!value;
  const convertDOMString = (value) => `${value}`;

  // [EnforceRange] unsigned long long
  function convertUnsignedLongLongEnforceRange(value, context) {
    let x = +value;
    if (x !== x || x === Infinity || x === -Infinity) throw new TypeError_(`${context} is not a finite number`);
    x = MathTrunc(x);
    if (x < 0 || x > 9007199254740991) throw new TypeError_(`${context} is outside the accepted range`);
    return x === 0 ? 0 : x;
  }

  function isSharedArrayBuffer(buffer) {
    if (sharedArrayBufferByteLength === null) return false;
    try {
      apply(sharedArrayBufferByteLength, buffer, []);
      return true;
    } catch {
      return false;
    }
  }

  // The kind of an ArrayBufferView: the name of the typed array, 'DataView', or undefined
  function getViewKind(value) {
    if (!isObject(value)) return undefined;
    const name = apply(typedArrayName, value, []);
    if (name !== undefined) return name;
    try {
      apply(dataViewBuffer, value, []);
      return 'DataView';
    } catch {
      return undefined;
    }
  }

  const getViewBuffer = (view) => apply(getViewKind(view) === 'DataView' ? dataViewBuffer : typedArrayBuffer, view, []);
  const getViewByteOffset = (view) => getViewKind(view) === 'DataView' ? (isDetachedBuffer(getViewBuffer(view)) ? 0 : apply(dataViewByteOffset, view, [])) : apply(typedArrayByteOffset, view, []);
  const getViewByteLength = (view) => getViewKind(view) === 'DataView' ? (isDetachedBuffer(getViewBuffer(view)) ? 0 : apply(dataViewByteLength, view, [])) : apply(typedArrayByteLength, view, []);

  // ArrayBufferView (not [AllowShared], not [AllowResizable])
  function convertArrayBufferView(value, context) {
    if (getViewKind(value) === undefined) throw new TypeError_(`${context} is not an ArrayBufferView`);
    const buffer = getViewBuffer(value);
    if (isSharedArrayBuffer(buffer)) throw new TypeError_(`${context} is backed by a SharedArrayBuffer`);
    if (apply(arrayBufferResizable, buffer, [])) throw new TypeError_(`${context} is backed by a resizable ArrayBuffer`);
    return value;
  }

  // Makes the members of a class follow WebIDL: the operations and the attributes are enumerable, the class has a
  // @@toStringTag, and the lengths of the functions are the number of required arguments.
  function defineInterface(constructor, name, lengths) {
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

    if (lengths !== undefined) {
      for (const key of ownKeys(lengths)) {
        const target = key === 'constructor' ? constructor : prototype[key];
        defineProperty(target, 'length', { value: lengths[key], writable: false, enumerable: false, configurable: true });
      }
    }

    defineProperty(prototype, SymbolToStringTag, { value: name, writable: false, enumerable: false, configurable: true });
  }

  // Creates an object of an interface without calling its constructor (the "new" objects of the algorithms)
  const createObject = (objectRealm, kind) => createRecord(ObjectCreate(objectRealm[kind].prototype), kind, objectRealm);

  // ------------------------------------------------------------------------------------------------------------------
  // ArrayBuffers
  // ------------------------------------------------------------------------------------------------------------------

  const isDetachedBuffer = (buffer) => apply(arrayBufferDetached, buffer, []);
  const arrayBufferByteLengthOf = (buffer) => apply(arrayBufferByteLength, buffer, []);

  // https://streams.spec.whatwg.org/#transfer-array-buffer
  function transferArrayBuffer(buffer) {
    if (isDetachedBuffer(buffer)) throw new TypeError_('The ArrayBuffer is detached');
    return apply(arrayBufferTransfer, buffer, []);
  }

  const canTransferArrayBuffer = (buffer) => !isDetachedBuffer(buffer);
  const cloneArrayBuffer = (buffer, byteOffset, byteLength) => apply(arrayBufferSlice, buffer, [byteOffset, byteOffset + byteLength]);

  function copyDataBlockBytes(destination, destinationStart, source, sourceStart, count) {
    apply(typedArraySet, new Uint8Array_(destination, destinationStart, count), [new Uint8Array_(source, sourceStart, count)]);
  }

  // https://streams.spec.whatwg.org/#abstract-opdef-cloneasuint8array
  function cloneAsUint8Array(view) {
    const buffer = cloneArrayBuffer(getViewBuffer(view), getViewByteOffset(view), getViewByteLength(view));
    return new Uint8Array_(buffer);
  }

  // ------------------------------------------------------------------------------------------------------------------
  // Queue-with-sizes (https://streams.spec.whatwg.org/#queue-with-sizes)
  // ------------------------------------------------------------------------------------------------------------------

  const isNonNegativeNumber = (value) => typeof value === 'number' && value === value && value >= 0;

  function dequeueValue(container) {
    const pair = container.queue.shift();
    container.queueTotalSize -= pair.size;
    if (container.queueTotalSize < 0) container.queueTotalSize = 0;
    return pair.value;
  }

  function enqueueValueWithSize(container, value, size) {
    if (!isNonNegativeNumber(size)) throw new RangeError_('The size of the chunk must be a non-negative number');
    if (size === Infinity) throw new RangeError_('The size of the chunk must be a finite number');
    const pair = ObjectCreate(null);
    pair.value = value;
    pair.size = size;
    container.queue.append(pair);
    container.queueTotalSize += size;
  }

  const peekQueueValue = (container) => container.queue.peek().value;

  function resetQueue(container) {
    container.queue = new List();
    container.queueTotalSize = 0;
  }

  // ------------------------------------------------------------------------------------------------------------------
  // Queuing strategies (https://streams.spec.whatwg.org/#qs)
  // ------------------------------------------------------------------------------------------------------------------

  // QueuingStrategy dictionary
  function convertQueuingStrategy(value, context) {
    convertDictionary(value, context);
    const result = ObjectCreate(null);
    const highWaterMark = getMember(value, 'highWaterMark');
    result.highWaterMark = highWaterMark === undefined ? undefined : convertUnrestrictedDouble(highWaterMark);
    const size = getMember(value, 'size');
    result.size = size === undefined ? undefined : convertCallback(size, `${context}.size`);
    return result;
  }

  // https://streams.spec.whatwg.org/#validate-and-normalize-high-water-mark
  function extractHighWaterMark(strategy, defaultHWM) {
    if (strategy.highWaterMark === undefined) return defaultHWM;
    const highWaterMark = strategy.highWaterMark;
    if (highWaterMark !== highWaterMark || highWaterMark < 0) throw new RangeError_('The highWaterMark must be a non-negative number');
    return highWaterMark;
  }

  // https://streams.spec.whatwg.org/#make-size-algorithm-from-size-function
  function extractSizeAlgorithm(strategy) {
    const size = strategy.size;
    if (size === undefined) return () => 1;
    return (chunk) => convertUnrestrictedDouble(apply(size, undefined, [chunk]));
  }

  function convertQueuingStrategyInit(value, context) {
    convertDictionary(value, context);
    const highWaterMark = getMember(value, 'highWaterMark');
    if (highWaterMark === undefined) throw new TypeError_(`${context}.highWaterMark is required`);
    return convertUnrestrictedDouble(highWaterMark);
  }

  // The size functions are created once per realm (https://streams.spec.whatwg.org/#blqs-internal-slots)
  const byteLengthSizeFunction = ({ size(chunk) { return chunk.byteLength; } }).size;
  const countSizeFunction = ({ size() { return 1; } }).size;

  class ByteLengthQueuingStrategy {
    constructor(init) {
      if (arguments.length < 1) throw new TypeError_("Failed to construct 'ByteLengthQueuingStrategy': 1 argument required");
      const highWaterMark = convertQueuingStrategyInit(init, 'init');
      createRecord(this, 'ByteLengthQueuingStrategy').highWaterMark = highWaterMark;
    }

    get highWaterMark() { return thisRecord(this, 'ByteLengthQueuingStrategy').highWaterMark; }

    get size() {
      thisRecord(this, 'ByteLengthQueuingStrategy');
      return byteLengthSizeFunction;
    }
  }

  class CountQueuingStrategy {
    constructor(init) {
      if (arguments.length < 1) throw new TypeError_("Failed to construct 'CountQueuingStrategy': 1 argument required");
      const highWaterMark = convertQueuingStrategyInit(init, 'init');
      createRecord(this, 'CountQueuingStrategy').highWaterMark = highWaterMark;
    }

    get highWaterMark() { return thisRecord(this, 'CountQueuingStrategy').highWaterMark; }

    get size() {
      thisRecord(this, 'CountQueuingStrategy');
      return countSizeFunction;
    }
  }

  defineInterface(ByteLengthQueuingStrategy, 'ByteLengthQueuingStrategy');
  defineInterface(CountQueuingStrategy, 'CountQueuingStrategy');

  // ------------------------------------------------------------------------------------------------------------------
  // ReadableStream (https://streams.spec.whatwg.org/#rs-class)
  // ------------------------------------------------------------------------------------------------------------------

  function convertUnderlyingSource(value) {
    const result = ObjectCreate(null);
    const autoAllocateChunkSize = getMember(value, 'autoAllocateChunkSize');
    result.autoAllocateChunkSize = autoAllocateChunkSize === undefined ? undefined : convertUnsignedLongLongEnforceRange(autoAllocateChunkSize, 'autoAllocateChunkSize');
    const cancel = getMember(value, 'cancel');
    result.cancel = cancel === undefined ? undefined : convertCallback(cancel, 'underlyingSource.cancel');
    const pull = getMember(value, 'pull');
    result.pull = pull === undefined ? undefined : convertCallback(pull, 'underlyingSource.pull');
    const start = getMember(value, 'start');
    result.start = start === undefined ? undefined : convertCallback(start, 'underlyingSource.start');
    const type = getMember(value, 'type');
    if (type === undefined) {
      result.type = undefined;
    } else {
      result.type = convertDOMString(type);
      if (result.type !== 'bytes') throw new TypeError_(`'${result.type}' is not a valid value for the type of the underlying source`);
    }

    return result;
  }

  function convertStreamPipeOptions(value) {
    convertDictionary(value, 'options');
    const result = ObjectCreate(null);
    result.preventAbort = convertBoolean(getMember(value, 'preventAbort'));
    result.preventCancel = convertBoolean(getMember(value, 'preventCancel'));
    result.preventClose = convertBoolean(getMember(value, 'preventClose'));
    const signal = getMember(value, 'signal');
    if (signal !== undefined && !isAbortSignal(signal)) throw new TypeError_('options.signal is not an AbortSignal');
    result.signal = signal;
    return result;
  }

  function isAbortSignal(value) {
    if (!isObject(value)) return false;
    try {
      apply(abortSignalAborted, value, []);
      return true;
    } catch {
      return false;
    }
  }

  class ReadableStream {
    constructor(underlyingSource = undefined, strategy = undefined) {
      if (underlyingSource === undefined) underlyingSource = null;
      else convertObject(underlyingSource, 'underlyingSource');
      const strategyDict = convertQueuingStrategy(strategy, 'strategy');
      const underlyingSourceDict = convertUnderlyingSource(underlyingSource);
      const stream = createRecord(this, 'ReadableStream');
      initializeReadableStream(stream);
      if (underlyingSourceDict.type === 'bytes') {
        if (strategyDict.size !== undefined) throw new RangeError_('The strategy of a byte stream cannot have a size function');
        const highWaterMark = extractHighWaterMark(strategyDict, 0);
        setUpReadableByteStreamControllerFromUnderlyingSource(stream, underlyingSource, underlyingSourceDict, highWaterMark);
      } else {
        const sizeAlgorithm = extractSizeAlgorithm(strategyDict);
        const highWaterMark = extractHighWaterMark(strategyDict, 1);
        setUpReadableStreamDefaultControllerFromUnderlyingSource(stream, underlyingSource, underlyingSourceDict, highWaterMark, sizeAlgorithm);
      }
    }

    static from(asyncIterable) {
      if (arguments.length < 1) throw new TypeError_("Failed to execute 'from' on 'ReadableStream': 1 argument required");
      if (!isObject(asyncIterable)) throw new TypeError_('The argument is not an async iterable');
      return readableStreamFromIterable(asyncIterable).object;
    }

    get locked() { return isReadableStreamLocked(thisRecord(this, 'ReadableStream')); }

    cancel(reason = undefined) {
      return promiseOperation(() => {
        const stream = thisRecord(this, 'ReadableStream');
        if (isReadableStreamLocked(stream)) return promiseRejectedWith(new TypeError_('Cannot cancel a locked stream'));
        return readableStreamCancel(stream, reason);
      });
    }

    getReader(options = undefined) {
      const stream = thisRecord(this, 'ReadableStream');
      convertDictionary(options, 'options');
      let mode = getMember(options, 'mode');
      if (mode !== undefined) {
        mode = convertDOMString(mode);
        if (mode !== 'byob') throw new TypeError_(`'${mode}' is not a valid reader mode`);
      }

      if (mode === undefined) return acquireReadableStreamDefaultReader(stream).object;
      return acquireReadableStreamBYOBReader(stream).object;
    }

    pipeThrough(transform, options = undefined) {
      const stream = thisRecord(this, 'ReadableStream');
      if (arguments.length < 1) throw new TypeError_("Failed to execute 'pipeThrough' on 'ReadableStream': 1 argument required");
      convertDictionary(transform, 'transform');
      const readableValue = getMember(transform, 'readable');
      if (readableValue === undefined) throw new TypeError_('transform.readable is required');
      const readable = getRecord(readableValue, 'ReadableStream');
      if (readable === undefined) throw new TypeError_('transform.readable is not a ReadableStream');
      const writableValue = getMember(transform, 'writable');
      if (writableValue === undefined) throw new TypeError_('transform.writable is required');
      const writable = getRecord(writableValue, 'WritableStream');
      if (writable === undefined) throw new TypeError_('transform.writable is not a WritableStream');
      const pipeOptions = convertStreamPipeOptions(options);
      if (isReadableStreamLocked(stream)) throw new TypeError_('Cannot pipe a locked stream');
      if (isWritableStreamLocked(writable)) throw new TypeError_('Cannot pipe to a locked stream');
      const promise = readableStreamPipeTo(stream, writable, pipeOptions.preventClose, pipeOptions.preventAbort, pipeOptions.preventCancel, pipeOptions.signal);
      setPromiseIsHandledToTrue(promise);
      return readable.object;
    }

    pipeTo(destination, options = undefined) {
      return promiseOperation(() => {
        const stream = thisRecord(this, 'ReadableStream');
        const destinationStream = getRecord(destination, 'WritableStream');
        if (destinationStream === undefined) throw new TypeError_('The destination is not a WritableStream');
        const pipeOptions = convertStreamPipeOptions(options);
        if (isReadableStreamLocked(stream)) throw new TypeError_('Cannot pipe a locked stream');
        if (isWritableStreamLocked(destinationStream)) throw new TypeError_('Cannot pipe to a locked stream');
        return readableStreamPipeTo(stream, destinationStream, pipeOptions.preventClose, pipeOptions.preventAbort, pipeOptions.preventCancel, pipeOptions.signal);
      });
    }

    tee() {
      const stream = thisRecord(this, 'ReadableStream');
      const branches = readableStreamTee(stream, false);
      return [branches[0].object, branches[1].object];
    }

    values(options = undefined) {
      const stream = thisRecord(this, 'ReadableStream');
      convertDictionary(options, 'options');
      const preventCancel = convertBoolean(getMember(options, 'preventCancel'));
      const reader = acquireReadableStreamDefaultReader(stream);
      const iterator = createRecord(ObjectCreate(stream.realm.ReadableStreamAsyncIteratorPrototype), 'ReadableStreamAsyncIterator', stream.realm);
      iterator.reader = reader;
      iterator.preventCancel = preventCancel;
      iterator.ongoingPromise = null;
      iterator.isFinished = false;
      return iterator.object;
    }
  }

  defineInterface(ReadableStream, 'ReadableStream');
  defineProperty(ReadableStream.prototype, SymbolAsyncIterator, { value: ReadableStream.prototype.values, writable: true, enumerable: false, configurable: true });

  // https://webidl.spec.whatwg.org/#es-asynchronous-iterator-prototype-object
  const createIterResultObject = (value, done) => ({ value, done });
  const ReadableStreamAsyncIteratorPrototype = ObjectCreate(AsyncIteratorPrototype);
  const asyncIteratorMethods = {
    next() {
      const iterator = getRecord(this, 'ReadableStreamAsyncIterator');
      if (iterator === undefined) return promiseRejectedWith(new TypeError_("Illegal invocation: 'this' is not a ReadableStream async iterator"));
      const nextSteps = () => {
        if (iterator.isFinished) return promiseResolvedWith(createIterResultObject(undefined, true));
        const nextPromise = readableStreamAsyncIteratorGetNext(iterator);
        return performPromiseThen(nextPromise, (next) => {
          iterator.ongoingPromise = null;
          if (next === endOfIteration) {
            iterator.isFinished = true;
            return createIterResultObject(undefined, true);
          }

          return createIterResultObject(next, false);
        }, (reason) => {
          iterator.ongoingPromise = null;
          iterator.isFinished = true;
          throw reason;
        });
      };

      const ongoingPromise = iterator.ongoingPromise;
      iterator.ongoingPromise = ongoingPromise !== null ? performPromiseThen(ongoingPromise, nextSteps, nextSteps) : nextSteps();
      return iterator.ongoingPromise;
    },
    return(value) {
      const iterator = getRecord(this, 'ReadableStreamAsyncIterator');
      if (iterator === undefined) return promiseRejectedWith(new TypeError_("Illegal invocation: 'this' is not a ReadableStream async iterator"));
      const returnSteps = () => {
        if (iterator.isFinished) return promiseResolvedWith(createIterResultObject(value, true));
        iterator.isFinished = true;
        return readableStreamAsyncIteratorReturn(iterator, value);
      };

      const ongoingPromise = iterator.ongoingPromise;
      iterator.ongoingPromise = ongoingPromise !== null ? performPromiseThen(ongoingPromise, returnSteps, returnSteps) : returnSteps();
      return performPromiseThen(iterator.ongoingPromise, () => createIterResultObject(value, true));
    },
  };
  defineProperty(ReadableStreamAsyncIteratorPrototype, 'next', { value: asyncIteratorMethods.next, writable: true, enumerable: true, configurable: true });
  defineProperty(ReadableStreamAsyncIteratorPrototype, 'return', { value: asyncIteratorMethods.return, writable: true, enumerable: true, configurable: true });
  defineProperty(ReadableStreamAsyncIteratorPrototype, SymbolToStringTag, { value: 'ReadableStream AsyncIterator', writable: false, enumerable: false, configurable: true });

  const endOfIteration = ObjectCreate(null);

  // https://streams.spec.whatwg.org/#ref-for-dfn-get-the-next-iteration-result
  function readableStreamAsyncIteratorGetNext(iterator) {
    const reader = iterator.reader;
    const promise = newPromise();
    readableStreamDefaultReaderRead(reader, {
      chunk(chunk) { resolvePromise(promise, chunk); },
      close() {
        readableStreamDefaultReaderRelease(reader);
        resolvePromise(promise, endOfIteration);
      },
      error(e) {
        readableStreamDefaultReaderRelease(reader);
        rejectPromise(promise, e);
      },
    });
    return promise.promise;
  }

  // https://streams.spec.whatwg.org/#rs-asynciterator-prototype-return
  function readableStreamAsyncIteratorReturn(iterator, arg) {
    const reader = iterator.reader;
    if (!iterator.preventCancel) {
      const result = readableStreamReaderGenericCancel(reader, arg);
      readableStreamDefaultReaderRelease(reader);
      return result;
    }

    readableStreamDefaultReaderRelease(reader);
    return promiseResolvedWith(undefined);
  }

  // https://tc39.es/ecma262/#sec-getiterator with the kind async
  function getAsyncIterator(object) {
    const method = object === undefined || object === null ? undefined : object[SymbolAsyncIterator];
    if (method === undefined || method === null) {
      const syncMethod = object === undefined || object === null ? undefined : object[SymbolIterator];
      if (syncMethod === undefined || syncMethod === null) throw new TypeError_('The object is not iterable');
      if (typeof syncMethod !== 'function') throw new TypeError_('The @@iterator method is not callable');
      const syncIterator = apply(syncMethod, object, []);
      if (!isObject(syncIterator)) throw new TypeError_('The iterator is not an object');
      return createAsyncFromSyncIterator(syncIterator, syncIterator.next);
    }

    if (typeof method !== 'function') throw new TypeError_('The @@asyncIterator method is not callable');
    const iterator = apply(method, object, []);
    if (!isObject(iterator)) throw new TypeError_('The async iterator is not an object');
    const record = ObjectCreate(null);
    record.iterator = iterator;
    record.nextMethod = iterator.next;
    return record;
  }

  // https://tc39.es/ecma262/#sec-createasyncfromsynciterator
  function createAsyncFromSyncIterator(syncIterator, syncNextMethod) {
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
          if (!isObject(result)) throw new TypeError_('The iterator result is not an object');
          return continuation(result, false);
        });
      },
    };
    const record = ObjectCreate(null);
    record.iterator = asyncIterator;
    record.nextMethod = asyncIterator.next;
    return record;
  }

  // https://streams.spec.whatwg.org/#readable-stream-from-iterable
  function readableStreamFromIterable(asyncIterable) {
    let stream;
    const iteratorRecord = getAsyncIterator(asyncIterable);
    const startAlgorithm = () => undefined;
    const pullAlgorithm = () => {
      let nextResult;
      try {
        if (typeof iteratorRecord.nextMethod !== 'function') throw new TypeError_('The next method of the iterator is not callable');
        nextResult = apply(iteratorRecord.nextMethod, iteratorRecord.iterator, []);
      } catch (error) {
        return promiseRejectedWith(error);
      }

      const nextPromise = promiseResolvedWith(nextResult);
      return performPromiseThen(nextPromise, (iterResult) => {
        if (!isObject(iterResult)) throw new TypeError_('The promise returned by the iterator.next() method must fulfill with an object');
        const done = !!iterResult.done;
        if (done) {
          readableStreamDefaultControllerClose(stream.controller);
        } else {
          const value = iterResult.value;
          readableStreamDefaultControllerEnqueue(stream.controller, value);
        }
      });
    };
    const cancelAlgorithm = (reason) => {
      const iterator = iteratorRecord.iterator;
      let returnMethod;
      try {
        returnMethod = iterator.return;
        if (returnMethod !== undefined && returnMethod !== null && typeof returnMethod !== 'function') throw new TypeError_('The return method of the iterator is not callable');
      } catch (error) {
        return promiseRejectedWith(error);
      }

      if (returnMethod === undefined || returnMethod === null) return promiseResolvedWith(undefined);
      let returnResult;
      try {
        returnResult = apply(returnMethod, iterator, [reason]);
      } catch (error) {
        return promiseRejectedWith(error);
      }

      const returnPromise = promiseResolvedWith(returnResult);
      return performPromiseThen(returnPromise, (iterResult) => {
        if (!isObject(iterResult)) throw new TypeError_('The promise returned by the iterator.return() method must fulfill with an object');
        return undefined;
      });
    };
    stream = createReadableStream(realm, startAlgorithm, pullAlgorithm, cancelAlgorithm, 0);
    return stream;
  }

  // ------------------------------------------------------------------------------------------------------------------
  // Readers (https://streams.spec.whatwg.org/#default-reader-class)
  // ------------------------------------------------------------------------------------------------------------------

  class ReadableStreamDefaultReader {
    constructor(stream) {
      if (arguments.length < 1) throw new TypeError_("Failed to construct 'ReadableStreamDefaultReader': 1 argument required");
      const streamRecord = getRecord(stream, 'ReadableStream');
      if (streamRecord === undefined) throw new TypeError_('The argument is not a ReadableStream');
      setUpReadableStreamDefaultReader(createRecord(this, 'ReadableStreamDefaultReader'), streamRecord);
    }

    get closed() {
      return promiseOperation(() => thisRecord(this, 'ReadableStreamDefaultReader').closedPromise.promise);
    }

    cancel(reason = undefined) {
      return promiseOperation(() => {
        const reader = thisRecord(this, 'ReadableStreamDefaultReader');
        if (reader.stream === undefined) return promiseRejectedWith(new TypeError_('The reader is released'));
        return readableStreamReaderGenericCancel(reader, reason);
      });
    }

    read() {
      return promiseOperation(() => {
        const reader = thisRecord(this, 'ReadableStreamDefaultReader');
        if (reader.stream === undefined) return promiseRejectedWith(new TypeError_('The reader is released'));
        const promise = newPromise();
        readableStreamDefaultReaderRead(reader, {
          chunk(chunk) { resolvePromise(promise, createIterResultObject(chunk, false)); },
          close() { resolvePromise(promise, createIterResultObject(undefined, true)); },
          error(e) { rejectPromise(promise, e); },
        });
        return promise.promise;
      });
    }

    releaseLock() {
      const reader = thisRecord(this, 'ReadableStreamDefaultReader');
      if (reader.stream === undefined) return;
      readableStreamDefaultReaderRelease(reader);
    }
  }

  class ReadableStreamBYOBReader {
    constructor(stream) {
      if (arguments.length < 1) throw new TypeError_("Failed to construct 'ReadableStreamBYOBReader': 1 argument required");
      const streamRecord = getRecord(stream, 'ReadableStream');
      if (streamRecord === undefined) throw new TypeError_('The argument is not a ReadableStream');
      setUpReadableStreamBYOBReader(createRecord(this, 'ReadableStreamBYOBReader'), streamRecord);
    }

    get closed() {
      return promiseOperation(() => thisRecord(this, 'ReadableStreamBYOBReader').closedPromise.promise);
    }

    cancel(reason = undefined) {
      return promiseOperation(() => {
        const reader = thisRecord(this, 'ReadableStreamBYOBReader');
        if (reader.stream === undefined) return promiseRejectedWith(new TypeError_('The reader is released'));
        return readableStreamReaderGenericCancel(reader, reason);
      });
    }

    read(view, options = undefined) {
      return promiseOperation(() => {
        const reader = thisRecord(this, 'ReadableStreamBYOBReader');
        if (arguments.length < 1) throw new TypeError_("Failed to execute 'read' on 'ReadableStreamBYOBReader': 1 argument required");
        convertArrayBufferView(view, 'view');
        convertDictionary(options, 'options');
        const minValue = getMember(options, 'min');
        const min = minValue === undefined ? 1 : convertUnsignedLongLongEnforceRange(minValue, 'options.min');
        const buffer = getViewBuffer(view);
        if (getViewByteLength(view) === 0) throw new TypeError_('The view must have a non-zero byteLength');
        if (arrayBufferByteLengthOf(buffer) === 0) {
          if (isDetachedBuffer(buffer)) throw new TypeError_("The view's buffer has been detached");
          throw new TypeError_("The view's buffer must have a non-zero byteLength");
        }

        if (isDetachedBuffer(buffer)) throw new TypeError_("The view's buffer has been detached");
        if (min === 0) throw new TypeError_('options.min must be greater than 0');
        const kind = getViewKind(view);
        if (kind !== 'DataView') {
          if (min > apply(typedArrayLength, view, [])) throw new RangeError_('options.min must be less than or equal to the length of the view');
        } else if (min > getViewByteLength(view)) {
          throw new RangeError_('options.min must be less than or equal to the byteLength of the view');
        }

        if (reader.stream === undefined) return promiseRejectedWith(new TypeError_('The reader is released'));
        const promise = newPromise();
        readableStreamBYOBReaderRead(reader, view, min, {
          chunk(chunk) { resolvePromise(promise, createIterResultObject(chunk, false)); },
          close(chunk) { resolvePromise(promise, createIterResultObject(chunk, true)); },
          error(e) { rejectPromise(promise, e); },
        });
        return promise.promise;
      });
    }

    releaseLock() {
      const reader = thisRecord(this, 'ReadableStreamBYOBReader');
      if (reader.stream === undefined) return;
      readableStreamBYOBReaderRelease(reader);
    }
  }

  defineInterface(ReadableStreamDefaultReader, 'ReadableStreamDefaultReader');
  defineInterface(ReadableStreamBYOBReader, 'ReadableStreamBYOBReader', { read: 1 });

  function acquireReadableStreamDefaultReader(stream) {
    const reader = createObject(stream.realm, 'ReadableStreamDefaultReader');
    setUpReadableStreamDefaultReader(reader, stream);
    return reader;
  }

  function acquireReadableStreamBYOBReader(stream) {
    const reader = createObject(stream.realm, 'ReadableStreamBYOBReader');
    setUpReadableStreamBYOBReader(reader, stream);
    return reader;
  }

  function readableStreamReaderGenericCancel(reader, reason) {
    return readableStreamCancel(reader.stream, reason);
  }

  function readableStreamReaderGenericInitialize(reader, stream) {
    reader.stream = stream;
    stream.reader = reader;
    if (stream.state === 'readable') {
      reader.closedPromise = newPromise();
    } else if (stream.state === 'closed') {
      reader.closedPromise = resolvedDeferred(undefined);
    } else {
      reader.closedPromise = rejectedDeferred(stream.storedError);
    }
  }

  function readableStreamReaderGenericRelease(reader) {
    const stream = reader.stream;
    if (stream.state === 'readable') {
      rejectPromise(reader.closedPromise, new TypeError_('The reader was released'));
    } else {
      reader.closedPromise = rejectedDeferred(new TypeError_('The reader was released'));
    }

    setPromiseIsHandledToTrue(reader.closedPromise.promise);
    stream.controller.releaseSteps();
    stream.reader = undefined;
    reader.stream = undefined;
  }

  function readableStreamDefaultReaderRelease(reader) {
    readableStreamReaderGenericRelease(reader);
    readableStreamDefaultReaderErrorReadRequests(reader, new TypeError_('The reader was released'));
  }

  function readableStreamDefaultReaderErrorReadRequests(reader, e) {
    const readRequests = reader.readRequests;
    reader.readRequests = new List();
    for (const readRequest of readRequests.toArray()) readRequest.error(e);
  }

  function readableStreamBYOBReaderRelease(reader) {
    readableStreamReaderGenericRelease(reader);
    readableStreamBYOBReaderErrorReadIntoRequests(reader, new TypeError_('The reader was released'));
  }

  function readableStreamBYOBReaderErrorReadIntoRequests(reader, e) {
    const readIntoRequests = reader.readIntoRequests;
    reader.readIntoRequests = new List();
    for (const readIntoRequest of readIntoRequests.toArray()) readIntoRequest.error(e);
  }

  function readableStreamDefaultReaderRead(reader, readRequest) {
    const stream = reader.stream;
    stream.disturbed = true;
    if (stream.state === 'closed') {
      readRequest.close();
    } else if (stream.state === 'errored') {
      readRequest.error(stream.storedError);
    } else {
      stream.controller.pullSteps(readRequest);
    }
  }

  function readableStreamBYOBReaderRead(reader, view, min, readIntoRequest) {
    const stream = reader.stream;
    stream.disturbed = true;
    if (stream.state === 'errored') {
      readIntoRequest.error(stream.storedError);
    } else {
      readableByteStreamControllerPullInto(stream.controller, view, min, readIntoRequest);
    }
  }

  function setUpReadableStreamDefaultReader(reader, stream) {
    if (isReadableStreamLocked(stream)) throw new TypeError_('The stream is locked');
    readableStreamReaderGenericInitialize(reader, stream);
    reader.readRequests = new List();
  }

  function setUpReadableStreamBYOBReader(reader, stream) {
    if (isReadableStreamLocked(stream)) throw new TypeError_('The stream is locked');
    if (stream.controller.kind !== 'ReadableByteStreamController') throw new TypeError_('A BYOB reader requires a readable byte stream');
    readableStreamReaderGenericInitialize(reader, stream);
    reader.readIntoRequests = new List();
  }

  // ------------------------------------------------------------------------------------------------------------------
  // Readable stream abstract operations (https://streams.spec.whatwg.org/#rs-abstract-ops)
  // ------------------------------------------------------------------------------------------------------------------

  function createReadableStream(objectRealm, startAlgorithm, pullAlgorithm, cancelAlgorithm, highWaterMark = 1, sizeAlgorithm = () => 1) {
    const stream = createObject(objectRealm, 'ReadableStream');
    initializeReadableStream(stream);
    const controller = createObject(objectRealm, 'ReadableStreamDefaultController');
    setUpReadableStreamDefaultController(stream, controller, startAlgorithm, pullAlgorithm, cancelAlgorithm, highWaterMark, sizeAlgorithm);
    return stream;
  }

  function createReadableByteStream(objectRealm, startAlgorithm, pullAlgorithm, cancelAlgorithm) {
    const stream = createObject(objectRealm, 'ReadableStream');
    initializeReadableStream(stream);
    const controller = createObject(objectRealm, 'ReadableByteStreamController');
    setUpReadableByteStreamController(stream, controller, startAlgorithm, pullAlgorithm, cancelAlgorithm, 0, undefined);
    return stream;
  }

  function initializeReadableStream(stream) {
    stream.state = 'readable';
    stream.reader = undefined;
    stream.storedError = undefined;
    stream.disturbed = false;
    stream.controller = undefined;
  }

  const isReadableStreamLocked = (stream) => stream.reader !== undefined;

  function readableStreamCancel(stream, reason) {
    stream.disturbed = true;
    if (stream.state === 'closed') return promiseResolvedWith(undefined);
    if (stream.state === 'errored') return promiseRejectedWith(stream.storedError);
    readableStreamClose(stream);
    const reader = stream.reader;
    if (reader !== undefined && reader.kind === 'ReadableStreamBYOBReader') {
      const readIntoRequests = reader.readIntoRequests;
      reader.readIntoRequests = new List();
      for (const readIntoRequest of readIntoRequests.toArray()) readIntoRequest.close(undefined);
    }

    const sourceCancelPromise = stream.controller.cancelSteps(reason);
    return performPromiseThen(sourceCancelPromise, noop);
  }

  function readableStreamClose(stream) {
    stream.state = 'closed';
    const reader = stream.reader;
    if (reader === undefined) return;
    resolvePromise(reader.closedPromise, undefined);
    if (reader.kind === 'ReadableStreamDefaultReader') {
      const readRequests = reader.readRequests;
      reader.readRequests = new List();
      for (const readRequest of readRequests.toArray()) readRequest.close();
    }
  }

  function readableStreamError(stream, e) {
    stream.state = 'errored';
    stream.storedError = e;
    const reader = stream.reader;
    if (reader === undefined) return;
    rejectPromise(reader.closedPromise, e);
    setPromiseIsHandledToTrue(reader.closedPromise.promise);
    if (reader.kind === 'ReadableStreamDefaultReader') {
      readableStreamDefaultReaderErrorReadRequests(reader, e);
    } else {
      readableStreamBYOBReaderErrorReadIntoRequests(reader, e);
    }
  }

  const readableStreamAddReadRequest = (stream, readRequest) => stream.reader.readRequests.append(readRequest);
  const readableStreamAddReadIntoRequest = (stream, readIntoRequest) => stream.reader.readIntoRequests.append(readIntoRequest);

  function readableStreamFulfillReadRequest(stream, chunk, done) {
    const readRequest = stream.reader.readRequests.shift();
    if (done) readRequest.close();
    else readRequest.chunk(chunk);
  }

  function readableStreamFulfillReadIntoRequest(stream, chunk, done) {
    const readIntoRequest = stream.reader.readIntoRequests.shift();
    if (done) readIntoRequest.close(chunk);
    else readIntoRequest.chunk(chunk);
  }

  const readableStreamGetNumReadRequests = (stream) => stream.reader.readRequests.size;
  const readableStreamGetNumReadIntoRequests = (stream) => stream.reader.readIntoRequests.size;
  const readableStreamHasBYOBReader = (stream) => stream.reader !== undefined && stream.reader.kind === 'ReadableStreamBYOBReader';
  const readableStreamHasDefaultReader = (stream) => stream.reader !== undefined && stream.reader.kind === 'ReadableStreamDefaultReader';

  // https://streams.spec.whatwg.org/#readable-stream-tee
  function readableStreamTee(stream, cloneForBranch2) {
    if (stream.controller.kind === 'ReadableByteStreamController') return readableByteStreamTee(stream);
    return readableStreamDefaultTee(stream, cloneForBranch2);
  }

  function readableStreamDefaultTee(stream, cloneForBranch2) {
    const reader = acquireReadableStreamDefaultReader(stream);
    let reading = false;
    let readAgain = false;
    let canceled1 = false;
    let canceled2 = false;
    let reason1;
    let reason2;
    let branch1;
    let branch2;
    const cancelPromise = newPromise();
    const pullAlgorithm = () => {
      if (reading) {
        readAgain = true;
        return promiseResolvedWith(undefined);
      }

      reading = true;
      readableStreamDefaultReaderRead(reader, {
        chunk(chunk) {
          queueMicrotask(() => {
            readAgain = false;
            const chunk1 = chunk;
            let chunk2 = chunk;
            if (!canceled2 && cloneForBranch2) {
              try {
                chunk2 = hooks.structuredClone(chunk2);
              } catch (error) {
                readableStreamDefaultControllerError(branch1.controller, error);
                readableStreamDefaultControllerError(branch2.controller, error);
                resolvePromise(cancelPromise, readableStreamCancel(stream, error));
                return;
              }
            }

            if (!canceled1) readableStreamDefaultControllerEnqueue(branch1.controller, chunk1);
            if (!canceled2) readableStreamDefaultControllerEnqueue(branch2.controller, chunk2);
            reading = false;
            if (readAgain) pullAlgorithm();
          });
        },
        close() {
          reading = false;
          if (!canceled1) readableStreamDefaultControllerClose(branch1.controller);
          if (!canceled2) readableStreamDefaultControllerClose(branch2.controller);
          if (!canceled1 || !canceled2) resolvePromise(cancelPromise, undefined);
        },
        error() {
          reading = false;
        },
      });
      return promiseResolvedWith(undefined);
    };
    const cancel1Algorithm = (reason) => {
      canceled1 = true;
      reason1 = reason;
      if (canceled2) {
        const cancelResult = readableStreamCancel(stream, [reason1, reason2]);
        resolvePromise(cancelPromise, cancelResult);
      }

      return cancelPromise.promise;
    };
    const cancel2Algorithm = (reason) => {
      canceled2 = true;
      reason2 = reason;
      if (canceled1) {
        const cancelResult = readableStreamCancel(stream, [reason1, reason2]);
        resolvePromise(cancelPromise, cancelResult);
      }

      return cancelPromise.promise;
    };
    const startAlgorithm = () => undefined;
    branch1 = createReadableStream(stream.realm, startAlgorithm, pullAlgorithm, cancel1Algorithm);
    branch2 = createReadableStream(stream.realm, startAlgorithm, pullAlgorithm, cancel2Algorithm);
    uponRejection(reader.closedPromise.promise, (r) => {
      readableStreamDefaultControllerError(branch1.controller, r);
      readableStreamDefaultControllerError(branch2.controller, r);
      if (!canceled1 || !canceled2) resolvePromise(cancelPromise, undefined);
    });
    return [branch1, branch2];
  }

  function readableByteStreamTee(stream) {
    let reader = acquireReadableStreamDefaultReader(stream);
    let reading = false;
    let readAgainForBranch1 = false;
    let readAgainForBranch2 = false;
    let canceled1 = false;
    let canceled2 = false;
    let reason1;
    let reason2;
    let branch1;
    let branch2;
    const cancelPromise = newPromise();
    const forwardReaderError = (thisReader) => {
      uponRejection(thisReader.closedPromise.promise, (r) => {
        if (thisReader !== reader) return;
        readableByteStreamControllerError(branch1.controller, r);
        readableByteStreamControllerError(branch2.controller, r);
        if (!canceled1 || !canceled2) resolvePromise(cancelPromise, undefined);
      });
    };
    const pullWithDefaultReader = () => {
      if (reader.kind === 'ReadableStreamBYOBReader') {
        readableStreamBYOBReaderRelease(reader);
        reader = acquireReadableStreamDefaultReader(stream);
        forwardReaderError(reader);
      }

      readableStreamDefaultReaderRead(reader, {
        chunk(chunk) {
          queueMicrotask(() => {
            readAgainForBranch1 = false;
            readAgainForBranch2 = false;
            const chunk1 = chunk;
            let chunk2 = chunk;
            if (!canceled1 && !canceled2) {
              try {
                chunk2 = cloneAsUint8Array(chunk);
              } catch (error) {
                readableByteStreamControllerError(branch1.controller, error);
                readableByteStreamControllerError(branch2.controller, error);
                resolvePromise(cancelPromise, readableStreamCancel(stream, error));
                return;
              }
            }

            if (!canceled1) readableByteStreamControllerEnqueue(branch1.controller, chunk1);
            if (!canceled2) readableByteStreamControllerEnqueue(branch2.controller, chunk2);
            reading = false;
            if (readAgainForBranch1) pull1Algorithm();
            else if (readAgainForBranch2) pull2Algorithm();
          });
        },
        close() {
          reading = false;
          if (!canceled1) readableByteStreamControllerClose(branch1.controller);
          if (!canceled2) readableByteStreamControllerClose(branch2.controller);
          if (branch1.controller.pendingPullIntos.size > 0) readableByteStreamControllerRespond(branch1.controller, 0);
          if (branch2.controller.pendingPullIntos.size > 0) readableByteStreamControllerRespond(branch2.controller, 0);
          if (!canceled1 || !canceled2) resolvePromise(cancelPromise, undefined);
        },
        error() {
          reading = false;
        },
      });
    };
    const pullWithBYOBReader = (view, forBranch2) => {
      if (reader.kind === 'ReadableStreamDefaultReader') {
        readableStreamDefaultReaderRelease(reader);
        reader = acquireReadableStreamBYOBReader(stream);
        forwardReaderError(reader);
      }

      const byobBranch = forBranch2 ? branch2 : branch1;
      const otherBranch = forBranch2 ? branch1 : branch2;
      readableStreamBYOBReaderRead(reader, view, 1, {
        chunk(chunk) {
          queueMicrotask(() => {
            readAgainForBranch1 = false;
            readAgainForBranch2 = false;
            const byobCanceled = forBranch2 ? canceled2 : canceled1;
            const otherCanceled = forBranch2 ? canceled1 : canceled2;
            if (!otherCanceled) {
              let clonedChunk;
              try {
                clonedChunk = cloneAsUint8Array(chunk);
              } catch (error) {
                readableByteStreamControllerError(byobBranch.controller, error);
                readableByteStreamControllerError(otherBranch.controller, error);
                resolvePromise(cancelPromise, readableStreamCancel(stream, error));
                return;
              }

              if (!byobCanceled) readableByteStreamControllerRespondWithNewView(byobBranch.controller, chunk);
              readableByteStreamControllerEnqueue(otherBranch.controller, clonedChunk);
            } else if (!byobCanceled) {
              readableByteStreamControllerRespondWithNewView(byobBranch.controller, chunk);
            }

            reading = false;
            if (readAgainForBranch1) pull1Algorithm();
            else if (readAgainForBranch2) pull2Algorithm();
          });
        },
        close(chunk) {
          reading = false;
          const byobCanceled = forBranch2 ? canceled2 : canceled1;
          const otherCanceled = forBranch2 ? canceled1 : canceled2;
          if (!byobCanceled) readableByteStreamControllerClose(byobBranch.controller);
          if (!otherCanceled) readableByteStreamControllerClose(otherBranch.controller);
          if (chunk !== undefined) {
            if (!byobCanceled) readableByteStreamControllerRespondWithNewView(byobBranch.controller, chunk);
            if (!otherCanceled && otherBranch.controller.pendingPullIntos.size > 0) readableByteStreamControllerRespond(otherBranch.controller, 0);
          }

          if (!byobCanceled || !otherCanceled) resolvePromise(cancelPromise, undefined);
        },
        error() {
          reading = false;
        },
      });
    };
    const pull1Algorithm = () => {
      if (reading) {
        readAgainForBranch1 = true;
        return promiseResolvedWith(undefined);
      }

      reading = true;
      const byobRequest = readableByteStreamControllerGetBYOBRequest(branch1.controller);
      if (byobRequest === null) pullWithDefaultReader();
      else pullWithBYOBReader(byobRequest.view, false);
      return promiseResolvedWith(undefined);
    };
    const pull2Algorithm = () => {
      if (reading) {
        readAgainForBranch2 = true;
        return promiseResolvedWith(undefined);
      }

      reading = true;
      const byobRequest = readableByteStreamControllerGetBYOBRequest(branch2.controller);
      if (byobRequest === null) pullWithDefaultReader();
      else pullWithBYOBReader(byobRequest.view, true);
      return promiseResolvedWith(undefined);
    };
    const cancel1Algorithm = (reason) => {
      canceled1 = true;
      reason1 = reason;
      if (canceled2) {
        const cancelResult = readableStreamCancel(stream, [reason1, reason2]);
        resolvePromise(cancelPromise, cancelResult);
      }

      return cancelPromise.promise;
    };
    const cancel2Algorithm = (reason) => {
      canceled2 = true;
      reason2 = reason;
      if (canceled1) {
        const cancelResult = readableStreamCancel(stream, [reason1, reason2]);
        resolvePromise(cancelPromise, cancelResult);
      }

      return cancelPromise.promise;
    };
    const startAlgorithm = () => undefined;
    branch1 = createReadableByteStream(stream.realm, startAlgorithm, pull1Algorithm, cancel1Algorithm);
    branch2 = createReadableByteStream(stream.realm, startAlgorithm, pull2Algorithm, cancel2Algorithm);
    forwardReaderError(reader);
    return [branch1, branch2];
  }

  // https://streams.spec.whatwg.org/#readable-stream-pipe-to
  function readableStreamPipeTo(source, dest, preventClose, preventAbort, preventCancel, signal) {
    const reader = acquireReadableStreamDefaultReader(source);
    const writer = acquireWritableStreamDefaultWriter(dest);
    source.disturbed = true;
    let shuttingDown = false;
    let currentWrite = promiseResolvedWith(undefined);
    const promise = newPromise();
    let removeAbortAlgorithm;

    const finalize = (isError, error) => {
      writableStreamDefaultWriterRelease(writer);
      readableStreamDefaultReaderRelease(reader);
      if (removeAbortAlgorithm !== undefined) removeAbortAlgorithm();
      if (isError) rejectPromise(promise, error);
      else resolvePromise(promise, undefined);
    };
    const waitForWritesToFinish = () => {
      const oldCurrentWrite = currentWrite;
      return performPromiseThen(currentWrite, () => (oldCurrentWrite !== currentWrite ? waitForWritesToFinish() : undefined));
    };
    const shutdownWithAnAction = (action, originalIsError, originalError) => {
      if (shuttingDown) return;
      shuttingDown = true;
      const doTheRest = () => {
        uponPromise(action(), () => finalize(originalIsError, originalError), (newError) => finalize(true, newError));
      };
      if (dest.state === 'writable' && !writableStreamCloseQueuedOrInFlight(dest)) uponFulfillment(waitForWritesToFinish(), doTheRest);
      else doTheRest();
    };
    const shutdown = (isError, error) => {
      if (shuttingDown) return;
      shuttingDown = true;
      if (dest.state === 'writable' && !writableStreamCloseQueuedOrInFlight(dest)) uponFulfillment(waitForWritesToFinish(), () => finalize(isError, error));
      else finalize(isError, error);
    };

    if (signal !== undefined) {
      const abortAlgorithm = () => {
        const error = apply(abortSignalReason, signal, []);
        const actions = [];
        if (!preventAbort) {
          actions[actions.length] = () => (dest.state === 'writable' ? writableStreamAbort(dest, error) : promiseResolvedWith(undefined));
        }

        if (!preventCancel) {
          actions[actions.length] = () => (source.state === 'readable' ? readableStreamCancel(source, error) : promiseResolvedWith(undefined));
        }

        shutdownWithAnAction(() => {
          const promises = [];
          for (let i = 0; i < actions.length; i++) promises[i] = actions[i]();
          return waitForAll(promises);
        }, true, error);
      };
      if (apply(abortSignalAborted, signal, [])) {
        abortAlgorithm();
        return promise.promise;
      }

      removeAbortAlgorithm = addAbortAlgorithm(signal, abortAlgorithm);
    }

    const pipeStep = () => {
      if (shuttingDown) return promiseResolvedWith(true);
      return performPromiseThen(writer.readyPromise.promise, () => {
        const read = newPromise();
        readableStreamDefaultReaderRead(reader, {
          chunk(chunk) {
            currentWrite = performPromiseThen(writableStreamDefaultWriterWrite(writer, chunk), undefined, noop);
            resolvePromise(read, false);
          },
          close() { resolvePromise(read, true); },
          error(e) { rejectPromise(read, e); },
        });
        return read.promise;
      });
    };
    const pipeLoop = () => {
      const loop = newPromise();
      const next = (done) => {
        if (done) resolvePromise(loop, undefined);
        else performPromiseThen(pipeStep(), next, (e) => rejectPromise(loop, e));
      };
      next(false);
      return loop.promise;
    };

    // Errors must be propagated forward
    if (source.state === 'errored') {
      if (!preventAbort) shutdownWithAnAction(() => writableStreamAbort(dest, source.storedError), true, source.storedError);
      else shutdown(true, source.storedError);
    } else {
      uponRejection(reader.closedPromise.promise, (storedError) => {
        if (!preventAbort) shutdownWithAnAction(() => writableStreamAbort(dest, storedError), true, storedError);
        else shutdown(true, storedError);
      });
    }

    // Errors must be propagated backward
    if (dest.state === 'errored') {
      if (!preventCancel) shutdownWithAnAction(() => readableStreamCancel(source, dest.storedError), true, dest.storedError);
      else shutdown(true, dest.storedError);
    } else {
      uponRejection(writer.closedPromise.promise, (storedError) => {
        if (!preventCancel) shutdownWithAnAction(() => readableStreamCancel(source, storedError), true, storedError);
        else shutdown(true, storedError);
      });
    }

    // Closing must be propagated forward
    if (source.state === 'closed') {
      if (!preventClose) shutdownWithAnAction(() => writableStreamDefaultWriterCloseWithErrorPropagation(writer));
      else shutdown();
    } else {
      uponFulfillment(reader.closedPromise.promise, () => {
        if (!preventClose) shutdownWithAnAction(() => writableStreamDefaultWriterCloseWithErrorPropagation(writer));
        else shutdown();
      });
    }

    // Closing must be propagated backward
    if (writableStreamCloseQueuedOrInFlight(dest) || dest.state === 'closed') {
      const destClosed = new TypeError_('The destination writable stream closed before all data could be piped to it');
      if (!preventCancel) shutdownWithAnAction(() => readableStreamCancel(source, destClosed), true, destClosed);
      else shutdown(true, destClosed);
    }

    setPromiseIsHandledToTrue(pipeLoop());
    return promise.promise;
  }

  // ------------------------------------------------------------------------------------------------------------------
  // ReadableStreamDefaultController (https://streams.spec.whatwg.org/#rs-default-controller-class)
  // ------------------------------------------------------------------------------------------------------------------

  class ReadableStreamDefaultController {
    constructor() {
      throw new TypeError_('Illegal constructor');
    }

    get desiredSize() { return readableStreamDefaultControllerGetDesiredSize(thisRecord(this, 'ReadableStreamDefaultController')); }

    close() {
      const controller = thisRecord(this, 'ReadableStreamDefaultController');
      if (!readableStreamDefaultControllerCanCloseOrEnqueue(controller)) throw new TypeError_('The stream is not in a state that permits close');
      readableStreamDefaultControllerClose(controller);
    }

    enqueue(chunk = undefined) {
      const controller = thisRecord(this, 'ReadableStreamDefaultController');
      if (!readableStreamDefaultControllerCanCloseOrEnqueue(controller)) throw new TypeError_('The stream is not in a state that permits enqueue');
      readableStreamDefaultControllerEnqueue(controller, chunk);
    }

    error(e = undefined) {
      readableStreamDefaultControllerError(thisRecord(this, 'ReadableStreamDefaultController'), e);
    }
  }

  defineInterface(ReadableStreamDefaultController, 'ReadableStreamDefaultController');

  function readableStreamDefaultControllerCallPullIfNeeded(controller) {
    if (!readableStreamDefaultControllerShouldCallPull(controller)) return;
    if (controller.pulling) {
      controller.pullAgain = true;
      return;
    }

    controller.pulling = true;
    const pullPromise = controller.pullAlgorithm();
    uponPromise(pullPromise, () => {
      controller.pulling = false;
      if (controller.pullAgain) {
        controller.pullAgain = false;
        readableStreamDefaultControllerCallPullIfNeeded(controller);
      }
    }, (e) => readableStreamDefaultControllerError(controller, e));
  }

  function readableStreamDefaultControllerShouldCallPull(controller) {
    const stream = controller.stream;
    if (!readableStreamDefaultControllerCanCloseOrEnqueue(controller)) return false;
    if (!controller.started) return false;
    if (isReadableStreamLocked(stream) && readableStreamGetNumReadRequests(stream) > 0) return true;
    return readableStreamDefaultControllerGetDesiredSize(controller) > 0;
  }

  function readableStreamDefaultControllerClearAlgorithms(controller) {
    controller.pullAlgorithm = undefined;
    controller.cancelAlgorithm = undefined;
    controller.strategySizeAlgorithm = undefined;
  }

  function readableStreamDefaultControllerClose(controller) {
    if (!readableStreamDefaultControllerCanCloseOrEnqueue(controller)) return;
    const stream = controller.stream;
    controller.closeRequested = true;
    if (controller.queue.size === 0) {
      readableStreamDefaultControllerClearAlgorithms(controller);
      readableStreamClose(stream);
    }
  }

  function readableStreamDefaultControllerEnqueue(controller, chunk) {
    if (!readableStreamDefaultControllerCanCloseOrEnqueue(controller)) return;
    const stream = controller.stream;
    if (isReadableStreamLocked(stream) && readableStreamGetNumReadRequests(stream) > 0) {
      readableStreamFulfillReadRequest(stream, chunk, false);
    } else {
      let chunkSize;
      try {
        chunkSize = controller.strategySizeAlgorithm(chunk);
      } catch (error) {
        readableStreamDefaultControllerError(controller, error);
        throw error;
      }

      try {
        enqueueValueWithSize(controller, chunk, chunkSize);
      } catch (error) {
        readableStreamDefaultControllerError(controller, error);
        throw error;
      }
    }

    readableStreamDefaultControllerCallPullIfNeeded(controller);
  }

  function readableStreamDefaultControllerError(controller, e) {
    const stream = controller.stream;
    if (stream.state !== 'readable') return;
    resetQueue(controller);
    readableStreamDefaultControllerClearAlgorithms(controller);
    readableStreamError(stream, e);
  }

  function readableStreamDefaultControllerGetDesiredSize(controller) {
    const state = controller.stream.state;
    if (state === 'errored') return null;
    if (state === 'closed') return 0;
    return controller.strategyHWM - controller.queueTotalSize;
  }

  const readableStreamDefaultControllerHasBackpressure = (controller) => !readableStreamDefaultControllerShouldCallPull(controller);
  const readableStreamDefaultControllerCanCloseOrEnqueue = (controller) => !controller.closeRequested && controller.stream.state === 'readable';

  function setUpReadableStreamDefaultController(stream, controller, startAlgorithm, pullAlgorithm, cancelAlgorithm, highWaterMark, sizeAlgorithm) {
    controller.stream = stream;
    resetQueue(controller);
    controller.started = false;
    controller.closeRequested = false;
    controller.pullAgain = false;
    controller.pulling = false;
    controller.strategySizeAlgorithm = sizeAlgorithm;
    controller.strategyHWM = highWaterMark;
    controller.pullAlgorithm = pullAlgorithm;
    controller.cancelAlgorithm = cancelAlgorithm;
    controller.cancelSteps = (reason) => {
      resetQueue(controller);
      const result = controller.cancelAlgorithm(reason);
      readableStreamDefaultControllerClearAlgorithms(controller);
      return result;
    };
    controller.pullSteps = (readRequest) => {
      if (controller.queue.size > 0) {
        const chunk = dequeueValue(controller);
        if (controller.closeRequested && controller.queue.size === 0) {
          readableStreamDefaultControllerClearAlgorithms(controller);
          readableStreamClose(stream);
        } else {
          readableStreamDefaultControllerCallPullIfNeeded(controller);
        }

        readRequest.chunk(chunk);
      } else {
        readableStreamAddReadRequest(stream, readRequest);
        readableStreamDefaultControllerCallPullIfNeeded(controller);
      }
    };
    controller.releaseSteps = noop;
    stream.controller = controller;
    const startResult = startAlgorithm();
    uponPromise(promiseResolvedWith(startResult), () => {
      controller.started = true;
      readableStreamDefaultControllerCallPullIfNeeded(controller);
    }, (r) => readableStreamDefaultControllerError(controller, r));
  }

  function setUpReadableStreamDefaultControllerFromUnderlyingSource(stream, underlyingSource, underlyingSourceDict, highWaterMark, sizeAlgorithm) {
    const controller = createObject(stream.realm, 'ReadableStreamDefaultController');
    let startAlgorithm = () => undefined;
    let pullAlgorithm = () => promiseResolvedWith(undefined);
    let cancelAlgorithm = () => promiseResolvedWith(undefined);
    const { start, pull, cancel } = underlyingSourceDict;
    if (start !== undefined) startAlgorithm = () => apply(start, underlyingSource, [controller.object]);
    if (pull !== undefined) pullAlgorithm = () => promiseCall(pull, underlyingSource, [controller.object]);
    if (cancel !== undefined) cancelAlgorithm = (reason) => promiseCall(cancel, underlyingSource, [reason]);
    setUpReadableStreamDefaultController(stream, controller, startAlgorithm, pullAlgorithm, cancelAlgorithm, highWaterMark, sizeAlgorithm);
  }

  // ------------------------------------------------------------------------------------------------------------------
  // ReadableByteStreamController (https://streams.spec.whatwg.org/#rbs-controller-class)
  // ------------------------------------------------------------------------------------------------------------------

  class ReadableByteStreamController {
    constructor() {
      throw new TypeError_('Illegal constructor');
    }

    get byobRequest() {
      const byobRequest = readableByteStreamControllerGetBYOBRequest(thisRecord(this, 'ReadableByteStreamController'));
      return byobRequest === null ? null : byobRequest.object;
    }

    get desiredSize() { return readableByteStreamControllerGetDesiredSize(thisRecord(this, 'ReadableByteStreamController')); }

    close() {
      const controller = thisRecord(this, 'ReadableByteStreamController');
      if (controller.closeRequested) throw new TypeError_('The stream is already closing');
      if (controller.stream.state !== 'readable') throw new TypeError_('The stream is not readable');
      readableByteStreamControllerClose(controller);
    }

    enqueue(chunk) {
      const controller = thisRecord(this, 'ReadableByteStreamController');
      if (arguments.length < 1) throw new TypeError_("Failed to execute 'enqueue' on 'ReadableByteStreamController': 1 argument required");
      convertArrayBufferView(chunk, 'chunk');
      if (getViewByteLength(chunk) === 0) throw new TypeError_('The chunk must have a non-zero byteLength');
      if (arrayBufferByteLengthOf(getViewBuffer(chunk)) === 0) throw new TypeError_("The chunk's buffer must have a non-zero byteLength");
      if (controller.closeRequested) throw new TypeError_('The stream is closing');
      if (controller.stream.state !== 'readable') throw new TypeError_('The stream is not readable');
      readableByteStreamControllerEnqueue(controller, chunk);
    }

    error(e = undefined) {
      readableByteStreamControllerError(thisRecord(this, 'ReadableByteStreamController'), e);
    }
  }

  defineInterface(ReadableByteStreamController, 'ReadableByteStreamController');

  class ReadableStreamBYOBRequest {
    constructor() {
      throw new TypeError_('Illegal constructor');
    }

    get view() { return thisRecord(this, 'ReadableStreamBYOBRequest').view; }

    respond(bytesWritten) {
      const request = thisRecord(this, 'ReadableStreamBYOBRequest');
      if (arguments.length < 1) throw new TypeError_("Failed to execute 'respond' on 'ReadableStreamBYOBRequest': 1 argument required");
      bytesWritten = convertUnsignedLongLongEnforceRange(bytesWritten, 'bytesWritten');
      if (request.controller === undefined) throw new TypeError_('The BYOB request has been invalidated');
      if (isDetachedBuffer(getViewBuffer(request.view))) throw new TypeError_("The view's buffer has been detached");
      readableByteStreamControllerRespond(request.controller, bytesWritten);
    }

    respondWithNewView(view) {
      const request = thisRecord(this, 'ReadableStreamBYOBRequest');
      if (arguments.length < 1) throw new TypeError_("Failed to execute 'respondWithNewView' on 'ReadableStreamBYOBRequest': 1 argument required");
      convertArrayBufferView(view, 'view');
      if (request.controller === undefined) throw new TypeError_('The BYOB request has been invalidated');
      if (isDetachedBuffer(getViewBuffer(view))) throw new TypeError_("The view's buffer has been detached");
      readableByteStreamControllerRespondWithNewView(request.controller, view);
    }
  }

  defineInterface(ReadableStreamBYOBRequest, 'ReadableStreamBYOBRequest');

  function readableByteStreamControllerCallPullIfNeeded(controller) {
    if (!readableByteStreamControllerShouldCallPull(controller)) return;
    if (controller.pulling) {
      controller.pullAgain = true;
      return;
    }

    controller.pulling = true;
    const pullPromise = controller.pullAlgorithm();
    uponPromise(pullPromise, () => {
      controller.pulling = false;
      if (controller.pullAgain) {
        controller.pullAgain = false;
        readableByteStreamControllerCallPullIfNeeded(controller);
      }
    }, (e) => readableByteStreamControllerError(controller, e));
  }

  function readableByteStreamControllerClearAlgorithms(controller) {
    controller.pullAlgorithm = undefined;
    controller.cancelAlgorithm = undefined;
  }

  function readableByteStreamControllerClearPendingPullIntos(controller) {
    readableByteStreamControllerInvalidateBYOBRequest(controller);
    controller.pendingPullIntos = new List();
  }

  function readableByteStreamControllerClose(controller) {
    const stream = controller.stream;
    if (controller.closeRequested || stream.state !== 'readable') return;
    if (controller.queueTotalSize > 0) {
      controller.closeRequested = true;
      return;
    }

    if (controller.pendingPullIntos.size > 0) {
      const firstPendingPullInto = controller.pendingPullIntos.peek();
      if (firstPendingPullInto.bytesFilled % firstPendingPullInto.elementSize !== 0) {
        const e = new TypeError_('Insufficient bytes to fill elements in the given buffer');
        readableByteStreamControllerError(controller, e);
        throw e;
      }
    }

    readableByteStreamControllerClearAlgorithms(controller);
    readableStreamClose(stream);
  }

  function readableByteStreamControllerCommitPullIntoDescriptor(stream, pullIntoDescriptor) {
    let done = false;
    if (stream.state === 'closed') done = true;
    const filledView = readableByteStreamControllerConvertPullIntoDescriptor(pullIntoDescriptor);
    if (pullIntoDescriptor.readerType === 'default') readableStreamFulfillReadRequest(stream, filledView, done);
    else readableStreamFulfillReadIntoRequest(stream, filledView, done);
  }

  function readableByteStreamControllerConvertPullIntoDescriptor(pullIntoDescriptor) {
    const bytesFilled = pullIntoDescriptor.bytesFilled;
    const elementSize = pullIntoDescriptor.elementSize;
    const buffer = transferArrayBuffer(pullIntoDescriptor.buffer);
    return new pullIntoDescriptor.viewConstructor(buffer, pullIntoDescriptor.byteOffset, bytesFilled / elementSize);
  }

  function readableByteStreamControllerEnqueue(controller, chunk) {
    const stream = controller.stream;
    if (controller.closeRequested || stream.state !== 'readable') return;
    const buffer = getViewBuffer(chunk);
    const byteOffset = getViewByteOffset(chunk);
    const byteLength = getViewByteLength(chunk);
    if (isDetachedBuffer(buffer)) throw new TypeError_("The chunk's buffer has been detached");
    const transferredBuffer = transferArrayBuffer(buffer);
    if (controller.pendingPullIntos.size > 0) {
      const firstPendingPullInto = controller.pendingPullIntos.peek();
      if (isDetachedBuffer(firstPendingPullInto.buffer)) throw new TypeError_("The BYOB request's buffer has been detached");
      readableByteStreamControllerInvalidateBYOBRequest(controller);
      firstPendingPullInto.buffer = transferArrayBuffer(firstPendingPullInto.buffer);
      if (firstPendingPullInto.readerType === 'none') readableByteStreamControllerEnqueueDetachedPullIntoToQueue(controller, firstPendingPullInto);
    }

    if (readableStreamHasDefaultReader(stream)) {
      readableByteStreamControllerProcessReadRequestsUsingQueue(controller);
      if (readableStreamGetNumReadRequests(stream) === 0) {
        readableByteStreamControllerEnqueueChunkToQueue(controller, transferredBuffer, byteOffset, byteLength);
      } else {
        if (controller.pendingPullIntos.size > 0) readableByteStreamControllerShiftPendingPullInto(controller);
        const transferredView = new Uint8Array_(transferredBuffer, byteOffset, byteLength);
        readableStreamFulfillReadRequest(stream, transferredView, false);
      }
    } else if (readableStreamHasBYOBReader(stream)) {
      readableByteStreamControllerEnqueueChunkToQueue(controller, transferredBuffer, byteOffset, byteLength);
      const filledPullIntos = readableByteStreamControllerProcessPullIntoDescriptorsUsingQueue(controller);
      for (const filledPullInto of filledPullIntos) readableByteStreamControllerCommitPullIntoDescriptor(stream, filledPullInto);
    } else {
      readableByteStreamControllerEnqueueChunkToQueue(controller, transferredBuffer, byteOffset, byteLength);
    }

    readableByteStreamControllerCallPullIfNeeded(controller);
  }

  function readableByteStreamControllerEnqueueChunkToQueue(controller, buffer, byteOffset, byteLength) {
    const entry = ObjectCreate(null);
    entry.buffer = buffer;
    entry.byteOffset = byteOffset;
    entry.byteLength = byteLength;
    controller.queue.append(entry);
    controller.queueTotalSize += byteLength;
  }

  function readableByteStreamControllerEnqueueClonedChunkToQueue(controller, buffer, byteOffset, byteLength) {
    let cloneResult;
    try {
      cloneResult = cloneArrayBuffer(buffer, byteOffset, byteLength);
    } catch (error) {
      readableByteStreamControllerError(controller, error);
      throw error;
    }

    readableByteStreamControllerEnqueueChunkToQueue(controller, cloneResult, 0, byteLength);
  }

  function readableByteStreamControllerEnqueueDetachedPullIntoToQueue(controller, pullIntoDescriptor) {
    if (pullIntoDescriptor.bytesFilled > 0) {
      readableByteStreamControllerEnqueueClonedChunkToQueue(controller, pullIntoDescriptor.buffer, pullIntoDescriptor.byteOffset, pullIntoDescriptor.bytesFilled);
    }

    readableByteStreamControllerShiftPendingPullInto(controller);
  }

  function readableByteStreamControllerFillPullIntoDescriptorFromQueue(controller, pullIntoDescriptor) {
    const maxBytesToCopy = MathMin(controller.queueTotalSize, pullIntoDescriptor.byteLength - pullIntoDescriptor.bytesFilled);
    const maxBytesFilled = pullIntoDescriptor.bytesFilled + maxBytesToCopy;
    let totalBytesToCopyRemaining = maxBytesToCopy;
    let ready = false;
    const remainderBytes = maxBytesFilled % pullIntoDescriptor.elementSize;
    const maxAlignedBytes = maxBytesFilled - remainderBytes;
    if (maxAlignedBytes >= pullIntoDescriptor.minimumFill) {
      totalBytesToCopyRemaining = maxAlignedBytes - pullIntoDescriptor.bytesFilled;
      ready = true;
    }

    const queue = controller.queue;
    while (totalBytesToCopyRemaining > 0) {
      const headOfQueue = queue.peek();
      const bytesToCopy = MathMin(totalBytesToCopyRemaining, headOfQueue.byteLength);
      const destStart = pullIntoDescriptor.byteOffset + pullIntoDescriptor.bytesFilled;
      copyDataBlockBytes(pullIntoDescriptor.buffer, destStart, headOfQueue.buffer, headOfQueue.byteOffset, bytesToCopy);
      if (headOfQueue.byteLength === bytesToCopy) {
        queue.shift();
      } else {
        headOfQueue.byteOffset += bytesToCopy;
        headOfQueue.byteLength -= bytesToCopy;
      }

      controller.queueTotalSize -= bytesToCopy;
      readableByteStreamControllerFillHeadPullIntoDescriptor(controller, bytesToCopy, pullIntoDescriptor);
      totalBytesToCopyRemaining -= bytesToCopy;
    }

    return ready;
  }

  function readableByteStreamControllerFillReadRequestFromQueue(controller, readRequest) {
    const entry = controller.queue.shift();
    controller.queueTotalSize -= entry.byteLength;
    readableByteStreamControllerHandleQueueDrain(controller);
    const view = new Uint8Array_(entry.buffer, entry.byteOffset, entry.byteLength);
    readRequest.chunk(view);
  }

  function readableByteStreamControllerGetBYOBRequest(controller) {
    if (controller.byobRequest === null && controller.pendingPullIntos.size > 0) {
      const firstDescriptor = controller.pendingPullIntos.peek();
      const view = new Uint8Array_(firstDescriptor.buffer, firstDescriptor.byteOffset + firstDescriptor.bytesFilled, firstDescriptor.byteLength - firstDescriptor.bytesFilled);
      const byobRequest = createObject(controller.stream.realm, 'ReadableStreamBYOBRequest');
      byobRequest.controller = controller;
      byobRequest.view = view;
      controller.byobRequest = byobRequest;
    }

    return controller.byobRequest;
  }

  function readableByteStreamControllerGetDesiredSize(controller) {
    const state = controller.stream.state;
    if (state === 'errored') return null;
    if (state === 'closed') return 0;
    return controller.strategyHWM - controller.queueTotalSize;
  }

  function readableByteStreamControllerHandleQueueDrain(controller) {
    if (controller.queueTotalSize === 0 && controller.closeRequested) {
      readableByteStreamControllerClearAlgorithms(controller);
      readableStreamClose(controller.stream);
    } else {
      readableByteStreamControllerCallPullIfNeeded(controller);
    }
  }

  function readableByteStreamControllerInvalidateBYOBRequest(controller) {
    if (controller.byobRequest === null) return;
    controller.byobRequest.controller = undefined;
    controller.byobRequest.view = null;
    controller.byobRequest = null;
  }

  function readableByteStreamControllerProcessPullIntoDescriptorsUsingQueue(controller) {
    const filledPullIntos = [];
    while (controller.pendingPullIntos.size > 0) {
      if (controller.queueTotalSize === 0) break;
      const pullIntoDescriptor = controller.pendingPullIntos.peek();
      if (readableByteStreamControllerFillPullIntoDescriptorFromQueue(controller, pullIntoDescriptor)) {
        readableByteStreamControllerShiftPendingPullInto(controller);
        filledPullIntos[filledPullIntos.length] = pullIntoDescriptor;
      }
    }

    return filledPullIntos;
  }

  function readableByteStreamControllerProcessReadRequestsUsingQueue(controller) {
    const reader = controller.stream.reader;
    while (reader.readRequests.size > 0) {
      if (controller.queueTotalSize === 0) return;
      const readRequest = reader.readRequests.shift();
      readableByteStreamControllerFillReadRequestFromQueue(controller, readRequest);
    }
  }

  function readableByteStreamControllerPullInto(controller, view, min, readIntoRequest) {
    const stream = controller.stream;
    let elementSize = 1;
    let ctor = DataView_;
    const kind = getViewKind(view);
    if (kind !== 'DataView') {
      elementSize = elementSizes[kind];
      ctor = typedArrayConstructors[kind];
    }

    const minimumFill = min * elementSize;
    const byteOffset = getViewByteOffset(view);
    const byteLength = getViewByteLength(view);
    let buffer;
    try {
      buffer = transferArrayBuffer(getViewBuffer(view));
    } catch (error) {
      readIntoRequest.error(error);
      return;
    }

    const pullIntoDescriptor = ObjectCreate(null);
    pullIntoDescriptor.buffer = buffer;
    pullIntoDescriptor.bufferByteLength = arrayBufferByteLengthOf(buffer);
    pullIntoDescriptor.byteOffset = byteOffset;
    pullIntoDescriptor.byteLength = byteLength;
    pullIntoDescriptor.bytesFilled = 0;
    pullIntoDescriptor.minimumFill = minimumFill;
    pullIntoDescriptor.elementSize = elementSize;
    pullIntoDescriptor.viewConstructor = ctor;
    pullIntoDescriptor.readerType = 'byob';
    if (controller.pendingPullIntos.size > 0) {
      controller.pendingPullIntos.append(pullIntoDescriptor);
      readableStreamAddReadIntoRequest(stream, readIntoRequest);
      return;
    }

    if (stream.state === 'closed') {
      const emptyView = new ctor(pullIntoDescriptor.buffer, pullIntoDescriptor.byteOffset, 0);
      readIntoRequest.close(emptyView);
      return;
    }

    if (controller.queueTotalSize > 0) {
      if (readableByteStreamControllerFillPullIntoDescriptorFromQueue(controller, pullIntoDescriptor)) {
        const filledView = readableByteStreamControllerConvertPullIntoDescriptor(pullIntoDescriptor);
        readableByteStreamControllerHandleQueueDrain(controller);
        readIntoRequest.chunk(filledView);
        return;
      }

      if (controller.closeRequested) {
        const e = new TypeError_('Insufficient bytes to fill elements in the given buffer');
        readableByteStreamControllerError(controller, e);
        readIntoRequest.error(e);
        return;
      }
    }

    controller.pendingPullIntos.append(pullIntoDescriptor);
    readableStreamAddReadIntoRequest(stream, readIntoRequest);
    readableByteStreamControllerCallPullIfNeeded(controller);
  }

  function readableByteStreamControllerRespond(controller, bytesWritten) {
    const firstDescriptor = controller.pendingPullIntos.peek();
    const state = controller.stream.state;
    if (state === 'closed') {
      if (bytesWritten !== 0) throw new TypeError_('bytesWritten must be 0 when calling respond() on a closed stream');
    } else {
      if (bytesWritten === 0) throw new TypeError_('bytesWritten must be greater than 0 when calling respond() on a readable stream');
      if (firstDescriptor.bytesFilled + bytesWritten > firstDescriptor.byteLength) throw new RangeError_('bytesWritten out of range');
    }

    firstDescriptor.buffer = transferArrayBuffer(firstDescriptor.buffer);
    readableByteStreamControllerRespondInternal(controller, bytesWritten);
  }

  function readableByteStreamControllerRespondInClosedState(controller, firstDescriptor) {
    if (firstDescriptor.readerType === 'none') readableByteStreamControllerShiftPendingPullInto(controller);
    const stream = controller.stream;
    if (readableStreamHasBYOBReader(stream)) {
      const filledPullIntos = [];
      while (filledPullIntos.length < readableStreamGetNumReadIntoRequests(stream)) {
        filledPullIntos[filledPullIntos.length] = readableByteStreamControllerShiftPendingPullInto(controller);
      }

      for (const pullIntoDescriptor of filledPullIntos) readableByteStreamControllerCommitPullIntoDescriptor(stream, pullIntoDescriptor);
    }
  }

  function readableByteStreamControllerRespondInReadableState(controller, bytesWritten, pullIntoDescriptor) {
    readableByteStreamControllerFillHeadPullIntoDescriptor(controller, bytesWritten, pullIntoDescriptor);
    if (pullIntoDescriptor.readerType === 'none') {
      readableByteStreamControllerEnqueueDetachedPullIntoToQueue(controller, pullIntoDescriptor);
      const filledPullIntos = readableByteStreamControllerProcessPullIntoDescriptorsUsingQueue(controller);
      for (const filledPullInto of filledPullIntos) readableByteStreamControllerCommitPullIntoDescriptor(controller.stream, filledPullInto);
      return;
    }

    if (pullIntoDescriptor.bytesFilled < pullIntoDescriptor.minimumFill) return;
    readableByteStreamControllerShiftPendingPullInto(controller);
    const remainderSize = pullIntoDescriptor.bytesFilled % pullIntoDescriptor.elementSize;
    if (remainderSize > 0) {
      const end = pullIntoDescriptor.byteOffset + pullIntoDescriptor.bytesFilled;
      readableByteStreamControllerEnqueueClonedChunkToQueue(controller, pullIntoDescriptor.buffer, end - remainderSize, remainderSize);
    }

    pullIntoDescriptor.bytesFilled -= remainderSize;
    const filledPullIntos = readableByteStreamControllerProcessPullIntoDescriptorsUsingQueue(controller);
    readableByteStreamControllerCommitPullIntoDescriptor(controller.stream, pullIntoDescriptor);
    for (const filledPullInto of filledPullIntos) readableByteStreamControllerCommitPullIntoDescriptor(controller.stream, filledPullInto);
  }

  function readableByteStreamControllerRespondInternal(controller, bytesWritten) {
    const firstDescriptor = controller.pendingPullIntos.peek();
    readableByteStreamControllerInvalidateBYOBRequest(controller);
    const state = controller.stream.state;
    if (state === 'closed') readableByteStreamControllerRespondInClosedState(controller, firstDescriptor);
    else readableByteStreamControllerRespondInReadableState(controller, bytesWritten, firstDescriptor);
    readableByteStreamControllerCallPullIfNeeded(controller);
  }

  function readableByteStreamControllerRespondWithNewView(controller, view) {
    const firstDescriptor = controller.pendingPullIntos.peek();
    const state = controller.stream.state;
    const viewByteLength = getViewByteLength(view);
    if (state === 'closed') {
      if (viewByteLength !== 0) throw new TypeError_("The view's length must be 0 when calling respondWithNewView() on a closed stream");
    } else if (viewByteLength === 0) {
      throw new TypeError_("The view's length must be greater than 0 when calling respondWithNewView() on a readable stream");
    }

    const viewBuffer = getViewBuffer(view);
    if (firstDescriptor.byteOffset + firstDescriptor.bytesFilled !== getViewByteOffset(view)) throw new RangeError_('The region specified by view does not match byobRequest');
    if (firstDescriptor.bufferByteLength !== arrayBufferByteLengthOf(viewBuffer)) throw new RangeError_('The buffer of view has different capacity than byobRequest');
    if (firstDescriptor.bytesFilled + viewByteLength > firstDescriptor.byteLength) throw new RangeError_('The region specified by view is larger than byobRequest');
    firstDescriptor.buffer = transferArrayBuffer(viewBuffer);
    readableByteStreamControllerRespondInternal(controller, viewByteLength);
  }

  const readableByteStreamControllerShiftPendingPullInto = (controller) => controller.pendingPullIntos.shift();

  function readableByteStreamControllerShouldCallPull(controller) {
    const stream = controller.stream;
    if (stream.state !== 'readable') return false;
    if (controller.closeRequested) return false;
    if (!controller.started) return false;
    if (readableStreamHasDefaultReader(stream) && readableStreamGetNumReadRequests(stream) > 0) return true;
    if (readableStreamHasBYOBReader(stream) && readableStreamGetNumReadIntoRequests(stream) > 0) return true;
    return readableByteStreamControllerGetDesiredSize(controller) > 0;
  }

  function readableByteStreamControllerFillHeadPullIntoDescriptor(controller, size, pullIntoDescriptor) {
    pullIntoDescriptor.bytesFilled += size;
  }

  function readableByteStreamControllerError(controller, e) {
    const stream = controller.stream;
    if (stream.state !== 'readable') return;
    readableByteStreamControllerClearPendingPullIntos(controller);
    resetQueue(controller);
    readableByteStreamControllerClearAlgorithms(controller);
    readableStreamError(stream, e);
  }

  function setUpReadableByteStreamController(stream, controller, startAlgorithm, pullAlgorithm, cancelAlgorithm, highWaterMark, autoAllocateChunkSize) {
    controller.stream = stream;
    controller.pullAgain = false;
    controller.pulling = false;
    controller.byobRequest = null;
    resetQueue(controller);
    controller.closeRequested = false;
    controller.started = false;
    controller.strategyHWM = highWaterMark;
    controller.pullAlgorithm = pullAlgorithm;
    controller.cancelAlgorithm = cancelAlgorithm;
    controller.autoAllocateChunkSize = autoAllocateChunkSize;
    controller.pendingPullIntos = new List();
    controller.cancelSteps = (reason) => {
      readableByteStreamControllerClearPendingPullIntos(controller);
      resetQueue(controller);
      const result = controller.cancelAlgorithm(reason);
      readableByteStreamControllerClearAlgorithms(controller);
      return result;
    };
    controller.pullSteps = (readRequest) => {
      if (controller.queueTotalSize > 0) {
        readableByteStreamControllerFillReadRequestFromQueue(controller, readRequest);
        return;
      }

      const chunkSize = controller.autoAllocateChunkSize;
      if (chunkSize !== undefined) {
        let buffer;
        try {
          buffer = new ArrayBuffer_(chunkSize);
        } catch (error) {
          readRequest.error(error);
          return;
        }

        const pullIntoDescriptor = ObjectCreate(null);
        pullIntoDescriptor.buffer = buffer;
        pullIntoDescriptor.bufferByteLength = chunkSize;
        pullIntoDescriptor.byteOffset = 0;
        pullIntoDescriptor.byteLength = chunkSize;
        pullIntoDescriptor.bytesFilled = 0;
        pullIntoDescriptor.minimumFill = 1;
        pullIntoDescriptor.elementSize = 1;
        pullIntoDescriptor.viewConstructor = Uint8Array_;
        pullIntoDescriptor.readerType = 'default';
        controller.pendingPullIntos.append(pullIntoDescriptor);
      }

      readableStreamAddReadRequest(stream, readRequest);
      readableByteStreamControllerCallPullIfNeeded(controller);
    };
    controller.releaseSteps = () => {
      if (controller.pendingPullIntos.size > 0) {
        const firstPendingPullInto = controller.pendingPullIntos.peek();
        firstPendingPullInto.readerType = 'none';
        controller.pendingPullIntos = new List();
        controller.pendingPullIntos.append(firstPendingPullInto);
      }
    };
    stream.controller = controller;
    const startResult = startAlgorithm();
    uponPromise(promiseResolvedWith(startResult), () => {
      controller.started = true;
      readableByteStreamControllerCallPullIfNeeded(controller);
    }, (r) => readableByteStreamControllerError(controller, r));
  }

  function setUpReadableByteStreamControllerFromUnderlyingSource(stream, underlyingSource, underlyingSourceDict, highWaterMark) {
    const controller = createObject(stream.realm, 'ReadableByteStreamController');
    let startAlgorithm = () => undefined;
    let pullAlgorithm = () => promiseResolvedWith(undefined);
    let cancelAlgorithm = () => promiseResolvedWith(undefined);
    const { start, pull, cancel } = underlyingSourceDict;
    if (start !== undefined) startAlgorithm = () => apply(start, underlyingSource, [controller.object]);
    if (pull !== undefined) pullAlgorithm = () => promiseCall(pull, underlyingSource, [controller.object]);
    if (cancel !== undefined) cancelAlgorithm = (reason) => promiseCall(cancel, underlyingSource, [reason]);
    const autoAllocateChunkSize = underlyingSourceDict.autoAllocateChunkSize;
    if (autoAllocateChunkSize === 0) throw new TypeError_('autoAllocateChunkSize must be greater than 0');
    setUpReadableByteStreamController(stream, controller, startAlgorithm, pullAlgorithm, cancelAlgorithm, highWaterMark, autoAllocateChunkSize);
  }

  // ------------------------------------------------------------------------------------------------------------------
  // WritableStream (https://streams.spec.whatwg.org/#ws-class)
  // ------------------------------------------------------------------------------------------------------------------

  function convertUnderlyingSink(value) {
    const result = ObjectCreate(null);
    const abort = getMember(value, 'abort');
    result.abort = abort === undefined ? undefined : convertCallback(abort, 'underlyingSink.abort');
    const close = getMember(value, 'close');
    result.close = close === undefined ? undefined : convertCallback(close, 'underlyingSink.close');
    const start = getMember(value, 'start');
    result.start = start === undefined ? undefined : convertCallback(start, 'underlyingSink.start');
    result.type = getMember(value, 'type');
    const write = getMember(value, 'write');
    result.write = write === undefined ? undefined : convertCallback(write, 'underlyingSink.write');
    return result;
  }

  class WritableStream {
    constructor(underlyingSink = undefined, strategy = undefined) {
      if (underlyingSink === undefined) underlyingSink = null;
      else convertObject(underlyingSink, 'underlyingSink');
      const strategyDict = convertQueuingStrategy(strategy, 'strategy');
      const underlyingSinkDict = convertUnderlyingSink(underlyingSink);
      if (underlyingSinkDict.type !== undefined) throw new RangeError_('Invalid type is specified');
      const stream = createRecord(this, 'WritableStream');
      initializeWritableStream(stream);
      const sizeAlgorithm = extractSizeAlgorithm(strategyDict);
      const highWaterMark = extractHighWaterMark(strategyDict, 1);
      setUpWritableStreamDefaultControllerFromUnderlyingSink(stream, underlyingSink, underlyingSinkDict, highWaterMark, sizeAlgorithm);
    }

    get locked() { return isWritableStreamLocked(thisRecord(this, 'WritableStream')); }

    abort(reason = undefined) {
      return promiseOperation(() => {
        const stream = thisRecord(this, 'WritableStream');
        if (isWritableStreamLocked(stream)) return promiseRejectedWith(new TypeError_('Cannot abort a stream that already has a writer'));
        return writableStreamAbort(stream, reason);
      });
    }

    close() {
      return promiseOperation(() => {
        const stream = thisRecord(this, 'WritableStream');
        if (isWritableStreamLocked(stream)) return promiseRejectedWith(new TypeError_('Cannot close a stream that already has a writer'));
        if (writableStreamCloseQueuedOrInFlight(stream)) return promiseRejectedWith(new TypeError_('Cannot close an already-closing stream'));
        return writableStreamClose(stream);
      });
    }

    getWriter() {
      return acquireWritableStreamDefaultWriter(thisRecord(this, 'WritableStream')).object;
    }
  }

  defineInterface(WritableStream, 'WritableStream');

  function acquireWritableStreamDefaultWriter(stream) {
    const writer = createObject(stream.realm, 'WritableStreamDefaultWriter');
    setUpWritableStreamDefaultWriter(writer, stream);
    return writer;
  }

  function createWritableStream(objectRealm, startAlgorithm, writeAlgorithm, closeAlgorithm, abortAlgorithm, highWaterMark = 1, sizeAlgorithm = () => 1) {
    const stream = createObject(objectRealm, 'WritableStream');
    initializeWritableStream(stream);
    const controller = createObject(objectRealm, 'WritableStreamDefaultController');
    setUpWritableStreamDefaultController(stream, controller, startAlgorithm, writeAlgorithm, closeAlgorithm, abortAlgorithm, highWaterMark, sizeAlgorithm);
    return stream;
  }

  function initializeWritableStream(stream) {
    stream.state = 'writable';
    stream.storedError = undefined;
    stream.writer = undefined;
    stream.controller = undefined;
    stream.inFlightWriteRequest = undefined;
    stream.closeRequest = undefined;
    stream.inFlightCloseRequest = undefined;
    stream.pendingAbortRequest = undefined;
    stream.writeRequests = new List();
    stream.backpressure = false;
  }

  const isWritableStreamLocked = (stream) => stream.writer !== undefined;

  function writableStreamAbort(stream, reason) {
    if (stream.state === 'closed' || stream.state === 'errored') return promiseResolvedWith(undefined);
    apply(abortControllerAbort, stream.controller.abortController, [reason]);
    const state = stream.state;
    if (state === 'closed' || state === 'errored') return promiseResolvedWith(undefined);
    if (stream.pendingAbortRequest !== undefined) return stream.pendingAbortRequest.promise.promise;
    let wasAlreadyErroring = false;
    if (state === 'erroring') {
      wasAlreadyErroring = true;
      reason = undefined;
    }

    const promise = newPromise();
    const pendingAbortRequest = ObjectCreate(null);
    pendingAbortRequest.promise = promise;
    pendingAbortRequest.reason = reason;
    pendingAbortRequest.wasAlreadyErroring = wasAlreadyErroring;
    stream.pendingAbortRequest = pendingAbortRequest;
    if (!wasAlreadyErroring) writableStreamStartErroring(stream, reason);
    return promise.promise;
  }

  function writableStreamClose(stream) {
    const state = stream.state;
    if (state === 'closed' || state === 'errored') return promiseRejectedWith(new TypeError_(`The stream (in ${state} state) is not in the writable state and cannot be closed`));
    const promise = newPromise();
    stream.closeRequest = promise;
    const writer = stream.writer;
    if (writer !== undefined && stream.backpressure && state === 'writable') resolvePromise(writer.readyPromise, undefined);
    writableStreamDefaultControllerClose(stream.controller);
    return promise.promise;
  }

  function writableStreamAddWriteRequest(stream) {
    const promise = newPromise();
    stream.writeRequests.append(promise);
    return promise.promise;
  }

  const writableStreamCloseQueuedOrInFlight = (stream) => stream.closeRequest !== undefined || stream.inFlightCloseRequest !== undefined;

  function writableStreamDealWithRejection(stream, error) {
    if (stream.state === 'writable') {
      writableStreamStartErroring(stream, error);
      return;
    }

    writableStreamFinishErroring(stream);
  }

  function writableStreamFinishErroring(stream) {
    stream.state = 'errored';
    resetQueue(stream.controller);
    const storedError = stream.storedError;
    for (const writeRequest of stream.writeRequests.toArray()) rejectPromise(writeRequest, storedError);
    stream.writeRequests = new List();
    if (stream.pendingAbortRequest === undefined) {
      writableStreamRejectCloseAndClosedPromiseIfNeeded(stream);
      return;
    }

    const abortRequest = stream.pendingAbortRequest;
    stream.pendingAbortRequest = undefined;
    if (abortRequest.wasAlreadyErroring) {
      rejectPromise(abortRequest.promise, storedError);
      writableStreamRejectCloseAndClosedPromiseIfNeeded(stream);
      return;
    }

    const promise = stream.controller.abortSteps(abortRequest.reason);
    uponPromise(promise, () => {
      resolvePromise(abortRequest.promise, undefined);
      writableStreamRejectCloseAndClosedPromiseIfNeeded(stream);
    }, (reason) => {
      rejectPromise(abortRequest.promise, reason);
      writableStreamRejectCloseAndClosedPromiseIfNeeded(stream);
    });
  }

  function writableStreamFinishInFlightClose(stream) {
    resolvePromise(stream.inFlightCloseRequest, undefined);
    stream.inFlightCloseRequest = undefined;
    if (stream.state === 'erroring') {
      stream.storedError = undefined;
      if (stream.pendingAbortRequest !== undefined) {
        resolvePromise(stream.pendingAbortRequest.promise, undefined);
        stream.pendingAbortRequest = undefined;
      }
    }

    stream.state = 'closed';
    const writer = stream.writer;
    if (writer !== undefined) resolvePromise(writer.closedPromise, undefined);
  }

  function writableStreamFinishInFlightCloseWithError(stream, error) {
    rejectPromise(stream.inFlightCloseRequest, error);
    stream.inFlightCloseRequest = undefined;
    if (stream.pendingAbortRequest !== undefined) {
      rejectPromise(stream.pendingAbortRequest.promise, error);
      stream.pendingAbortRequest = undefined;
    }

    writableStreamDealWithRejection(stream, error);
  }

  function writableStreamFinishInFlightWrite(stream) {
    resolvePromise(stream.inFlightWriteRequest, undefined);
    stream.inFlightWriteRequest = undefined;
  }

  function writableStreamFinishInFlightWriteWithError(stream, error) {
    rejectPromise(stream.inFlightWriteRequest, error);
    stream.inFlightWriteRequest = undefined;
    writableStreamDealWithRejection(stream, error);
  }

  const writableStreamHasOperationMarkedInFlight = (stream) => stream.inFlightWriteRequest !== undefined || stream.inFlightCloseRequest !== undefined;

  function writableStreamMarkCloseRequestInFlight(stream) {
    stream.inFlightCloseRequest = stream.closeRequest;
    stream.closeRequest = undefined;
  }

  function writableStreamMarkFirstWriteRequestInFlight(stream) {
    stream.inFlightWriteRequest = stream.writeRequests.shift();
  }

  function writableStreamRejectCloseAndClosedPromiseIfNeeded(stream) {
    if (stream.closeRequest !== undefined) {
      rejectPromise(stream.closeRequest, stream.storedError);
      stream.closeRequest = undefined;
    }

    const writer = stream.writer;
    if (writer !== undefined) {
      rejectPromise(writer.closedPromise, stream.storedError);
      setPromiseIsHandledToTrue(writer.closedPromise.promise);
    }
  }

  function writableStreamStartErroring(stream, reason) {
    const controller = stream.controller;
    stream.state = 'erroring';
    stream.storedError = reason;
    const writer = stream.writer;
    if (writer !== undefined) writableStreamDefaultWriterEnsureReadyPromiseRejected(writer, reason);
    if (!writableStreamHasOperationMarkedInFlight(stream) && controller.started) writableStreamFinishErroring(stream);
  }

  function writableStreamUpdateBackpressure(stream, backpressure) {
    const writer = stream.writer;
    if (writer !== undefined && backpressure !== stream.backpressure) {
      if (backpressure) writer.readyPromise = newPromise();
      else resolvePromise(writer.readyPromise, undefined);
    }

    stream.backpressure = backpressure;
  }

  // ------------------------------------------------------------------------------------------------------------------
  // WritableStreamDefaultWriter (https://streams.spec.whatwg.org/#default-writer-class)
  // ------------------------------------------------------------------------------------------------------------------

  class WritableStreamDefaultWriter {
    constructor(stream) {
      if (arguments.length < 1) throw new TypeError_("Failed to construct 'WritableStreamDefaultWriter': 1 argument required");
      const streamRecord = getRecord(stream, 'WritableStream');
      if (streamRecord === undefined) throw new TypeError_('The argument is not a WritableStream');
      setUpWritableStreamDefaultWriter(createRecord(this, 'WritableStreamDefaultWriter'), streamRecord);
    }

    get closed() {
      return promiseOperation(() => thisRecord(this, 'WritableStreamDefaultWriter').closedPromise.promise);
    }

    get desiredSize() {
      const writer = thisRecord(this, 'WritableStreamDefaultWriter');
      if (writer.stream === undefined) throw new TypeError_('The writer is released');
      return writableStreamDefaultWriterGetDesiredSize(writer);
    }

    get ready() {
      return promiseOperation(() => thisRecord(this, 'WritableStreamDefaultWriter').readyPromise.promise);
    }

    abort(reason = undefined) {
      return promiseOperation(() => {
        const writer = thisRecord(this, 'WritableStreamDefaultWriter');
        if (writer.stream === undefined) return promiseRejectedWith(new TypeError_('The writer is released'));
        return writableStreamAbort(writer.stream, reason);
      });
    }

    close() {
      return promiseOperation(() => {
        const writer = thisRecord(this, 'WritableStreamDefaultWriter');
        const stream = writer.stream;
        if (stream === undefined) return promiseRejectedWith(new TypeError_('The writer is released'));
        if (writableStreamCloseQueuedOrInFlight(stream)) return promiseRejectedWith(new TypeError_('Cannot close an already-closing stream'));
        return writableStreamClose(stream);
      });
    }

    releaseLock() {
      const writer = thisRecord(this, 'WritableStreamDefaultWriter');
      if (writer.stream === undefined) return;
      writableStreamDefaultWriterRelease(writer);
    }

    write(chunk = undefined) {
      return promiseOperation(() => {
        const writer = thisRecord(this, 'WritableStreamDefaultWriter');
        if (writer.stream === undefined) return promiseRejectedWith(new TypeError_('The writer is released'));
        return writableStreamDefaultWriterWrite(writer, chunk);
      });
    }
  }

  defineInterface(WritableStreamDefaultWriter, 'WritableStreamDefaultWriter');

  function setUpWritableStreamDefaultWriter(writer, stream) {
    if (isWritableStreamLocked(stream)) throw new TypeError_('The stream is locked');
    writer.stream = stream;
    stream.writer = writer;
    const state = stream.state;
    if (state === 'writable') {
      writer.readyPromise = !writableStreamCloseQueuedOrInFlight(stream) && stream.backpressure ? newPromise() : resolvedDeferred(undefined);
      writer.closedPromise = newPromise();
    } else if (state === 'erroring') {
      writer.readyPromise = rejectedDeferred(stream.storedError);
      writer.closedPromise = newPromise();
    } else if (state === 'closed') {
      writer.readyPromise = resolvedDeferred(undefined);
      writer.closedPromise = resolvedDeferred(undefined);
    } else {
      writer.readyPromise = rejectedDeferred(stream.storedError);
      writer.closedPromise = rejectedDeferred(stream.storedError);
    }
  }

  function writableStreamDefaultWriterCloseWithErrorPropagation(writer) {
    const stream = writer.stream;
    const state = stream.state;
    if (writableStreamCloseQueuedOrInFlight(stream) || state === 'closed') return promiseResolvedWith(undefined);
    if (state === 'errored') return promiseRejectedWith(stream.storedError);
    return writableStreamClose(stream);
  }

  function writableStreamDefaultWriterEnsureClosedPromiseRejected(writer, error) {
    if (writer.closedPromise.state === 'pending') rejectPromise(writer.closedPromise, error);
    else writer.closedPromise = rejectedDeferred(error);
    setPromiseIsHandledToTrue(writer.closedPromise.promise);
  }

  function writableStreamDefaultWriterEnsureReadyPromiseRejected(writer, error) {
    if (writer.readyPromise.state === 'pending') rejectPromise(writer.readyPromise, error);
    else writer.readyPromise = rejectedDeferred(error);
    setPromiseIsHandledToTrue(writer.readyPromise.promise);
  }

  function writableStreamDefaultWriterGetDesiredSize(writer) {
    const stream = writer.stream;
    const state = stream.state;
    if (state === 'errored' || state === 'erroring') return null;
    if (state === 'closed') return 0;
    return writableStreamDefaultControllerGetDesiredSize(stream.controller);
  }

  function writableStreamDefaultWriterRelease(writer) {
    const stream = writer.stream;
    const releasedError = new TypeError_('The writer was released');
    writableStreamDefaultWriterEnsureReadyPromiseRejected(writer, releasedError);
    writableStreamDefaultWriterEnsureClosedPromiseRejected(writer, releasedError);
    stream.writer = undefined;
    writer.stream = undefined;
  }

  function writableStreamDefaultWriterWrite(writer, chunk) {
    const stream = writer.stream;
    const controller = stream.controller;
    const chunkSize = writableStreamDefaultControllerGetChunkSize(controller, chunk);
    if (stream !== writer.stream) return promiseRejectedWith(new TypeError_('The writer was released'));
    const state = stream.state;
    if (state === 'errored') return promiseRejectedWith(stream.storedError);
    if (writableStreamCloseQueuedOrInFlight(stream) || state === 'closed') return promiseRejectedWith(new TypeError_('The stream is closing or closed and cannot be written to'));
    if (state === 'erroring') return promiseRejectedWith(stream.storedError);
    const promise = writableStreamAddWriteRequest(stream);
    writableStreamDefaultControllerWrite(controller, chunk, chunkSize);
    return promise;
  }

  // ------------------------------------------------------------------------------------------------------------------
  // WritableStreamDefaultController (https://streams.spec.whatwg.org/#ws-default-controller-class)
  // ------------------------------------------------------------------------------------------------------------------

  const closeSentinel = ObjectCreate(null);

  class WritableStreamDefaultController {
    constructor() {
      throw new TypeError_('Illegal constructor');
    }

    get signal() {
      return apply(abortControllerSignal, thisRecord(this, 'WritableStreamDefaultController').abortController, []);
    }

    error(e = undefined) {
      const controller = thisRecord(this, 'WritableStreamDefaultController');
      if (controller.stream.state !== 'writable') return;
      writableStreamDefaultControllerError(controller, e);
    }
  }

  defineInterface(WritableStreamDefaultController, 'WritableStreamDefaultController');

  function setUpWritableStreamDefaultController(stream, controller, startAlgorithm, writeAlgorithm, closeAlgorithm, abortAlgorithm, highWaterMark, sizeAlgorithm) {
    controller.stream = stream;
    stream.controller = controller;
    resetQueue(controller);
    controller.abortController = new stream.realm.AbortController();
    controller.started = false;
    controller.strategySizeAlgorithm = sizeAlgorithm;
    controller.strategyHWM = highWaterMark;
    controller.writeAlgorithm = writeAlgorithm;
    controller.closeAlgorithm = closeAlgorithm;
    controller.abortAlgorithm = abortAlgorithm;
    controller.abortSteps = (reason) => {
      const result = controller.abortAlgorithm(reason);
      writableStreamDefaultControllerClearAlgorithms(controller);
      return result;
    };
    const backpressure = writableStreamDefaultControllerGetBackpressure(controller);
    writableStreamUpdateBackpressure(stream, backpressure);
    const startResult = startAlgorithm();
    uponPromise(promiseResolvedWith(startResult), () => {
      controller.started = true;
      writableStreamDefaultControllerAdvanceQueueIfNeeded(controller);
    }, (r) => {
      controller.started = true;
      writableStreamDealWithRejection(stream, r);
    });
  }

  function setUpWritableStreamDefaultControllerFromUnderlyingSink(stream, underlyingSink, underlyingSinkDict, highWaterMark, sizeAlgorithm) {
    const controller = createObject(stream.realm, 'WritableStreamDefaultController');
    let startAlgorithm = () => undefined;
    let writeAlgorithm = () => promiseResolvedWith(undefined);
    let closeAlgorithm = () => promiseResolvedWith(undefined);
    let abortAlgorithm = () => promiseResolvedWith(undefined);
    const { start, write, close, abort } = underlyingSinkDict;
    if (start !== undefined) startAlgorithm = () => apply(start, underlyingSink, [controller.object]);
    if (write !== undefined) writeAlgorithm = (chunk) => promiseCall(write, underlyingSink, [chunk, controller.object]);
    if (close !== undefined) closeAlgorithm = () => promiseCall(close, underlyingSink, []);
    if (abort !== undefined) abortAlgorithm = (reason) => promiseCall(abort, underlyingSink, [reason]);
    setUpWritableStreamDefaultController(stream, controller, startAlgorithm, writeAlgorithm, closeAlgorithm, abortAlgorithm, highWaterMark, sizeAlgorithm);
  }

  function writableStreamDefaultControllerAdvanceQueueIfNeeded(controller) {
    const stream = controller.stream;
    if (!controller.started) return;
    if (stream.inFlightWriteRequest !== undefined) return;
    const state = stream.state;
    if (state === 'erroring') {
      writableStreamFinishErroring(stream);
      return;
    }

    if (controller.queue.size === 0) return;
    const value = peekQueueValue(controller);
    if (value === closeSentinel) writableStreamDefaultControllerProcessClose(controller);
    else writableStreamDefaultControllerProcessWrite(controller, value);
  }

  function writableStreamDefaultControllerClearAlgorithms(controller) {
    controller.writeAlgorithm = undefined;
    controller.closeAlgorithm = undefined;
    controller.abortAlgorithm = undefined;
    controller.strategySizeAlgorithm = undefined;
  }

  function writableStreamDefaultControllerClose(controller) {
    enqueueValueWithSize(controller, closeSentinel, 0);
    writableStreamDefaultControllerAdvanceQueueIfNeeded(controller);
  }

  function writableStreamDefaultControllerError(controller, error) {
    writableStreamDefaultControllerClearAlgorithms(controller);
    writableStreamStartErroring(controller.stream, error);
  }

  function writableStreamDefaultControllerErrorIfNeeded(controller, error) {
    if (controller.stream.state === 'writable') writableStreamDefaultControllerError(controller, error);
  }

  const writableStreamDefaultControllerGetBackpressure = (controller) => writableStreamDefaultControllerGetDesiredSize(controller) <= 0;

  function writableStreamDefaultControllerGetChunkSize(controller, chunk) {
    if (controller.strategySizeAlgorithm === undefined) return 1;
    try {
      return controller.strategySizeAlgorithm(chunk);
    } catch (error) {
      writableStreamDefaultControllerErrorIfNeeded(controller, error);
      return 1;
    }
  }

  const writableStreamDefaultControllerGetDesiredSize = (controller) => controller.strategyHWM - controller.queueTotalSize;

  function writableStreamDefaultControllerProcessClose(controller) {
    const stream = controller.stream;
    writableStreamMarkCloseRequestInFlight(stream);
    dequeueValue(controller);
    const sinkClosePromise = controller.closeAlgorithm();
    writableStreamDefaultControllerClearAlgorithms(controller);
    uponPromise(sinkClosePromise, () => writableStreamFinishInFlightClose(stream), (reason) => writableStreamFinishInFlightCloseWithError(stream, reason));
  }

  function writableStreamDefaultControllerProcessWrite(controller, chunk) {
    const stream = controller.stream;
    writableStreamMarkFirstWriteRequestInFlight(stream);
    const sinkWritePromise = controller.writeAlgorithm(chunk);
    uponPromise(sinkWritePromise, () => {
      writableStreamFinishInFlightWrite(stream);
      const state = stream.state;
      dequeueValue(controller);
      if (!writableStreamCloseQueuedOrInFlight(stream) && state === 'writable') {
        const backpressure = writableStreamDefaultControllerGetBackpressure(controller);
        writableStreamUpdateBackpressure(stream, backpressure);
      }

      writableStreamDefaultControllerAdvanceQueueIfNeeded(controller);
    }, (reason) => {
      if (stream.state === 'writable') writableStreamDefaultControllerClearAlgorithms(controller);
      writableStreamFinishInFlightWriteWithError(stream, reason);
    });
  }

  function writableStreamDefaultControllerWrite(controller, chunk, chunkSize) {
    try {
      enqueueValueWithSize(controller, chunk, chunkSize);
    } catch (error) {
      writableStreamDefaultControllerErrorIfNeeded(controller, error);
      return;
    }

    const stream = controller.stream;
    if (!writableStreamCloseQueuedOrInFlight(stream) && stream.state === 'writable') {
      const backpressure = writableStreamDefaultControllerGetBackpressure(controller);
      writableStreamUpdateBackpressure(stream, backpressure);
    }

    writableStreamDefaultControllerAdvanceQueueIfNeeded(controller);
  }

  // ------------------------------------------------------------------------------------------------------------------
  // TransformStream (https://streams.spec.whatwg.org/#ts-class)
  // ------------------------------------------------------------------------------------------------------------------

  function convertTransformer(value) {
    const result = ObjectCreate(null);
    const cancel = getMember(value, 'cancel');
    result.cancel = cancel === undefined ? undefined : convertCallback(cancel, 'transformer.cancel');
    const flush = getMember(value, 'flush');
    result.flush = flush === undefined ? undefined : convertCallback(flush, 'transformer.flush');
    result.readableType = getMember(value, 'readableType');
    const start = getMember(value, 'start');
    result.start = start === undefined ? undefined : convertCallback(start, 'transformer.start');
    const transform = getMember(value, 'transform');
    result.transform = transform === undefined ? undefined : convertCallback(transform, 'transformer.transform');
    result.writableType = getMember(value, 'writableType');
    return result;
  }

  class TransformStream {
    constructor(transformer = undefined, writableStrategy = undefined, readableStrategy = undefined) {
      if (transformer === undefined) transformer = null;
      else convertObject(transformer, 'transformer');
      const writableStrategyDict = convertQueuingStrategy(writableStrategy, 'writableStrategy');
      const readableStrategyDict = convertQueuingStrategy(readableStrategy, 'readableStrategy');
      const transformerDict = convertTransformer(transformer);
      if (transformerDict.readableType !== undefined) throw new RangeError_('Invalid readableType specified');
      if (transformerDict.writableType !== undefined) throw new RangeError_('Invalid writableType specified');
      const readableHighWaterMark = extractHighWaterMark(readableStrategyDict, 0);
      const readableSizeAlgorithm = extractSizeAlgorithm(readableStrategyDict);
      const writableHighWaterMark = extractHighWaterMark(writableStrategyDict, 1);
      const writableSizeAlgorithm = extractSizeAlgorithm(writableStrategyDict);
      const startPromise = newPromise();
      const stream = createRecord(this, 'TransformStream');
      initializeTransformStream(stream, startPromise, writableHighWaterMark, writableSizeAlgorithm, readableHighWaterMark, readableSizeAlgorithm);
      setUpTransformStreamDefaultControllerFromTransformer(stream, transformer, transformerDict);
      if (transformerDict.start !== undefined) resolvePromise(startPromise, apply(transformerDict.start, transformer, [stream.controller.object]));
      else resolvePromise(startPromise, undefined);
    }

    get readable() { return thisRecord(this, 'TransformStream').readable.object; }

    get writable() { return thisRecord(this, 'TransformStream').writable.object; }
  }

  defineInterface(TransformStream, 'TransformStream');

  function initializeTransformStream(stream, startPromise, writableHighWaterMark, writableSizeAlgorithm, readableHighWaterMark, readableSizeAlgorithm) {
    const startAlgorithm = () => startPromise.promise;
    const writeAlgorithm = (chunk) => transformStreamDefaultSinkWriteAlgorithm(stream, chunk);
    const abortAlgorithm = (reason) => transformStreamDefaultSinkAbortAlgorithm(stream, reason);
    const closeAlgorithm = () => transformStreamDefaultSinkCloseAlgorithm(stream);
    stream.writable = createWritableStream(stream.realm, startAlgorithm, writeAlgorithm, closeAlgorithm, abortAlgorithm, writableHighWaterMark, writableSizeAlgorithm);
    const pullAlgorithm = () => transformStreamDefaultSourcePullAlgorithm(stream);
    const cancelAlgorithm = (reason) => transformStreamDefaultSourceCancelAlgorithm(stream, reason);
    stream.readable = createReadableStream(stream.realm, startAlgorithm, pullAlgorithm, cancelAlgorithm, readableHighWaterMark, readableSizeAlgorithm);
    stream.backpressure = undefined;
    stream.backpressureChangePromise = undefined;
    transformStreamSetBackpressure(stream, true);
    stream.controller = undefined;
  }

  function transformStreamError(stream, e) {
    readableStreamDefaultControllerError(stream.readable.controller, e);
    transformStreamErrorWritableAndUnblockWrite(stream, e);
  }

  function transformStreamErrorWritableAndUnblockWrite(stream, e) {
    transformStreamDefaultControllerClearAlgorithms(stream.controller);
    writableStreamDefaultControllerErrorIfNeeded(stream.writable.controller, e);
    transformStreamUnblockWrite(stream);
  }

  function transformStreamUnblockWrite(stream) {
    if (stream.backpressure) transformStreamSetBackpressure(stream, false);
  }

  function transformStreamSetBackpressure(stream, backpressure) {
    if (stream.backpressureChangePromise !== undefined) resolvePromise(stream.backpressureChangePromise, undefined);
    stream.backpressureChangePromise = newPromise();
    stream.backpressure = backpressure;
  }

  class TransformStreamDefaultController {
    constructor() {
      throw new TypeError_('Illegal constructor');
    }

    get desiredSize() {
      const controller = thisRecord(this, 'TransformStreamDefaultController');
      return readableStreamDefaultControllerGetDesiredSize(controller.stream.readable.controller);
    }

    enqueue(chunk = undefined) {
      transformStreamDefaultControllerEnqueue(thisRecord(this, 'TransformStreamDefaultController'), chunk);
    }

    error(reason = undefined) {
      transformStreamDefaultControllerError(thisRecord(this, 'TransformStreamDefaultController'), reason);
    }

    terminate() {
      transformStreamDefaultControllerTerminate(thisRecord(this, 'TransformStreamDefaultController'));
    }
  }

  defineInterface(TransformStreamDefaultController, 'TransformStreamDefaultController');

  function setUpTransformStreamDefaultController(stream, controller, transformAlgorithm, flushAlgorithm, cancelAlgorithm) {
    controller.stream = stream;
    stream.controller = controller;
    controller.transformAlgorithm = transformAlgorithm;
    controller.flushAlgorithm = flushAlgorithm;
    controller.cancelAlgorithm = cancelAlgorithm;
    controller.finishPromise = undefined;
  }

  function setUpTransformStreamDefaultControllerFromTransformer(stream, transformer, transformerDict) {
    const controller = createObject(stream.realm, 'TransformStreamDefaultController');
    let transformAlgorithm = (chunk) => {
      try {
        transformStreamDefaultControllerEnqueue(controller, chunk);
        return promiseResolvedWith(undefined);
      } catch (error) {
        return promiseRejectedWith(error);
      }
    };
    let flushAlgorithm = () => promiseResolvedWith(undefined);
    let cancelAlgorithm = () => promiseResolvedWith(undefined);
    const { transform, flush, cancel } = transformerDict;
    if (transform !== undefined) transformAlgorithm = (chunk) => promiseCall(transform, transformer, [chunk, controller.object]);
    if (flush !== undefined) flushAlgorithm = () => promiseCall(flush, transformer, [controller.object]);
    if (cancel !== undefined) cancelAlgorithm = (reason) => promiseCall(cancel, transformer, [reason]);
    setUpTransformStreamDefaultController(stream, controller, transformAlgorithm, flushAlgorithm, cancelAlgorithm);
  }

  function transformStreamDefaultControllerClearAlgorithms(controller) {
    controller.transformAlgorithm = undefined;
    controller.flushAlgorithm = undefined;
    controller.cancelAlgorithm = undefined;
  }

  function transformStreamDefaultControllerEnqueue(controller, chunk) {
    const stream = controller.stream;
    const readableController = stream.readable.controller;
    if (!readableStreamDefaultControllerCanCloseOrEnqueue(readableController)) throw new TypeError_('Readable side is not in a state that permits enqueue');
    try {
      readableStreamDefaultControllerEnqueue(readableController, chunk);
    } catch (error) {
      transformStreamErrorWritableAndUnblockWrite(stream, error);
      throw stream.readable.storedError;
    }

    const backpressure = readableStreamDefaultControllerHasBackpressure(readableController);
    if (backpressure !== stream.backpressure) transformStreamSetBackpressure(stream, true);
  }

  function transformStreamDefaultControllerError(controller, e) {
    transformStreamError(controller.stream, e);
  }

  function transformStreamDefaultControllerPerformTransform(controller, chunk) {
    const transformPromise = controller.transformAlgorithm(chunk);
    return performPromiseThen(transformPromise, undefined, (r) => {
      transformStreamError(controller.stream, r);
      throw r;
    });
  }

  function transformStreamDefaultControllerTerminate(controller) {
    const stream = controller.stream;
    readableStreamDefaultControllerClose(stream.readable.controller);
    const error = new TypeError_('TransformStream terminated');
    transformStreamErrorWritableAndUnblockWrite(stream, error);
  }

  function transformStreamDefaultSinkWriteAlgorithm(stream, chunk) {
    const controller = stream.controller;
    if (stream.backpressure) {
      const backpressureChangePromise = stream.backpressureChangePromise;
      return performPromiseThen(backpressureChangePromise.promise, () => {
        const writable = stream.writable;
        if (writable.state === 'erroring') throw writable.storedError;
        return transformStreamDefaultControllerPerformTransform(controller, chunk);
      });
    }

    return transformStreamDefaultControllerPerformTransform(controller, chunk);
  }

  function transformStreamDefaultSinkAbortAlgorithm(stream, reason) {
    const controller = stream.controller;
    if (controller.finishPromise !== undefined) return controller.finishPromise.promise;
    const readable = stream.readable;
    controller.finishPromise = newPromise();
    const cancelPromise = controller.cancelAlgorithm(reason);
    transformStreamDefaultControllerClearAlgorithms(controller);
    uponPromise(cancelPromise, () => {
      if (readable.state === 'errored') {
        rejectPromise(controller.finishPromise, readable.storedError);
      } else {
        readableStreamDefaultControllerError(readable.controller, reason);
        resolvePromise(controller.finishPromise, undefined);
      }
    }, (r) => {
      readableStreamDefaultControllerError(readable.controller, r);
      rejectPromise(controller.finishPromise, r);
    });
    return controller.finishPromise.promise;
  }

  function transformStreamDefaultSinkCloseAlgorithm(stream) {
    const controller = stream.controller;
    if (controller.finishPromise !== undefined) return controller.finishPromise.promise;
    const readable = stream.readable;
    controller.finishPromise = newPromise();
    const flushPromise = controller.flushAlgorithm();
    transformStreamDefaultControllerClearAlgorithms(controller);
    uponPromise(flushPromise, () => {
      if (readable.state === 'errored') {
        rejectPromise(controller.finishPromise, readable.storedError);
      } else {
        readableStreamDefaultControllerClose(readable.controller);
        resolvePromise(controller.finishPromise, undefined);
      }
    }, (r) => {
      readableStreamDefaultControllerError(readable.controller, r);
      rejectPromise(controller.finishPromise, r);
    });
    return controller.finishPromise.promise;
  }

  function transformStreamDefaultSourceCancelAlgorithm(stream, reason) {
    const controller = stream.controller;
    if (controller.finishPromise !== undefined) return controller.finishPromise.promise;
    const writable = stream.writable;
    controller.finishPromise = newPromise();
    const cancelPromise = controller.cancelAlgorithm(reason);
    transformStreamDefaultControllerClearAlgorithms(controller);
    uponPromise(cancelPromise, () => {
      if (writable.state === 'errored') {
        rejectPromise(controller.finishPromise, writable.storedError);
      } else {
        writableStreamDefaultControllerErrorIfNeeded(writable.controller, reason);
        transformStreamUnblockWrite(stream);
        resolvePromise(controller.finishPromise, undefined);
      }
    }, (r) => {
      writableStreamDefaultControllerErrorIfNeeded(writable.controller, r);
      transformStreamUnblockWrite(stream);
      rejectPromise(controller.finishPromise, r);
    });
    return controller.finishPromise.promise;
  }

  function transformStreamDefaultSourcePullAlgorithm(stream) {
    transformStreamSetBackpressure(stream, false);
    return stream.backpressureChangePromise.promise;
  }

  // Creates a TransformStream with the algorithms of another specification (https://streams.spec.whatwg.org/#transformstream-set-up)
  function setUpTransformStream(stream, transformAlgorithm, flushAlgorithm) {
    const startPromise = resolvedDeferred(undefined);
    initializeTransformStream(stream, startPromise, 1, () => 1, 0, () => 1);
    const controller = createObject(stream.realm, 'TransformStreamDefaultController');
    const transformAlgorithmWrapper = (chunk) => {
      try {
        const result = transformAlgorithm(chunk);
        return isObject(result) ? result : promiseResolvedWith(undefined);
      } catch (error) {
        return promiseRejectedWith(error);
      }
    };
    const flushAlgorithmWrapper = () => {
      try {
        flushAlgorithm();
        return promiseResolvedWith(undefined);
      } catch (error) {
        return promiseRejectedWith(error);
      }
    };
    setUpTransformStreamDefaultController(stream, controller, transformAlgorithmWrapper, flushAlgorithmWrapper, () => promiseResolvedWith(undefined));
  }

  // ------------------------------------------------------------------------------------------------------------------
  // TextEncoderStream and TextDecoderStream (https://encoding.spec.whatwg.org/#interface-textdecoderstream)
  // ------------------------------------------------------------------------------------------------------------------

  class TextDecoderStream {
    constructor(label = undefined, options = undefined) {
      label = label === undefined ? 'utf-8' : convertDOMString(label);
      convertDictionary(options, 'options');
      const fatal = convertBoolean(getMember(options, 'fatal'));
      const ignoreBOM = convertBoolean(getMember(options, 'ignoreBOM'));
      const decoder = new TextDecoder_(label, { fatal, ignoreBOM });
      const record = createRecord(this, 'TextDecoderStream');
      record.decoder = decoder;
      const transform = createObject(record.realm, 'TransformStream');
      setUpTransformStream(transform, (chunk) => {
        // https://encoding.spec.whatwg.org/#decode-and-enqueue-a-chunk
        if (getViewKind(chunk) === undefined) {
          try {
            arrayBufferByteLengthOf(chunk);
          } catch {
            if (!isSharedArrayBuffer(chunk)) throw new TypeError_('The chunk is not an ArrayBuffer or an ArrayBufferView');
          }
        }

        const output = apply(textDecoderDecode, decoder, [chunk, { stream: true }]);
        if (output !== '') transformStreamDefaultControllerEnqueue(transform.controller, output);
      }, () => {
        // https://encoding.spec.whatwg.org/#flush-and-enqueue
        const output = apply(textDecoderDecode, decoder, []);
        if (output !== '') transformStreamDefaultControllerEnqueue(transform.controller, output);
      });
      record.transform = transform;
    }

    get encoding() { return apply(textDecoderEncoding, thisRecord(this, 'TextDecoderStream').decoder, []); }

    get fatal() { return apply(textDecoderFatal, thisRecord(this, 'TextDecoderStream').decoder, []); }

    get ignoreBOM() { return apply(textDecoderIgnoreBOM, thisRecord(this, 'TextDecoderStream').decoder, []); }

    get readable() { return thisRecord(this, 'TextDecoderStream').transform.readable.object; }

    get writable() { return thisRecord(this, 'TextDecoderStream').transform.writable.object; }
  }

  defineInterface(TextDecoderStream, 'TextDecoderStream');

  class TextEncoderStream {
    constructor() {
      const record = createRecord(this, 'TextEncoderStream');
      record.leadingSurrogate = null;
      const transform = createObject(record.realm, 'TransformStream');
      setUpTransformStream(transform, (chunk) => {
        // https://encoding.spec.whatwg.org/#encode-and-enqueue-a-chunk
        const input = convertDOMString(chunk);
        const bytes = [];
        for (let i = 0; i < input.length; i++) {
          let codePoint = input.charCodeAt(i);
          if (record.leadingSurrogate !== null) {
            const leading = record.leadingSurrogate;
            record.leadingSurrogate = null;
            if (codePoint >= 0xDC00 && codePoint <= 0xDFFF) {
              codePoint = 0x10000 + ((leading - 0xD800) << 10) + (codePoint - 0xDC00);
              appendUtf8(bytes, codePoint);
              continue;
            }

            appendUtf8(bytes, 0xFFFD);
          }

          if (codePoint >= 0xD800 && codePoint <= 0xDBFF) {
            record.leadingSurrogate = codePoint;
            continue;
          }

          if (codePoint >= 0xDC00 && codePoint <= 0xDFFF) codePoint = 0xFFFD;
          appendUtf8(bytes, codePoint);
        }

        if (bytes.length > 0) transformStreamDefaultControllerEnqueue(transform.controller, new Uint8Array_(bytes));
      }, () => {
        // https://encoding.spec.whatwg.org/#encode-and-flush
        if (record.leadingSurrogate !== null) transformStreamDefaultControllerEnqueue(transform.controller, new Uint8Array_([0xEF, 0xBF, 0xBD]));
      });
      record.transform = transform;
    }

    get encoding() {
      thisRecord(this, 'TextEncoderStream');
      return 'utf-8';
    }

    get readable() { return thisRecord(this, 'TextEncoderStream').transform.readable.object; }

    get writable() { return thisRecord(this, 'TextEncoderStream').transform.writable.object; }
  }

  defineInterface(TextEncoderStream, 'TextEncoderStream');

  function appendUtf8(bytes, codePoint) {
    if (codePoint < 0x80) {
      bytes[bytes.length] = codePoint;
    } else if (codePoint < 0x800) {
      bytes[bytes.length] = 0xC0 | (codePoint >> 6);
      bytes[bytes.length] = 0x80 | (codePoint & 0x3F);
    } else if (codePoint < 0x10000) {
      bytes[bytes.length] = 0xE0 | (codePoint >> 12);
      bytes[bytes.length] = 0x80 | ((codePoint >> 6) & 0x3F);
      bytes[bytes.length] = 0x80 | (codePoint & 0x3F);
    } else {
      bytes[bytes.length] = 0xF0 | (codePoint >> 18);
      bytes[bytes.length] = 0x80 | ((codePoint >> 12) & 0x3F);
      bytes[bytes.length] = 0x80 | ((codePoint >> 6) & 0x3F);
      bytes[bytes.length] = 0x80 | (codePoint & 0x3F);
    }
  }

  // ------------------------------------------------------------------------------------------------------------------
  // CompressionStream and DecompressionStream (https://compression.spec.whatwg.org/): the compression contexts are
  // implemented in .NET (hooks.createCompressionContext)
  // ------------------------------------------------------------------------------------------------------------------

  // CompressionFormat (https://compression.spec.whatwg.org/#enumdef-compressionformat)
  function convertCompressionFormat(value, interfaceName) {
    const format = convertDOMString(value);
    if (format !== 'brotli' && format !== 'deflate' && format !== 'deflate-raw' && format !== 'gzip') {
      throw new TypeError_(`Failed to construct '${interfaceName}': '${format}' is not a valid value for enumeration CompressionFormat`);
    }

    return format;
  }

  // The transform of a CompressionStream or a DecompressionStream: https://compression.spec.whatwg.org/#compress-and-enqueue-a-chunk,
  // https://compression.spec.whatwg.org/#compress-flush-and-enqueue, https://compression.spec.whatwg.org/#decompress-and-enqueue-a-chunk
  // and https://compression.spec.whatwg.org/#decompress-flush-and-enqueue
  function createCompressionTransform(objectRealm, format, decompress) {
    const context = hooks.createCompressionContext(format, decompress);
    const transform = createObject(objectRealm, 'TransformStream');
    const enqueue = (arrays) => {
      for (let i = 0; i < arrays.length; i++) transformStreamDefaultControllerEnqueue(transform.controller, arrays[i]);
    };
    setUpTransformStream(transform, (chunk) => {
      enqueue(context.transform(chunk));
      if (context.hasTrailingInput()) throw new TypeError_('The compressed data is followed by additional data');
    }, () => {
      enqueue(context.flush());
    });
    return transform;
  }

  class CompressionStream {
    constructor(format) {
      if (arguments.length < 1) throw new TypeError_("Failed to construct 'CompressionStream': 1 argument required");
      format = convertCompressionFormat(format, 'CompressionStream');
      const record = createRecord(this, 'CompressionStream');
      record.transform = createCompressionTransform(record.realm, format, false);
    }

    get readable() { return thisRecord(this, 'CompressionStream').transform.readable.object; }

    get writable() { return thisRecord(this, 'CompressionStream').transform.writable.object; }
  }

  defineInterface(CompressionStream, 'CompressionStream');

  class DecompressionStream {
    constructor(format) {
      if (arguments.length < 1) throw new TypeError_("Failed to construct 'DecompressionStream': 1 argument required");
      format = convertCompressionFormat(format, 'DecompressionStream');
      const record = createRecord(this, 'DecompressionStream');
      record.transform = createCompressionTransform(record.realm, format, true);
    }

    get readable() { return thisRecord(this, 'DecompressionStream').transform.readable.object; }

    get writable() { return thisRecord(this, 'DecompressionStream').transform.writable.object; }
  }

  defineInterface(DecompressionStream, 'DecompressionStream');

  // ------------------------------------------------------------------------------------------------------------------
  // Blob (https://w3c.github.io/FileAPI/#blob-get-stream)
  // ------------------------------------------------------------------------------------------------------------------

  // The stream of the bytes of a blob, read in chunks of 64 KiB, with byte reading support
  function createBlobStream(bytes) {
    const chunkSize = 65536;
    let position = 0;
    let stream;
    const pullAlgorithm = () => {
      const controller = stream.controller;
      if (position >= bytes.length) {
        readableByteStreamControllerClose(controller);
        const byobRequest = readableByteStreamControllerGetBYOBRequest(controller);
        if (byobRequest !== null) readableByteStreamControllerRespond(controller, 0);
        return promiseResolvedWith(undefined);
      }

      const end = MathMin(position + chunkSize, bytes.length);
      const chunk = new Uint8Array_(end - position);
      apply(typedArraySet, chunk, [new Uint8Array_(getViewBuffer(bytes), getViewByteOffset(bytes) + position, end - position)]);
      position = end;
      readableByteStreamControllerEnqueue(controller, chunk);
      return promiseResolvedWith(undefined);
    };
    stream = createReadableByteStream(realm, () => undefined, pullAlgorithm, () => promiseResolvedWith(undefined));
    return stream.object;
  }

  // https://whatpr.org/html/12758/dynamic-markup-insertion.html#stream-html: the WritableStream of the HTML streaming methods,
  // whose sink is the HTML stream writer of the library (its write(string), close() and abort() functions throw the errors
  // of the parser, e.g. a HierarchyRequestError when the reference child was moved)
  function createHtmlWritableStream(sink) {
    const writeAlgorithm = (chunk) => {
      let string;
      try {
        string = `${chunk}`;
      } catch (e) {
        sink.abort();
        return promiseRejectedWith(e);
      }

      try {
        sink.write(string);
      } catch (e) {
        return promiseRejectedWith(e);
      }

      return promiseResolvedWith(undefined);
    };
    const closeAlgorithm = () => {
      try {
        sink.close();
      } catch (e) {
        return promiseRejectedWith(e);
      }

      return promiseResolvedWith(undefined);
    };
    const abortAlgorithm = () => {
      sink.abort();
      return promiseResolvedWith(undefined);
    };
    return createWritableStream(realm, () => undefined, writeAlgorithm, closeAlgorithm, abortAlgorithm).object;
  }

  // ------------------------------------------------------------------------------------------------------------------
  // The File System Standard: FileSystemWritableFileStream (https://fs.spec.whatwg.org/#api-filesystemwritablefilestream)
  // ------------------------------------------------------------------------------------------------------------------

  // The record of a FileSystemWritableFileStream is a WritableStream record with the sink of the library: its
  // write(type, data, position, size), close() and abort() functions, which throw the errors of the file system
  function thisFileSystemWritableFileStream(object) {
    const stream = getRecord(object, 'WritableStream');
    if (stream === undefined || stream.fileSystemSink === undefined) throw new TypeError_("Illegal invocation: 'this' does not implement interface FileSystemWritableFileStream");
    return stream;
  }

  // unsigned long long (https://webidl.spec.whatwg.org/#es-unsigned-long-long), limited to the safe integers
  function convertUnsignedLongLong(value) {
    let x = +value;
    if (x !== x || x === Infinity || x === -Infinity) return 0;
    x = MathTrunc(x) % 18446744073709551616;
    if (x < 0) x += 18446744073709551616;
    return x === 0 ? 0 : x;
  }

  const isBufferSource = (value) => getViewKind(value) !== undefined || isArrayBufferObject(value) || isSharedArrayBuffer(value);

  function isArrayBufferObject(value) {
    try {
      apply(arrayBufferByteLength, value, []);
      return true;
    } catch {
      return false;
    }
  }

  // (BufferSource or Blob or USVString): the objects that are not buffer sources nor blobs are converted to strings
  function convertFileSystemWriteData(sink, value) {
    if (isObject(value) && (isBufferSource(value) || sink.isBlob(value))) return value;
    return `${value}`;
  }

  // https://fs.spec.whatwg.org/#write-a-chunk: converts the chunk to a FileSystemWriteChunkType (a BufferSource, a Blob, a
  // USVString or a WriteParams dictionary), then runs the command in the library (which rejects a command without its
  // member: data is undefined when the member is missing, position and size are null)
  function fileSystemWriteChunk(sink, chunk) {
    let type = 'write';
    let data;
    let position = null;
    let size = null;
    if (chunk === undefined || chunk === null || (isObject(chunk) && !isBufferSource(chunk) && !sink.isBlob(chunk))) {
      // WriteParams: the members are converted in lexicographical order
      const dataValue = getMember(chunk, 'data');
      data = dataValue === undefined || dataValue === null ? dataValue : convertFileSystemWriteData(sink, dataValue);
      const positionValue = getMember(chunk, 'position');
      position = positionValue === undefined || positionValue === null ? null : convertUnsignedLongLong(positionValue);
      const sizeValue = getMember(chunk, 'size');
      size = sizeValue === undefined || sizeValue === null ? null : convertUnsignedLongLong(sizeValue);
      const typeValue = getMember(chunk, 'type');
      if (typeValue === undefined) throw new TypeError_("The 'type' member of WriteParams is required");
      type = `${typeValue}`;
      if (type !== 'write' && type !== 'seek' && type !== 'truncate') throw new TypeError_(`'${type}' is not a valid value of WriteCommandType`);
    } else {
      data = convertFileSystemWriteData(sink, chunk);
    }

    return sink.write(type, data, position, size);
  }

  // "Let writer be the result of getting a writer for this; let result be the result of writing a chunk to writer; release writer"
  function fileSystemWritableFileStreamWrite(stream, chunk) {
    const writer = acquireWritableStreamDefaultWriter(stream);
    const result = writableStreamDefaultWriterWrite(writer, chunk);
    writableStreamDefaultWriterRelease(writer);
    return result;
  }

  class FileSystemWritableFileStream extends WritableStream {
    constructor() {
      throw new TypeError_('Illegal constructor');
    }

    // The mode attribute of the locking proposal that Chromium implements (not in the File System Standard yet)
    get mode() { return thisFileSystemWritableFileStream(this).fileSystemSink.mode; }

    write(data) {
      const argumentCount = arguments.length;
      return promiseOperation(() => {
        const stream = thisFileSystemWritableFileStream(this);
        if (argumentCount < 1) throw new TypeError_("Not enough arguments to 'write': 1 argument required, but only 0 present");
        return fileSystemWritableFileStreamWrite(stream, data);
      });
    }

    seek(position) {
      const argumentCount = arguments.length;
      return promiseOperation(() => {
        const stream = thisFileSystemWritableFileStream(this);
        if (argumentCount < 1) throw new TypeError_("Not enough arguments to 'seek': 1 argument required, but only 0 present");
        return fileSystemWritableFileStreamWrite(stream, { type: 'seek', position: convertUnsignedLongLong(position) });
      });
    }

    truncate(size) {
      const argumentCount = arguments.length;
      return promiseOperation(() => {
        const stream = thisFileSystemWritableFileStream(this);
        if (argumentCount < 1) throw new TypeError_("Not enough arguments to 'truncate': 1 argument required, but only 0 present");
        return fileSystemWritableFileStreamWrite(stream, { type: 'truncate', size: convertUnsignedLongLong(size) });
      });
    }
  }

  defineInterface(FileSystemWritableFileStream, 'FileSystemWritableFileStream', { constructor: 0 });

  // https://fs.spec.whatwg.org/#create-a-new-filesystemwritablefilestream. As in the browsers, the lock of the file is also
  // released when a write fails: the stream is then errored, and neither its close nor its abort algorithm runs.
  function createFileSystemWritableFileStream(sink) {
    const stream = createRecord(ObjectCreate(FileSystemWritableFileStream.prototype), 'WritableStream', realm);
    initializeWritableStream(stream);
    stream.fileSystemSink = sink;
    const writeAlgorithm = (chunk) => {
      try {
        return performPromiseThen(promiseResolvedWith(fileSystemWriteChunk(sink, chunk)), undefined, (error) => {
          sink.abort();
          throw error;
        });
      } catch (error) {
        sink.abort();
        return promiseRejectedWith(error);
      }
    };
    const closeAlgorithm = () => {
      try {
        return promiseResolvedWith(sink.close());
      } catch (error) {
        return promiseRejectedWith(error);
      }
    };
    const abortAlgorithm = () => {
      sink.abort();
      return promiseResolvedWith(undefined);
    };
    const controller = createObject(realm, 'WritableStreamDefaultController');
    setUpWritableStreamDefaultController(stream, controller, () => undefined, writeAlgorithm, closeAlgorithm, abortAlgorithm, 1, () => 1);
    return stream.object;
  }

  // https://w3c.github.io/FileAPI/#dom-blob-textstream
  function createBlobTextStream(bytes) {
    const stream = getRecord(createBlobStream(bytes), 'ReadableStream');
    const decoder = new TextDecoderStream();
    const transform = getRecord(decoder, 'TextDecoderStream').transform;
    const promise = readableStreamPipeTo(stream, transform.writable, false, false, false, undefined);
    setPromiseIsHandledToTrue(promise);
    return transform.readable.object;
  }

  // ------------------------------------------------------------------------------------------------------------------
  // The Fetch Standard (Fetch.js): the bodies of the requests and of the responses
  // ------------------------------------------------------------------------------------------------------------------

  // https://streams.spec.whatwg.org/#readablestream-read-all-bytes: the chunks must be Uint8Array objects. The steps are
  // callbacks (a promise resolved with the bytes would look up their 'then' property)
  function readAllBytes(stream, successSteps, failureSteps) {
    const reader = acquireReadableStreamDefaultReader(stream);
    const chunks = [];
    let length = 0;
    const readLoop = () => {
      readableStreamDefaultReaderRead(reader, {
        chunk(chunk) {
          if (getViewKind(chunk) !== 'Uint8Array') {
            const error = new TypeError_('The chunk is not a Uint8Array');
            failureSteps(error);
            readableStreamReaderGenericCancel(reader, error);
            return;
          }

          chunks[chunks.length] = cloneAsUint8Array(chunk);
          length += apply(typedArrayByteLength, chunk, []);
          readLoop();
        },
        close() {
          const bytes = new Uint8Array_(length);
          let offset = 0;
          for (const chunk of chunks) {
            apply(typedArraySet, bytes, [chunk, offset]);
            offset += apply(typedArrayByteLength, chunk, []);
          }

          successSteps(bytes);
        },
        error(e) { failureSteps(e); },
      });
    };
    readLoop();
  }

  // https://streams.spec.whatwg.org/#readablestream-error
  function errorReadableStream(stream, e) {
    if (stream.state !== 'readable') return;
    if (stream.controller.kind === 'ReadableByteStreamController') {
      readableByteStreamControllerError(stream.controller, e);
    } else {
      readableStreamDefaultControllerError(stream.controller, e);
    }
  }

  // The functions used by the bodies of the Fetch API, on the records of the streams
  const fetchSupport = ObjectCreate(null);
  fetchSupport.getStream = (value) => getRecord(value, 'ReadableStream');
  fetchSupport.createByteStream = (bytes) => getRecord(createBlobStream(bytes), 'ReadableStream');
  fetchSupport.isLocked = isReadableStreamLocked;
  fetchSupport.isDisturbed = (stream) => stream.disturbed;
  fetchSupport.isReadable = (stream) => stream.state === 'readable';
  fetchSupport.isErrored = (stream) => stream.state === 'errored';
  fetchSupport.storedError = (stream) => stream.storedError;
  fetchSupport.cancel = (stream, reason) => { setPromiseIsHandledToTrue(readableStreamCancel(stream, reason)); };
  fetchSupport.tee = (stream) => readableStreamTee(stream, true);
  fetchSupport.readAllBytes = readAllBytes;
  fetchSupport.error = errorReadableStream;
  fetchSupport.lock = (stream) => { acquireReadableStreamDefaultReader(stream); stream.disturbed = true; };

  // https://fetch.spec.whatwg.org/#dom-body-textstream: the stream piped through a TextDecoderStream
  fetchSupport.textStream = (stream) => {
    const decoder = new TextDecoderStream();
    const transform = getRecord(decoder, 'TextDecoderStream').transform;
    setPromiseIsHandledToTrue(readableStreamPipeTo(stream, transform.writable, false, false, false, undefined));
    return transform.readable.object;
  };
  fetchSupport.emptyStream = () => getRecord(createBlobStream(new Uint8Array_(0)), 'ReadableStream');

  // https://fetch.spec.whatwg.org/#concept-body-clone uses the identity transform to create a proxy
  fetchSupport.createProxy = (stream) => {
    const transform = new TransformStream();
    const transformRecord = getRecord(transform, 'TransformStream');
    const promise = readableStreamPipeTo(stream, transformRecord.writable, false, false, false, undefined);
    setPromiseIsHandledToTrue(promise);
    return transformRecord.readable;
  };

  const realm = {
    ReadableStream,
    ReadableStreamDefaultReader,
    ReadableStreamBYOBReader,
    ReadableStreamDefaultController,
    ReadableByteStreamController,
    ReadableStreamBYOBRequest,
    WritableStream,
    WritableStreamDefaultWriter,
    WritableStreamDefaultController,
    TransformStream,
    TransformStreamDefaultController,
    ReadableStreamAsyncIteratorPrototype,
    AbortController: AbortController_,
  };

  return {
    ReadableStream,
    ReadableStreamDefaultReader,
    ReadableStreamBYOBReader,
    ReadableStreamDefaultController,
    ReadableByteStreamController,
    ReadableStreamBYOBRequest,
    WritableStream,
    WritableStreamDefaultWriter,
    WritableStreamDefaultController,
    TransformStream,
    TransformStreamDefaultController,
    ByteLengthQueuingStrategy,
    CountQueuingStrategy,
    TextEncoderStream,
    TextDecoderStream,
    CompressionStream,
    DecompressionStream,
    createBlobStream,
    createBlobTextStream,
    createHtmlWritableStream,
    FileSystemWritableFileStream,
    createFileSystemWritableFileStream,
    fetchSupport,
  };
});
