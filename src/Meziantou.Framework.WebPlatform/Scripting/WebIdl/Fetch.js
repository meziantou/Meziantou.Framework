// The Request and Response interfaces and fetch() of the Fetch Standard (https://fetch.spec.whatwg.org/#fetch-api),
// implemented in JavaScript because their bodies are streams (Streams.js). The Headers interface and the fetch algorithm
// (https://fetch.spec.whatwg.org/#fetching: the CORS protocol, the redirects, the network requests) are implemented in
// .NET and given by the hooks of the host.
//
// The script is evaluated once per realm, when one of its interfaces is first used. It is a function called with the
// hooks of the host, and returns the interface objects and fetch(). The internal slots of the objects are stored in
// records (objects without prototype) associated with the objects by a WeakMap shared by the realms of the agent.
(function (hooks) {
  'use strict';

  // ------------------------------------------------------------------------------------------------------------------
  // Intrinsics
  // ------------------------------------------------------------------------------------------------------------------

  const apply = Reflect.apply;
  const ObjectCreate = Object.create;
  const defineProperty = Object.defineProperty;
  const getOwnPropertyDescriptor = Object.getOwnPropertyDescriptor;
  const ownKeys = Reflect.ownKeys;
  const getter = (o, k) => getOwnPropertyDescriptor(o, k).get;
  const TypeError_ = TypeError;
  const RangeError_ = RangeError;
  const Promise_ = Promise;
  const promiseReject = Promise.reject;
  const weakMapGet = WeakMap.prototype.get;
  const weakMapSet = WeakMap.prototype.set;
  const SymbolToStringTag = Symbol.toStringTag;
  const ArrayBuffer_ = ArrayBuffer;
  const arrayBufferSlice = ArrayBuffer.prototype.slice;
  const Uint8Array_ = Uint8Array;
  const typedArrayPrototype = Object.getPrototypeOf(Uint8Array.prototype);
  const typedArrayName = getter(typedArrayPrototype, SymbolToStringTag);
  const typedArrayBuffer = getter(typedArrayPrototype, 'buffer');
  const typedArrayByteOffset = getter(typedArrayPrototype, 'byteOffset');
  const typedArrayByteLength = getter(typedArrayPrototype, 'byteLength');
  const typedArraySet = typedArrayPrototype.set;
  const dataViewBuffer = getter(DataView.prototype, 'buffer');
  const dataViewByteOffset = getter(DataView.prototype, 'byteOffset');
  const dataViewByteLength = getter(DataView.prototype, 'byteLength');
  const arrayBufferByteLength = getter(ArrayBuffer.prototype, 'byteLength');
  // %SharedArrayBuffer% (the global object has no SharedArrayBuffer property when the agent cluster is not cross-origin isolated)
  const sharedArrayBufferByteLength = typeof hooks.SharedArrayBuffer === 'function' ? getter(hooks.SharedArrayBuffer.prototype, 'byteLength') : null;
  const arrayBufferResizable = getter(ArrayBuffer.prototype, 'resizable');
  const JSONParse = JSON.parse;
  const JSONStringify = JSON.stringify;
  const StringFromCharCode = String.fromCharCode;
  const RegExpTest = RegExp.prototype.test;

  const { Headers: Headers_, AbortSignal: AbortSignal_, Blob: Blob_, FormData: FormData_, URLSearchParams: URLSearchParams_, TextDecoder: TextDecoder_, DOMException: DOMException_ } = hooks.interfaces;
  const headersAppend = Headers_.prototype.append;
  const abortSignalAny = AbortSignal_.any;
  const abortSignalAborted = getter(AbortSignal_.prototype, 'aborted');
  const abortSignalReason = getter(AbortSignal_.prototype, 'reason');
  const blobType = getter(Blob_.prototype, 'type');
  const urlSearchParamsToString = URLSearchParams_.prototype.toString;
  const textDecoderDecode = TextDecoder_.prototype.decode;
  const streams = hooks.streams;

  const internals = hooks.internals;
  const isObject = (value) => (typeof value === 'object' && value !== null) || typeof value === 'function';

  function createRecord(object, kind) {
    const record = ObjectCreate(null);
    record.kind = kind;
    record.object = object;
    record.realm = realm;
    apply(weakMapSet, internals, [object, record]);
    return record;
  }

  function getRecord(object, kind) {
    if (!isObject(object)) return undefined;
    const record = apply(weakMapGet, internals, [object]);
    return record !== undefined && record.kind === kind ? record : undefined;
  }

  function thisRecord(object, kind) {
    const record = getRecord(object, kind);
    if (record === undefined) throw new TypeError_(`Illegal invocation: 'this' does not implement interface ${kind}`);
    return record;
  }

  const promiseResolvedWith = (value) => new Promise_((resolve) => resolve(value));
  const promiseRejectedWith = (reason) => apply(promiseReject, Promise_, [reason]);

  function newPromise() {
    const deferred = ObjectCreate(null);
    deferred.promise = new Promise_((resolve, reject) => {
      deferred.resolve = resolve;
      deferred.reject = reject;
    });
    return deferred;
  }

  // Makes the members of a class follow WebIDL: the operations and the attributes are enumerable, and the class has a @@toStringTag
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
        const target = key === 'constructor' ? constructor : key[0] === '@' ? constructor[key.substring(1)] : prototype[key];
        defineProperty(target, 'length', { value: lengths[key], writable: false, enumerable: false, configurable: true });
      }
    }

    defineProperty(prototype, SymbolToStringTag, { value: name, writable: false, enumerable: false, configurable: true });
  }

  // ------------------------------------------------------------------------------------------------------------------
  // WebIDL conversions
  // ------------------------------------------------------------------------------------------------------------------

  const convertDOMString = (value) => `${value}`;

  function convertUSVString(value) {
    const string = `${value}`;
    let result = '';
    for (let i = 0; i < string.length; i++) {
      const c = string.charCodeAt(i);
      if (c >= 0xD800 && c <= 0xDBFF && i + 1 < string.length) {
        const d = string.charCodeAt(i + 1);
        if (d >= 0xDC00 && d <= 0xDFFF) {
          result += string[i] + string[i + 1];
          i++;
          continue;
        }
      }

      result += c >= 0xD800 && c <= 0xDFFF ? StringFromCharCode(0xFFFD) : string[i];
    }

    return result;
  }

  function convertByteString(value, context) {
    const string = `${value}`;
    for (let i = 0; i < string.length; i++) {
      if (string.charCodeAt(i) > 255) throw new TypeError_(`${context} is not a valid ByteString`);
    }

    return string;
  }

  function convertEnum(value, values, context) {
    const string = `${value}`;
    for (const item of values) {
      if (item === string) return string;
    }

    throw new TypeError_(`The provided value '${string}' is not a valid enum value of type ${context}`);
  }

  function convertUnsignedShort(value) {
    let x = +value;
    if (x !== x || x === Infinity || x === -Infinity) return 0;
    x = x < 0 ? -Math.floor(-x) : Math.floor(x);
    x = x % 65536;
    return x < 0 ? x + 65536 : x;
  }

  function convertDictionary(value, context) {
    if (value === undefined || value === null) return undefined;
    if (!isObject(value)) throw new TypeError_(`${context} is not an object`);
    return value;
  }

  const requestCacheValues = ['default', 'no-store', 'reload', 'no-cache', 'force-cache', 'only-if-cached'];
  const requestCredentialsValues = ['omit', 'same-origin', 'include'];
  const requestModeValues = ['navigate', 'same-origin', 'no-cors', 'cors'];
  const requestRedirectValues = ['follow', 'error', 'manual'];
  const requestPriorityValues = ['high', 'low', 'auto'];
  const requestDuplexValues = ['half'];
  const referrerPolicyValues = ['', 'no-referrer', 'no-referrer-when-downgrade', 'same-origin', 'origin', 'strict-origin', 'origin-when-cross-origin', 'strict-origin-when-cross-origin', 'unsafe-url'];

  // https://fetch.spec.whatwg.org/#requestinit: the members are read in lexicographical order
  function convertRequestInit(value) {
    const dictionary = convertDictionary(value, 'RequestInit');
    const init = ObjectCreate(null);
    if (dictionary === undefined) return init;
    let member = dictionary.body;
    if (member !== undefined) init.body = member;
    member = dictionary.cache;
    if (member !== undefined) init.cache = convertEnum(member, requestCacheValues, 'RequestCache');
    member = dictionary.credentials;
    if (member !== undefined) init.credentials = convertEnum(member, requestCredentialsValues, 'RequestCredentials');
    member = dictionary.duplex;
    if (member !== undefined) init.duplex = convertEnum(member, requestDuplexValues, 'RequestDuplex');
    member = dictionary.headers;
    if (member !== undefined) init.headers = convertHeadersInit(member);
    member = dictionary.integrity;
    if (member !== undefined) init.integrity = convertDOMString(member);
    member = dictionary.keepalive;
    if (member !== undefined) init.keepalive = !!member;
    member = dictionary.method;
    if (member !== undefined) init.method = convertByteString(member, 'method');
    member = dictionary.mode;
    if (member !== undefined) init.mode = convertEnum(member, requestModeValues, 'RequestMode');
    member = dictionary.priority;
    if (member !== undefined) init.priority = convertEnum(member, requestPriorityValues, 'RequestPriority');
    member = dictionary.redirect;
    if (member !== undefined) init.redirect = convertEnum(member, requestRedirectValues, 'RequestRedirect');
    member = dictionary.referrer;
    if (member !== undefined) init.referrer = convertUSVString(member);
    member = dictionary.referrerPolicy;
    if (member !== undefined) init.referrerPolicy = convertEnum(member, referrerPolicyValues, 'ReferrerPolicy');
    member = dictionary.signal;
    if (member !== undefined) {
      if (member !== null && !hooks.isAbortSignal(member)) throw new TypeError_("Failed to read the 'signal' property from 'RequestInit': The provided value is not of type 'AbortSignal'");
      init.signal = member;
    }

    member = dictionary.window;
    if (member !== undefined) init.window = member;
    return init;
  }

  // https://fetch.spec.whatwg.org/#responseinit
  function convertResponseInit(value) {
    const dictionary = convertDictionary(value, 'ResponseInit');
    const init = ObjectCreate(null);
    init.status = 200;
    init.statusText = '';
    if (dictionary === undefined) return init;
    let member = dictionary.headers;
    if (member !== undefined) init.headers = convertHeadersInit(member);
    member = dictionary.status;
    if (member !== undefined) init.status = convertUnsignedShort(member);
    member = dictionary.statusText;
    if (member !== undefined) init.statusText = convertByteString(member, 'statusText');
    return init;
  }

  // HeadersInit (sequence<sequence<ByteString>> or record<ByteString, ByteString>): converted by the Headers constructor
  function convertHeadersInit(value) {
    if (!isObject(value)) throw new TypeError_('The headers are not an object');
    return new Headers_(value);
  }

  // ------------------------------------------------------------------------------------------------------------------
  // Bodies (https://fetch.spec.whatwg.org/#bodies)
  // ------------------------------------------------------------------------------------------------------------------

  // A body: its stream (the record of a ReadableStream), its source (the bytes, or null for a stream) and its length
  function createBody(stream, source, length) {
    const body = ObjectCreate(null);
    body.stream = stream;
    body.source = source;
    body.length = length;
    return body;
  }

  // A body whose source is bytes
  const bodyFromBytes = (bytes) => createBody(streams.createByteStream(bytes), bytes, bytes.length);

  // The body of a response that arrives over time: its chunks are pulled from the reader of the library (read(callback)
  // gives the next chunk, null at the end, or (undefined, true, aborted) when the body fails; cancel() stops the reading)
  const bodyFromReader = (reader) => createBody(streams.createPullByteStream((onChunk, onError) => {
    reader.read((chunk, failed, aborted) => {
      if (failed) {
        onError(aborted ? new DOMException_('The operation was aborted.', 'AbortError') : new TypeError_('Failed to fetch: the body of the response could not be read'));
      } else {
        onChunk(chunk);
      }
    });
  }, () => reader.cancel()), null, null);

  const utf8Encode = (string) => hooks.utf8Encode(string);

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

  function isArrayBuffer(value) {
    try {
      apply(arrayBufferByteLength, value, []);
      return true;
    } catch {
      return false;
    }
  }

  function isSharedArrayBuffer(value) {
    if (sharedArrayBufferByteLength === null) return false;
    try {
      apply(sharedArrayBufferByteLength, value, []);
      return true;
    } catch {
      return false;
    }
  }

  // A copy of the bytes of a BufferSource (https://webidl.spec.whatwg.org/#dfn-get-buffer-source-copy)
  function getBufferSourceCopy(value) {
    if (isArrayBuffer(value)) {
      if (apply(arrayBufferResizable, value, [])) throw new TypeError_('The buffer is resizable');
      return new Uint8Array_(apply(arrayBufferSlice, value, [0]));
    }

    const kind = getViewKind(value);
    const isDataView = kind === 'DataView';
    const buffer = apply(isDataView ? dataViewBuffer : typedArrayBuffer, value, []);
    if (isSharedArrayBuffer(buffer)) throw new TypeError_('The buffer is a SharedArrayBuffer');
    if (apply(arrayBufferResizable, buffer, [])) throw new TypeError_('The buffer is resizable');
    const offset = apply(isDataView ? dataViewByteOffset : typedArrayByteOffset, value, []);
    const length = apply(isDataView ? dataViewByteLength : typedArrayByteLength, value, []);
    return new Uint8Array_(apply(arrayBufferSlice, buffer, [offset, offset + length]));
  }

  const isBufferSource = (value) => isArrayBuffer(value) || getViewKind(value) !== undefined;

  // https://fetch.spec.whatwg.org/#concept-bodyinit-extract: returns [body, type]
  function extractBody(object, keepalive) {
    if (streams.getStream(object) !== undefined) {
      const stream = streams.getStream(object);
      if (keepalive) throw new TypeError_('A keepalive request cannot have a stream body');
      if (streams.isDisturbed(stream) || streams.isLocked(stream)) throw new TypeError_('The stream is disturbed or locked');
      return [createBody(stream, null, null), null];
    }

    if (hooks.isBlob(object)) {
      const bytes = hooks.blobBytes(object);
      const type = apply(blobType, object, []);
      return [bodyFromBytes(bytes), type !== '' ? type : null];
    }

    if (isBufferSource(object)) return [bodyFromBytes(getBufferSourceCopy(object)), null];

    if (hooks.isFormData(object)) {
      const [bytes, type] = hooks.serializeFormData(object);
      return [bodyFromBytes(bytes), type];
    }

    if (hooks.isURLSearchParams(object)) return [bodyFromBytes(utf8Encode(apply(urlSearchParamsToString, object, []))), 'application/x-www-form-urlencoded;charset=UTF-8'];

    return [bodyFromBytes(utf8Encode(convertUSVString(object))), 'text/plain;charset=UTF-8'];
  }

  // https://fetch.spec.whatwg.org/#concept-body-clone
  function cloneBody(body) {
    const [out1, out2] = streams.tee(body.stream);
    body.stream = out1;
    return createBody(out2, body.source, body.length);
  }

  // https://fetch.spec.whatwg.org/#body-unusable
  const isUnusable = (record) => record.body !== null && (streams.isDisturbed(record.body.stream) || streams.isLocked(record.body.stream));

  // https://fetch.spec.whatwg.org/#concept-body-consume-body: the bytes are processed by a task (https://fetch.spec.whatwg.org/#body-fully-read)
  function consumeBody(record, convert) {
    // A body whose stream is errored (e.g. an aborted fetch) is rejected with the error, even when it is locked
    if (record.body !== null && streams.isErrored(record.body.stream)) return record.realm.promiseRejectedWith(streams.storedError(record.body.stream));
    if (isUnusable(record)) return record.realm.promiseRejectedWith(new TypeError_('The body has already been consumed'));
    const deferred = record.realm.newPromise();
    const success = (bytes) => {
      let value;
      try {
        value = convert(bytes);
      } catch (e) {
        deferred.reject(e);
        return;
      }

      deferred.resolve(value);
    };

    if (record.body === null) {
      success(new Uint8Array_(0));
      return deferred.promise;
    }

    try {
      streams.readAllBytes(record.body.stream, (bytes) => hooks.queueTask(() => success(bytes)), (e) => hooks.queueTask(() => deferred.reject(e)));
    } catch (e) {
      deferred.reject(e);
    }

    return deferred.promise;
  }

  const toArrayBuffer = (bytes) => apply(arrayBufferSlice, apply(typedArrayBuffer, bytes, []), [apply(typedArrayByteOffset, bytes, []), apply(typedArrayByteOffset, bytes, []) + bytes.length]);

  // https://encoding.spec.whatwg.org/#utf-8-decode
  const utf8Decode = (bytes) => apply(textDecoderDecode, new TextDecoder_(), [bytes]);

  // https://fetch.spec.whatwg.org/#body-mixin: the methods are defined on the prototypes of Request and Response
  function defineBodyMixin(constructor, kind) {
    const methods = {
      get body() {
        const record = thisRecord(this, kind);
        return record.body === null ? null : record.body.stream.object;
      },
      get bodyUsed() {
        const record = thisRecord(this, kind);
        return record.body !== null && streams.isDisturbed(record.body.stream);
      },
      arrayBuffer() {
        try {
          const record = thisRecord(this, kind);
          return consumeBody(record, (bytes) => record.realm.toArrayBuffer(bytes));
        } catch (e) {
          return promiseRejectedWith(e);
        }
      },
      blob() {
        try {
          const record = thisRecord(this, kind);
          return consumeBody(record, (bytes) => record.realm.hooks.createBlob(bytes, hooks.extractMimeType(record.headers) ?? ''));
        } catch (e) {
          return promiseRejectedWith(e);
        }
      },
      bytes() {
        try {
          const record = thisRecord(this, kind);
          return consumeBody(record, (bytes) => record.realm.toUint8Array(bytes));
        } catch (e) {
          return promiseRejectedWith(e);
        }
      },
      formData() {
        try {
          const record = thisRecord(this, kind);
          return consumeBody(record, (bytes) => record.realm.hooks.parseFormData(bytes, hooks.extractMimeType(record.headers)));
        } catch (e) {
          return promiseRejectedWith(e);
        }
      },
      json() {
        try {
          const record = thisRecord(this, kind);
          return consumeBody(record, (bytes) => JSONParse(utf8Decode(bytes)));
        } catch (e) {
          return promiseRejectedWith(e);
        }
      },
      text() {
        try {
          const record = thisRecord(this, kind);
          return consumeBody(record, (bytes) => utf8Decode(bytes));
        } catch (e) {
          return promiseRejectedWith(e);
        }
      },
      textStream() {
        const record = thisRecord(this, kind);
        if (isUnusable(record)) throw new TypeError_('The body has already been consumed');
        return streams.textStream(record.body === null ? streams.emptyStream() : record.body.stream);
      },
    };

    for (const key of ownKeys(methods)) {
      const descriptor = getOwnPropertyDescriptor(methods, key);
      descriptor.enumerable = true;
      defineProperty(constructor.prototype, key, descriptor);
    }
  }

  // ------------------------------------------------------------------------------------------------------------------
  // Request (https://fetch.spec.whatwg.org/#request-class)
  // ------------------------------------------------------------------------------------------------------------------

  // https://fetch.spec.whatwg.org/#concept-request
  function createRequest(url) {
    const request = ObjectCreate(null);
    request.method = 'GET';
    request.urlList = [url];
    request.headerList = [];
    request.unsafeRequest = false;
    request.body = null;
    request.priority = 'auto';
    request.origin = 'client';
    request.referrer = 'client';
    request.referrerPolicy = '';
    request.mode = 'no-cors';
    request.useCorsPreflight = false;
    request.credentials = 'same-origin';
    request.cache = 'default';
    request.redirect = 'follow';
    request.integrity = '';
    request.keepalive = false;
    request.reloadNavigation = false;
    request.historyNavigation = false;
    request.duplex = 'half';
    request.blob = null;
    request.destination = '';

    // The request of a fetch event (a navigation): the URLs of its redirects, and the data of the navigation kept by the host
    request.redirectChain = [];
    request.navigation = null;
    return request;
  }

  const isToken = (value) => apply(RegExpTest, /^[!#$%&'*+\-.^_`|~0-9A-Za-z]+$/, [value]);
  const isForbiddenMethod = (method) => apply(RegExpTest, /^(CONNECT|TRACE|TRACK)$/i, [method]);
  const isCorsSafelistedMethod = (method) => method === 'GET' || method === 'HEAD' || method === 'POST';

  function normalizeMethod(method) {
    const upper = method.toUpperCase();
    return upper === 'DELETE' || upper === 'GET' || upper === 'HEAD' || upper === 'OPTIONS' || upper === 'POST' || upper === 'PUT' ? upper : method;
  }

  // Creates the Request object of a request (https://fetch.spec.whatwg.org/#request-create)
  function createRequestObject(objectRealm, request, headers, signal) {
    const object = ObjectCreate(objectRealm.Request.prototype);
    const record = createRecord(object, 'Request');
    record.request = request;
    record.body = request.body;
    record.headers = headers;
    record.signal = signal;
    return object;
  }

  class Request {
    constructor(input, init = undefined) {
      if (arguments.length < 1) throw new TypeError_("Failed to construct 'Request': 1 argument required, but only 0 present.");
      const inputRecord = getRecord(input, 'Request');
      if (inputRecord === undefined) input = convertUSVString(input);
      const initDict = convertRequestInit(init);
      const record = createRecord(this, 'Request');
      let request;
      let fallbackMode = null;
      const origin = hooks.origin();
      let signal = null;

      // Steps 5 and 6
      if (inputRecord === undefined) {
        const parsedUrl = hooks.parseUrl(input);
        if (parsedUrl === null) throw new TypeError_(`Failed to construct 'Request': Invalid URL '${input}'`);
        if (parsedUrl.includesCredentials) throw new TypeError_(`Failed to construct 'Request': The URL '${input}' includes credentials`);
        request = createRequest(parsedUrl.href);
        request.blob = hooks.resolveBlobUrl(parsedUrl.href);
        fallbackMode = 'cors';
      } else {
        request = inputRecord.request;
        signal = inputRecord.signal;
      }

      // Step 12: window
      if (initDict.window !== undefined && initDict.window !== null) throw new TypeError_("Failed to construct 'Request': The 'window' member must be null");

      // Step 12: the new request
      const inputHeaderList = inputRecord === undefined ? [] : hooks.headerList(inputRecord.headers);
      const newRequest = createRequest(request.urlList[request.urlList.length - 1]);
      newRequest.method = request.method;
      newRequest.urlList = [...request.urlList];
      newRequest.unsafeRequest = true;
      newRequest.priority = request.priority;
      newRequest.origin = request.origin;
      newRequest.referrer = request.referrer;
      newRequest.referrerPolicy = request.referrerPolicy;
      newRequest.mode = request.mode;
      newRequest.credentials = request.credentials;
      newRequest.cache = request.cache;
      newRequest.redirect = request.redirect;
      newRequest.integrity = request.integrity;
      newRequest.keepalive = request.keepalive;
      newRequest.reloadNavigation = request.reloadNavigation;
      newRequest.historyNavigation = request.historyNavigation;
      newRequest.useCorsPreflight = request.useCorsPreflight;
      newRequest.blob = request.blob;
      newRequest.redirectChain = request.redirectChain;
      newRequest.navigation = request.navigation;
      request = newRequest;

      // Step 13
      const initIsEmpty = ownKeys(initDict).length === 0;
      if (!initIsEmpty) {
        if (request.mode === 'navigate') request.mode = 'same-origin';
        request.reloadNavigation = false;
        request.historyNavigation = false;
        request.origin = 'client';
        request.referrer = 'client';
        request.referrerPolicy = '';
        request.urlList = [request.urlList[request.urlList.length - 1]];
        request.redirectChain = [];
        request.navigation = null;
      }

      // Step 14: referrer
      if (initDict.referrer !== undefined) {
        const referrer = initDict.referrer;
        if (referrer === '') {
          request.referrer = 'no-referrer';
        } else {
          const parsedReferrer = hooks.parseUrl(referrer);
          if (parsedReferrer === null) throw new TypeError_(`Failed to construct 'Request': Invalid referrer URL '${referrer}'`);
          if (parsedReferrer.href === 'about:client' || parsedReferrer.origin !== origin) {
            request.referrer = 'client';
          } else {
            request.referrer = parsedReferrer.href;
          }
        }
      }

      // Steps 15 to 22
      if (initDict.referrerPolicy !== undefined) request.referrerPolicy = initDict.referrerPolicy;
      const mode = initDict.mode !== undefined ? initDict.mode : fallbackMode;
      if (mode === 'navigate') throw new TypeError_("Failed to construct 'Request': Cannot construct a Request with a RequestInit whose mode member is set as 'navigate'.");
      if (mode !== null) request.mode = mode;
      if (initDict.credentials !== undefined) request.credentials = initDict.credentials;
      if (initDict.cache !== undefined) request.cache = initDict.cache;
      if (request.cache === 'only-if-cached' && request.mode !== 'same-origin') throw new TypeError_("Failed to construct 'Request': 'only-if-cached' can be set only with 'same-origin' mode");
      if (initDict.redirect !== undefined) request.redirect = initDict.redirect;
      if (initDict.integrity !== undefined) request.integrity = initDict.integrity;
      if (initDict.keepalive !== undefined) request.keepalive = initDict.keepalive;

      // Step 25: method
      if (initDict.method !== undefined) {
        const method = initDict.method;
        if (!isToken(method)) throw new TypeError_(`Failed to construct 'Request': '${method}' is not a valid HTTP method.`);
        if (isForbiddenMethod(method)) throw new TypeError_(`Failed to construct 'Request': '${method}' HTTP method is unsupported.`);
        request.method = normalizeMethod(method);
      }

      // Steps 26 to 30: the signal and the headers
      if (initDict.signal !== undefined) signal = initDict.signal;
      if (initDict.priority !== undefined) request.priority = initDict.priority;
      record.request = request;
      record.signal = apply(abortSignalAny, AbortSignal_, [signal === null ? [] : [signal]]);
      record.headers = hooks.createHeaders(inputHeaderList, 'request');
      if (request.mode === 'no-cors') {
        if (!isCorsSafelistedMethod(request.method)) throw new TypeError_(`Failed to construct 'Request': '${request.method}' is unsupported in no-cors mode.`);
        hooks.setGuard(record.headers, 'request-no-cors');
      }

      if (!initIsEmpty) {
        let headers = inputRecord === undefined ? hooks.createHeaders([], 'none') : hooks.createHeaders(inputHeaderList, 'none');
        if (initDict.headers !== undefined) headers = initDict.headers;
        const headerList = hooks.headerList(headers);
        hooks.clearHeaders(record.headers);
        for (const [name, value] of headerList) apply(headersAppend, record.headers, [name, value]);
      }

      // Steps 33 to 41: the body
      const inputBody = inputRecord === undefined ? null : inputRecord.body;
      if (((initDict.body !== undefined && initDict.body !== null) || inputBody !== null) && (request.method === 'GET' || request.method === 'HEAD')) {
        throw new TypeError_("Failed to construct 'Request': Request with GET/HEAD method cannot have body.");
      }

      let initBody = null;
      if (initDict.body !== undefined && initDict.body !== null) {
        const [body, contentType] = extractBody(initDict.body, request.keepalive);
        initBody = body;
        if (contentType !== null && !hooks.hasHeader(record.headers, 'Content-Type')) apply(headersAppend, record.headers, ['Content-Type', contentType]);
      }

      const inputOrInitBody = initBody !== null ? initBody : inputBody;
      if (inputOrInitBody !== null && inputOrInitBody.source === null) {
        if (initBody !== null && initDict.duplex === undefined) throw new TypeError_("Failed to construct 'Request': The duplex member must be specified for a request with a streaming body");
        if (request.mode !== 'same-origin' && request.mode !== 'cors') throw new TypeError_("Failed to construct 'Request': If request is made from ReadableStream, mode should be \"same-origin\" or \"cors\"");
        request.useCorsPreflight = true;
      }

      let finalBody = inputOrInitBody;
      if (initBody === null && inputBody !== null) {
        if (isUnusable(inputRecord)) throw new TypeError_("Failed to construct 'Request': Cannot construct a Request with a Request object that has already been used.");
        finalBody = proxyBody(inputBody);
      } else if (inputBody !== null && !isUnusable(inputRecord)) {
        // The body of the input request is disturbed even when it is replaced (as in the browsers)
        streams.lock(inputBody.stream);
      }

      request.body = finalBody;
      record.body = finalBody;
    }

    get method() { return thisRecord(this, 'Request').request.method; }

    get url() { return thisRecord(this, 'Request').request.urlList[0]; }

    get headers() { return thisRecord(this, 'Request').headers; }

    get destination() { return thisRecord(this, 'Request').request.destination; }

    get referrer() {
      const request = thisRecord(this, 'Request').request;
      if (request.referrer === 'no-referrer') return '';
      if (request.referrer === 'client') return 'about:client';
      return request.referrer;
    }

    get referrerPolicy() { return thisRecord(this, 'Request').request.referrerPolicy; }

    get mode() { return thisRecord(this, 'Request').request.mode; }

    get credentials() { return thisRecord(this, 'Request').request.credentials; }

    get cache() { return thisRecord(this, 'Request').request.cache; }

    get redirect() { return thisRecord(this, 'Request').request.redirect; }

    get integrity() { return thisRecord(this, 'Request').request.integrity; }

    get keepalive() { return thisRecord(this, 'Request').request.keepalive; }

    get isReloadNavigation() { return thisRecord(this, 'Request').request.reloadNavigation; }

    get isHistoryNavigation() { return thisRecord(this, 'Request').request.historyNavigation; }

    get signal() { return thisRecord(this, 'Request').signal; }

    get duplex() { thisRecord(this, 'Request'); return 'half'; }

    clone() {
      const record = thisRecord(this, 'Request');
      if (isUnusable(record)) throw new TypeError_("Failed to execute 'clone' on 'Request': Request body is already used");
      const request = record.request;
      const clonedRequest = ObjectCreate(null);
      for (const key of ownKeys(request)) clonedRequest[key] = request[key];
      clonedRequest.urlList = [...request.urlList];
      clonedRequest.body = request.body === null ? null : cloneBody(request.body);
      record.body = request.body;
      const headers = hooks.createHeaders(hooks.headerList(record.headers), hooks.getGuard(record.headers));
      const signal = apply(abortSignalAny, AbortSignal_, [[record.signal]]);
      return createRequestObject(record.realm, clonedRequest, headers, signal);
    }
  }

  // The body of a request created from another request: the stream of the other request is piped to a new stream (https://fetch.spec.whatwg.org/#concept-body-clone)
  function proxyBody(body) {
    if (body.source !== null) {
      streams.lock(body.stream);
      return bodyFromBytes(body.source);
    }

    return createBody(streams.createProxy(body.stream), null, null);
  }

  defineInterface(Request, 'Request', { constructor: 1 });
  defineBodyMixin(Request, 'Request');

  // ------------------------------------------------------------------------------------------------------------------
  // Response (https://fetch.spec.whatwg.org/#response-class)
  // ------------------------------------------------------------------------------------------------------------------

  // https://fetch.spec.whatwg.org/#concept-response
  function createResponse() {
    const response = ObjectCreate(null);
    response.type = 'default';
    response.urlList = [];
    response.status = 200;
    response.statusText = '';
    response.body = null;
    // The data of the internal response used by the cross-origin resource policy check of the Cache API
    response.requestIncludesCredentials = true;
    response.crossOriginResourcePolicy = null;
    // The internal response of an opaque response (its status, headers and body), kept for the fetches answered by a service worker
    response.internal = null;
    // The state of the body of the internal response, when it arrives over time (see drainInternalBody), or null
    response.internalBody = null;
    // The headers of the internal response of a CORS response, when the filtered response hides some of them
    response.internalHeaders = null;
    return response;
  }

  function createResponseObject(objectRealm, response, headers) {
    const object = ObjectCreate(objectRealm.Response.prototype);
    const record = createRecord(object, 'Response');
    record.response = response;
    record.body = response.body;
    record.headers = headers;
    return object;
  }

  const isNullBodyStatus = (status) => status === 101 || status === 103 || status === 204 || status === 205 || status === 304;
  const isRedirectStatus = (status) => status === 301 || status === 302 || status === 303 || status === 307 || status === 308;

  // https://fetch.spec.whatwg.org/#initialize-a-response
  function initializeResponse(record, init, bodyWithType) {
    if (init.status < 200 || init.status > 599) throw new RangeError_(`Failed to construct 'Response': The status provided (${init.status}) is outside the range [200, 599].`);

    // https://httpwg.org/specs/rfc9112.html#status.line: reason-phrase = 1*( HTAB / SP / VCHAR / obs-text )
    if (!apply(RegExpTest, /^[\t\x20-\x7E\x80-\xFF]*$/, [init.statusText])) throw new TypeError_("Failed to construct 'Response': Invalid statusText");
    record.response.status = init.status;
    record.response.statusText = init.statusText;
    if (init.headers !== undefined) {
      for (const [name, value] of hooks.headerList(init.headers)) apply(headersAppend, record.headers, [name, value]);
    }

    if (bodyWithType !== null) {
      if (isNullBodyStatus(record.response.status)) throw new TypeError_("Failed to construct 'Response': Response with null body status cannot have body");
      record.response.body = bodyWithType[0];
      record.body = bodyWithType[0];
      if (bodyWithType[1] !== null && !hooks.hasHeader(record.headers, 'Content-Type')) apply(headersAppend, record.headers, ['Content-Type', bodyWithType[1]]);
    }
  }

  class Response {
    constructor(body = null, init = undefined) {
      const initDict = convertResponseInit(init);
      const record = createRecord(this, 'Response');
      record.response = createResponse();
      record.body = null;
      record.headers = hooks.createHeaders([], 'response');
      const bodyWithType = body === null || body === undefined ? null : extractBody(body, false);
      initializeResponse(record, initDict, bodyWithType);
    }

    static error() {
      const response = createResponse();
      response.type = 'error';
      response.status = 0;
      return createResponseObject(realm, response, hooks.createHeaders([], 'immutable'));
    }

    static redirect(url, status = 302) {
      if (arguments.length < 1) throw new TypeError_("Failed to execute 'redirect' on 'Response': 1 argument required, but only 0 present.");
      url = convertUSVString(url);
      status = convertUnsignedShort(status);
      const parsedUrl = hooks.parseUrl(url);
      if (parsedUrl === null) throw new TypeError_(`Failed to execute 'redirect' on 'Response': Invalid URL '${url}'`);
      if (!isRedirectStatus(status)) throw new RangeError_(`Failed to execute 'redirect' on 'Response': Invalid status code ${status}`);
      const response = createResponse();
      response.status = status;
      return createResponseObject(realm, response, hooks.createHeaders([['Location', parsedUrl.href]], 'immutable'));
    }

    static json(data, init = undefined) {
      if (arguments.length < 1) throw new TypeError_("Failed to execute 'json' on 'Response': 1 argument required, but only 0 present.");
      const initDict = convertResponseInit(init);
      const text = JSONStringify(data);
      if (text === undefined) throw new TypeError_("Failed to execute 'json' on 'Response': The data is not JSON serializable");
      const object = ObjectCreate(realm.Response.prototype);
      const record = createRecord(object, 'Response');
      record.response = createResponse();
      record.body = null;
      record.headers = hooks.createHeaders([], 'response');
      initializeResponse(record, initDict, [bodyFromBytes(utf8Encode(text)), 'application/json']);
      return object;
    }

    get type() { return thisRecord(this, 'Response').response.type; }

    get url() {
      const urlList = thisRecord(this, 'Response').response.urlList;
      if (urlList.length === 0) return '';
      const url = urlList[urlList.length - 1];
      const index = url.indexOf('#');
      return index < 0 ? url : url.substring(0, index);
    }

    get redirected() { return thisRecord(this, 'Response').response.urlList.length > 1; }

    get status() { return thisRecord(this, 'Response').response.status; }

    get ok() {
      const status = thisRecord(this, 'Response').response.status;
      return status >= 200 && status <= 299;
    }

    get statusText() { return thisRecord(this, 'Response').response.statusText; }

    get headers() { return thisRecord(this, 'Response').headers; }

    clone() {
      const record = thisRecord(this, 'Response');
      if (isUnusable(record)) throw new TypeError_("Failed to execute 'clone' on 'Response': Response body is already used");
      const response = record.response;
      const clonedResponse = ObjectCreate(null);
      for (const key of ownKeys(response)) clonedResponse[key] = response[key];
      clonedResponse.urlList = [...response.urlList];
      clonedResponse.body = response.body === null ? null : cloneBody(response.body);
      record.body = response.body;
      const headers = hooks.createHeaders(hooks.headerList(record.headers), hooks.getGuard(record.headers));
      return createResponseObject(record.realm, clonedResponse, headers);
    }
  }

  defineInterface(Response, 'Response', { '@redirect': 1, '@json': 1 });
  defineBodyMixin(Response, 'Response');

  // ------------------------------------------------------------------------------------------------------------------
  // fetch() (https://fetch.spec.whatwg.org/#fetch-method)
  // ------------------------------------------------------------------------------------------------------------------

  // The body of the internal response of an opaque response that arrives over time: the scripts cannot read it, so it is
  // read in the background, and the body of the internal response is set at its end. The waiters are called once it is
  // read (or failed, e.g. the fetch was aborted).
  function drainInternalBody(reader, internal) {
    const state = ObjectCreate(null);
    state.done = false;
    state.error = null;
    state.waiters = [];
    const chunks = [];
    let length = 0;
    const finish = (error) => {
      if (error === null) {
        const bytes = new Uint8Array_(length);
        let offset = 0;
        for (let i = 0; i < chunks.length; i++) {
          apply(typedArraySet, bytes, [chunks[i], offset]);
          offset += apply(typedArrayByteLength, chunks[i], []);
        }

        internal.body = bytes;
      }

      state.done = true;
      state.error = error;
      const waiters = state.waiters;
      state.waiters = [];
      for (let i = 0; i < waiters.length; i++) waiters[i]();
    };
    const readNext = () => reader.read((chunk, failed, aborted) => {
      if (failed) {
        finish(aborted ? new DOMException_('The operation was aborted.', 'AbortError') : new TypeError_('Failed to fetch: the body of the response could not be read'));
      } else if (chunk === null) {
        finish(null);
      } else {
        chunks[chunks.length] = chunk;
        length += apply(typedArrayByteLength, chunk, []);
        readNext();
      }
    });
    readNext();
    return state;
  }

  // https://fetch.spec.whatwg.org/#abort-fetch
  function abortFetch(deferred, request, responseObject, error) {
    deferred.reject(error);
    if (request.body !== null && streams.isReadable(request.body.stream)) streams.cancel(request.body.stream, error);
    if (responseObject === null) return;
    const body = getRecord(responseObject, 'Response').body;
    if (body !== null && streams.isReadable(body.stream)) streams.error(body.stream, error);
  }

  // The request given to the fetch algorithm of the library
  function serializeRequestForFetch(requestRecord, bodyBytes) {
    const request = requestRecord.request;
    const serialized = ObjectCreate(null);
    serialized.method = request.method;
    serialized.urlList = request.urlList;
    serialized.headers = hooks.headerList(requestRecord.headers);
    serialized.body = bodyBytes;
    serialized.bodyHasSource = request.body === null || request.body.source !== null;
    serialized.mode = request.mode;
    serialized.credentials = request.credentials;
    serialized.cache = request.cache;
    serialized.redirect = request.redirect;
    serialized.referrer = request.referrer;
    serialized.referrerPolicy = request.referrerPolicy;
    serialized.useCorsPreflight = request.useCorsPreflight;
    serialized.keepalive = request.keepalive;
    serialized.integrity = request.integrity;
    serialized.blob = request.blob;
    serialized.destination = request.destination;
    serialized.origin = request.origin;
    serialized.redirectChain = request.redirectChain;
    serialized.navigation = request.mode === 'navigate' ? request.navigation : null;
    return serialized;
  }

  // fetch() is an operation of the global object: 'this' is the global object or undefined (https://webidl.spec.whatwg.org/#dfn-create-operation-function)
  const global = globalThis;
  function fetch(input, init = undefined) {
    const deferred = newPromise();
    try {
      if (this !== undefined && this !== null && this !== global && !hooks.isWindow(this)) throw new TypeError_("Illegal invocation: 'this' does not implement interface Window");
      if (arguments.length < 1) throw new TypeError_("Failed to execute 'fetch' on 'Window': 1 argument required, but only 0 present.");
      const requestObject = new realm.Request(input, init);
      const requestRecord = getRecord(requestObject, 'Request');
      const request = requestRecord.request;
      const signal = requestRecord.signal;
      if (apply(abortSignalAborted, signal, [])) {
        abortFetch(deferred, request, null, apply(abortSignalReason, signal, []));
        return deferred.promise;
      }

      let responseObject = null;
      let locallyAborted = false;
      let controller = null;
      hooks.addAbortAlgorithm(signal, () => {
        locallyAborted = true;
        abortFetch(deferred, request, responseObject, apply(abortSignalReason, signal, []));
        if (controller !== null) controller();
      });

      const start = (bodyBytes) => {
        if (locallyAborted) return;
        const serialized = serializeRequestForFetch(requestRecord, bodyBytes);
        controller = hooks.fetch(serialized, (response) => {
          if (locallyAborted) return;

          // A fetch aborted with its document (e.g. window.stop()) is rejected with an AbortError
          if (response.aborted) {
            abortFetch(deferred, request, responseObject, new DOMException_('The operation was aborted.', 'AbortError'));
            return;
          }

          if (response.type === 'error') {
            deferred.reject(new TypeError_('Failed to fetch'));
            return;
          }

          const fetched = createResponse();
          fetched.type = response.type;
          fetched.urlList = response.urlList;
          fetched.status = response.status;
          fetched.statusText = response.statusText;
          fetched.requestIncludesCredentials = response.requestIncludesCredentials;
          fetched.crossOriginResourcePolicy = response.crossOriginResourcePolicy;
          fetched.body = response.bodyReader !== null ? bodyFromReader(response.bodyReader) : response.body === null ? null : bodyFromBytes(response.body);
          fetched.internal = response.internal;
          fetched.internalBody = response.internalBodyReader === null ? null : drainInternalBody(response.internalBodyReader, response.internal);
          fetched.internalHeaders = response.internalHeaders;
          responseObject = createResponseObject(realm, fetched, hooks.createHeaders(response.headers, 'immutable'));
          deferred.resolve(responseObject);
        });
      };

      // The body of the request is read before the request is sent (the requests are not streamed)
      if (request.body === null) {
        start(null);
      } else if (request.body.source !== null) {
        streams.lock(request.body.stream);
        start(request.body.source);
      } else {
        streams.readAllBytes(request.body.stream, start, (e) => {
          if (!locallyAborted) deferred.reject(new TypeError_('Failed to fetch: ' + e));
        });
      }
    } catch (e) {
      deferred.reject(e);
    }

    return deferred.promise;
  }

  defineProperty(fetch, 'length', { value: 1, writable: false, enumerable: false, configurable: true });

  // ------------------------------------------------------------------------------------------------------------------
  // fetchLater() (https://fetch.spec.whatwg.org/#dom-window-fetchlater)
  // ------------------------------------------------------------------------------------------------------------------

  class FetchLaterResult {
    constructor() {
      throw new TypeError_('Illegal constructor');
    }

    get activated() { return thisRecord(this, 'FetchLaterResult').deferredFetch.isActivated(); }
  }

  defineInterface(FetchLaterResult, 'FetchLaterResult', { constructor: 0 });

  function fetchLater(input, init = undefined) {
    if (this !== undefined && this !== null && this !== global && !hooks.isWindow(this)) throw new TypeError_("Illegal invocation: 'this' does not implement interface Window");
    if (arguments.length < 1) throw new TypeError_("Failed to execute 'fetchLater' on 'Window': 1 argument required, but only 0 present.");

    // DeferredRequestInit: DOMHighResTimeStamp activateAfter (a double: the values that are not finite are a TypeError)
    let activateAfter = null;
    if (init !== undefined && init !== null) {
      if (!isObject(init)) throw new TypeError_('The init argument is not an object');
      const activateAfterValue = init.activateAfter;
      if (activateAfterValue !== undefined) {
        activateAfter = +activateAfterValue;
        if (activateAfter !== activateAfter || activateAfter === Infinity || activateAfter === -Infinity) throw new TypeError_("The 'activateAfter' member is not a finite number");
      }
    }

    const requestObject = new realm.Request(input, init);
    const requestRecord = getRecord(requestObject, 'Request');
    const request = requestRecord.request;
    const signal = requestRecord.signal;
    if (apply(abortSignalAborted, signal, [])) throw apply(abortSignalReason, signal, []);
    if (activateAfter !== null && activateAfter < 0) throw new RangeError('The activateAfter option is negative');

    // "If request's body is not null, and request's body length is null, then throw a TypeError": the requests whose
    // body is a ReadableStream object cannot be deferred. The other checks (the document, the URL, the quota) are done by
    // the library, which queues the deferred fetch.
    if (request.body !== null && request.body.source === null) throw new TypeError_('A request whose body is a stream cannot be deferred');
    const deferredFetch = hooks.fetchLater(serializeRequestForFetch(requestRecord, request.body === null ? null : request.body.source), activateAfter);
    hooks.addAbortAlgorithm(signal, () => deferredFetch.abort());
    const result = ObjectCreate(FetchLaterResult.prototype);
    createRecord(result, 'FetchLaterResult').deferredFetch = deferredFetch;
    return result;
  }

  defineProperty(fetchLater, 'length', { value: 1, writable: false, enumerable: false, configurable: true });

  // ------------------------------------------------------------------------------------------------------------------
  // The internals used by the Cache API (CacheStorage.js): the requests and the responses of the cache entries are
  // serialized as objects ({ method, url, headers... } and { type, status, statusText, headers, urlList, body })
  // ------------------------------------------------------------------------------------------------------------------

  const cacheSupport = ObjectCreate(null);
  cacheSupport.getRequestRecord = (object) => getRecord(object, 'Request');
  cacheSupport.getResponseRecord = (object) => getRecord(object, 'Response');
  cacheSupport.isUnusable = isUnusable;
  cacheSupport.isDisturbedOrLocked = (record) => record.body !== null && (streams.isDisturbed(record.body.stream) || streams.isLocked(record.body.stream));

  // The request of a Request object (the request of a new Request object for a string, which can throw)
  cacheSupport.toRequestRecord = (input) => {
    const record = getRecord(input, 'Request');
    return record !== undefined ? record : getRecord(new realm.Request(input), 'Request');
  };

  cacheSupport.serializeRequest = (record) => {
    const request = record.request;
    const serialized = ObjectCreate(null);
    serialized.method = request.method;
    serialized.url = request.urlList[request.urlList.length - 1];
    serialized.headers = hooks.headerList(record.headers);
    serialized.mode = request.mode;
    serialized.credentials = request.credentials;
    serialized.cache = request.cache;
    serialized.redirect = request.redirect;
    serialized.integrity = request.integrity;
    serialized.referrer = request.referrer;
    serialized.referrerPolicy = request.referrerPolicy;
    return serialized;
  };

  cacheSupport.serializeResponse = (record, body) => {
    const response = record.response;
    const serialized = ObjectCreate(null);
    serialized.type = response.type;
    serialized.status = response.status;
    serialized.statusText = response.statusText;
    serialized.headers = hooks.headerList(record.headers);
    serialized.urlList = [...response.urlList];
    serialized.requestIncludesCredentials = response.requestIncludesCredentials;
    serialized.crossOriginResourcePolicy = response.crossOriginResourcePolicy;
    serialized.body = body;
    serialized.internal = response.internal;
    serialized.internalHeaders = response.internalHeaders;
    return serialized;
  };

  // A new Request object of the realm for a serialized request (https://fetch.spec.whatwg.org/#request-create)
  cacheSupport.createRequestObject = (serialized) => {
    const request = createRequest(serialized.url);
    request.method = serialized.method;
    request.mode = serialized.mode;
    request.credentials = serialized.credentials;
    request.cache = serialized.cache;
    request.redirect = serialized.redirect;
    request.integrity = serialized.integrity;
    request.referrer = serialized.referrer;
    request.referrerPolicy = serialized.referrerPolicy;
    const headers = hooks.createHeaders(serialized.headers, request.mode === 'no-cors' ? 'request-no-cors' : 'request');
    return createRequestObject(realm, request, headers, apply(abortSignalAny, AbortSignal_, [[]]));
  };

  // A new Response object of the realm for a serialized response (https://fetch.spec.whatwg.org/#response-create)
  cacheSupport.createResponseObject = (serialized) => {
    const response = createResponse();
    response.type = serialized.type;
    response.status = serialized.status;
    response.statusText = serialized.statusText;
    response.urlList = [...serialized.urlList];
    response.requestIncludesCredentials = serialized.requestIncludesCredentials;
    response.crossOriginResourcePolicy = serialized.crossOriginResourcePolicy;
    response.body = serialized.body === null ? null : bodyFromBytes(serialized.body);
    response.internal = serialized.internal === undefined ? null : serialized.internal;
    return createResponseObject(realm, response, hooks.createHeaders(serialized.headers, 'immutable'));
  };

  // Reads the body of a request or a response (the body is disturbed): calls onBytes with the bytes, or null without body
  cacheSupport.readBody = (record, onBytes, onError) => {
    // The body of the internal response of an opaque response is read once it is fully received
    const internalBody = record.response.internalBody;
    if (internalBody !== null && internalBody !== undefined) {
      if (!internalBody.done) {
        internalBody.waiters[internalBody.waiters.length] = () => cacheSupport.readBody(record, onBytes, onError);
        return;
      }

      if (internalBody.error !== null) {
        hooks.queueTask(() => onError(internalBody.error));
        return;
      }
    }

    if (record.body === null) {
      onBytes(null);
      return;
    }

    streams.readAllBytes(record.body.stream, (bytes) => hooks.queueTask(() => onBytes(bytes)), (e) => hooks.queueTask(() => onError(e)));
  };

  cacheSupport.fetch = (requestObject) => apply(fetch, undefined, [requestObject]);

  // The Request object of a fetch event (https://w3c.github.io/ServiceWorker/#create-fetch-event-and-dispatch): a request
  // of a client, whose headers are immutable and whose signal is the signal of the fetch event
  cacheSupport.createFetchEventRequest = (serialized, signal) => {
    const request = createRequest(serialized.urlList[0]);
    request.urlList = [...serialized.urlList];
    request.method = serialized.method;
    request.mode = serialized.mode;
    request.credentials = serialized.credentials;
    request.cache = serialized.cache;
    request.redirect = serialized.redirect;
    request.integrity = serialized.integrity;
    request.referrer = serialized.referrer;
    request.referrerPolicy = serialized.referrerPolicy;
    request.destination = serialized.destination;
    request.keepalive = serialized.keepalive;
    request.reloadNavigation = serialized.reloadNavigation;
    request.historyNavigation = serialized.historyNavigation;
    request.origin = serialized.origin;
    request.redirectChain = [...serialized.redirectChain];
    request.navigation = serialized.navigation;
    request.body = serialized.body === null ? null : bodyFromBytes(serialized.body);
    return createRequestObject(realm, request, hooks.createHeaders(serialized.headers, 'immutable'), signal);
  };

  // The response of the value given to respondWith() (https://w3c.github.io/ServiceWorker/#dom-fetchevent-respondwith): the
  // body of a Response object is read (the bodies are not streamed to the client); null for another value, or a Response
  // object whose body is disturbed or locked or cannot be read
  cacheSupport.readFetchEventResponse = (value, onResponse) => {
    const record = getRecord(value, 'Response');
    if (record === undefined || cacheSupport.isDisturbedOrLocked(record)) {
      onResponse(null);
      return;
    }

    cacheSupport.readBody(record, (bytes) => onResponse(cacheSupport.serializeResponse(record, bytes)), () => onResponse(null));
  };

  // The response of the value given to respondWith() for a client that reads the body as it arrives: onResponse is called
  // at once with the response, then the chunks of its body are given to onChunk as they are read, until onEnd or onError.
  // Returns the function that cancels the reading (the client stops reading the body), or null. The body of an opaque
  // response, which the client does not read, is read before the response is given.
  cacheSupport.readFetchEventResponseStreamed = (value, onResponse, onChunk, onEnd, onError) => {
    const record = getRecord(value, 'Response');
    if (record === undefined || cacheSupport.isDisturbedOrLocked(record) || record.body === null || record.response.type === 'opaque' || record.response.type === 'opaqueredirect') {
      cacheSupport.readFetchEventResponse(value, onResponse);
      return null;
    }

    const serialized = cacheSupport.serializeResponse(record, null);
    serialized.streamed = true;
    onResponse(serialized);
    return streams.readChunks(record.body.stream, onChunk, onEnd, onError);
  };

  const realm = {
    Request,
    Response,
    hooks,
    newPromise,
    promiseRejectedWith,
    toArrayBuffer,
    toUint8Array: (bytes) => new Uint8Array_(toArrayBuffer(bytes)),
  };
  return { Request, Response, fetch, fetchLater, FetchLaterResult, cacheSupport };
});
