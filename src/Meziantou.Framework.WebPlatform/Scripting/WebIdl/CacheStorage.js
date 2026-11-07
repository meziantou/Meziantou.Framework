// The Cache and CacheStorage interfaces of Service Workers (https://w3c.github.io/ServiceWorker/#cache-objects),
// implemented in JavaScript because they store and return the Request and Response objects of Fetch.js. The caches are
// stored by the CacheStorageBackend of the host, which also matches the requests (the Vary header) and applies the batch
// operations: the hooks are synchronous, and the promises are settled by a task.
//
// The script is evaluated once per realm, when caches or one of the interfaces is first used. It is a function called
// with the hooks of the host, and returns the interface objects and the CacheStorage object of the global object.
(function (hooks) {
  'use strict';

  const apply = Reflect.apply;
  const ObjectCreate = Object.create;
  const ObjectFreeze = Object.freeze;
  const defineProperty = Object.defineProperty;
  const getOwnPropertyDescriptor = Object.getOwnPropertyDescriptor;
  const ownKeys = Reflect.ownKeys;
  const TypeError_ = TypeError;
  const Promise_ = Promise;
  const promiseReject = Promise.reject;
  const weakMapGet = WeakMap.prototype.get;
  const weakMapSet = WeakMap.prototype.set;
  const SymbolIterator = Symbol.iterator;
  const SymbolToStringTag = Symbol.toStringTag;
  const isObject = (value) => (typeof value === 'object' && value !== null) || typeof value === 'function';
  const fetchSupport = hooks.fetchSupport;
  const records = new WeakMap();

  // https://webidl.spec.whatwg.org/#dfn-create-operation-function: the exceptions of the operations returning a promise reject the promise
  const promiseRejectedWith = (reason) => apply(promiseReject, Promise_, [reason]);

  function newPromise() {
    const deferred = ObjectCreate(null);
    deferred.promise = new Promise_((resolve, reject) => {
      deferred.resolve = resolve;
      deferred.reject = reject;
    });
    return deferred;
  }

  // The steps run in parallel by the host: the promise is settled by a task
  function settle(deferred, steps) {
    let result;
    try {
      result = steps();
    } catch (e) {
      hooks.queueTask(() => deferred.reject(e));
      return;
    }

    hooks.queueTask(() => {
      try {
        deferred.resolve(result());
      } catch (e) {
        deferred.reject(e);
      }
    });
  }

  function thisRecord(object, kind) {
    const record = isObject(object) ? apply(weakMapGet, records, [object]) : undefined;
    if (record === undefined || record.kind !== kind) throw new TypeError_(`Illegal invocation: 'this' does not implement interface ${kind}`);
    return record;
  }

  // CacheQueryOptions and MultiCacheQueryOptions (https://webidl.spec.whatwg.org/#js-dictionary: the members in lexicographical order)
  function convertOptions(value, multi) {
    const options = ObjectCreate(null);
    options.cacheName = undefined;
    options.ignoreMethod = false;
    options.ignoreSearch = false;
    options.ignoreVary = false;
    if (value === undefined || value === null) return options;
    if (!isObject(value)) throw new TypeError_('The options are not an object');
    if (multi) {
      const cacheName = value.cacheName;
      if (cacheName !== undefined) options.cacheName = `${cacheName}`;
    }

    options.ignoreMethod = !!value.ignoreMethod;
    options.ignoreSearch = !!value.ignoreSearch;
    options.ignoreVary = !!value.ignoreVary;
    return options;
  }

  // sequence<RequestInfo> (https://webidl.spec.whatwg.org/#js-sequence)
  function convertSequence(value) {
    if (!isObject(value)) throw new TypeError_('The requests are not a sequence');
    const method = value[SymbolIterator];
    if (typeof method !== 'function') throw new TypeError_('The requests are not iterable');
    const items = [];
    const iterator = apply(method, value, []);
    while (true) {
      const next = iterator.next();
      if (next.done) break;
      defineProperty(items, items.length, { value: next.value, writable: true, enumerable: true, configurable: true });
    }

    return items;
  }

  const isHttpUrl = (url) => url.startsWith('http:') || url.startsWith('https:');

  // The values of the Vary header of a response record contain '*'
  function hasVaryStar(responseRecord) {
    for (const [name, value] of hooks.headerList(responseRecord.headers)) {
      if (name.toLowerCase() !== 'vary') continue;
      for (const item of value.split(',')) {
        if (item.trim() === '*') return true;
      }
    }

    return false;
  }

  // Makes the members of a class follow WebIDL: the operations are enumerable, and the class has a @@toStringTag
  function defineInterface(constructor, name, lengths) {
    const prototype = constructor.prototype;
    for (const key of ownKeys(prototype)) {
      if (key === 'constructor') continue;
      const descriptor = getOwnPropertyDescriptor(prototype, key);
      descriptor.enumerable = typeof key === 'string';
      defineProperty(prototype, key, descriptor);
    }

    for (const key of ownKeys(lengths)) {
      defineProperty(prototype[key], 'length', { value: lengths[key], writable: false, enumerable: false, configurable: true });
    }

    defineProperty(prototype, SymbolToStringTag, { value: name, writable: false, enumerable: false, configurable: true });
  }

  // ------------------------------------------------------------------------------------------------------------------
  // Cache (https://w3c.github.io/ServiceWorker/#cache-interface)
  // ------------------------------------------------------------------------------------------------------------------

  let constructing = false;

  function createCache(handle) {
    constructing = true;
    let cache;
    try {
      cache = new Cache();
    } finally {
      constructing = false;
    }

    const record = ObjectCreate(null);
    record.kind = 'Cache';
    record.handle = handle;
    apply(weakMapSet, records, [cache, record]);
    return cache;
  }

  // https://w3c.github.io/ServiceWorker/#cache-matchall (with the first response only for match())
  function matchAll(record, request, options, single) {
    let serialized = null;
    if (request !== undefined) {
      const requestRecord = fetchSupport.toRequestRecord(request);
      if (requestRecord.request.method !== 'GET' && !options.ignoreMethod) {
        const deferred = newPromise();
        hooks.queueTask(() => deferred.resolve(single ? undefined : ObjectFreeze([])));
        return deferred.promise;
      }

      serialized = fetchSupport.serializeRequest(requestRecord);
    }

    const deferred = newPromise();
    settle(deferred, () => {
      const responses = hooks.queryResponses(record.handle, serialized, options.ignoreSearch, options.ignoreMethod, options.ignoreVary, single);
      return () => {
        if (single) return responses.length === 0 ? undefined : fetchSupport.createResponseObject(responses[0]);
        const list = [];
        for (const response of responses) defineProperty(list, list.length, { value: fetchSupport.createResponseObject(response), writable: true, enumerable: true, configurable: true });
        return ObjectFreeze(list);
      };
    });
    return deferred.promise;
  }

  // https://w3c.github.io/ServiceWorker/#batch-cache-operations: the operations are { request, response, options }
  function batch(record, operations, deferred, toResult) {
    settle(deferred, () => {
      const results = hooks.batch(record.handle, operations);
      return () => toResult(results);
    });
  }

  class Cache {
    constructor() {
      if (!constructing) throw new TypeError_('Illegal constructor');
    }

    match(request, options = undefined) {
      try {
        const record = thisRecord(this, 'Cache');
        if (arguments.length < 1) throw new TypeError_("Failed to execute 'match' on 'Cache': 1 argument required, but only 0 present.");
        return matchAll(record, request, convertOptions(options, false), true);
      } catch (e) {
        return promiseRejectedWith(e);
      }
    }

    matchAll(request = undefined, options = undefined) {
      try {
        const record = thisRecord(this, 'Cache');
        return matchAll(record, request, convertOptions(options, false), false);
      } catch (e) {
        return promiseRejectedWith(e);
      }
    }

    add(request) {
      try {
        thisRecord(this, 'Cache');
        if (arguments.length < 1) throw new TypeError_("Failed to execute 'add' on 'Cache': 1 argument required, but only 0 present.");
        return apply(Cache.prototype.addAll, this, [[request]]);
      } catch (e) {
        return promiseRejectedWith(e);
      }
    }

    // https://w3c.github.io/ServiceWorker/#cache-addAll
    addAll(requests) {
      try {
        const record = thisRecord(this, 'Cache');
        if (arguments.length < 1) throw new TypeError_("Failed to execute 'addAll' on 'Cache': 1 argument required, but only 0 present.");
        const requestRecords = [];
        for (const request of convertSequence(requests)) {
          const requestRecord = fetchSupport.toRequestRecord(request);
          const url = requestRecord.request.urlList[requestRecord.request.urlList.length - 1];
          if (!isHttpUrl(url) || requestRecord.request.method !== 'GET') throw new TypeError_("Failed to execute 'addAll' on 'Cache': Request scheme or method is not supported");
          requestRecords[requestRecords.length] = requestRecord;
        }

        const deferred = newPromise();
        const count = requestRecords.length;
        const operations = [];
        let remaining = count;
        let failed = false;
        const controllers = [];
        const fail = (e) => {
          if (failed) return;
          failed = true;
          for (const controller of controllers) controller.abort();
          deferred.reject(e);
        };

        if (count === 0) {
          batch(record, operations, deferred, () => undefined);
          return deferred.promise;
        }

        for (let i = 0; i < count; i++) {
          const requestRecord = requestRecords[i];
          const requestObject = fetchSupport.createRequestObject(fetchSupport.serializeRequest(requestRecord));
          // The fetches of the other requests are aborted when a fetch fails
          const controller = new hooks.interfaces.AbortController();
          controllers[controllers.length] = controller;
          const signal = hooks.anySignal([requestRecord.signal, controller.signal]);
          const fetchRequest = new (hooks.fetchSupportRequest)(requestObject, { signal });
          fetchSupport.fetch(fetchRequest).then((response) => {
            if (failed) return;
            const responseRecord = fetchSupport.getResponseRecord(response);
            const status = responseRecord.response.status;
            if (responseRecord.response.type === 'error' || status < 200 || status > 299 || status === 206) {
              fail(new TypeError_("Failed to execute 'addAll' on 'Cache': Request failed"));
              return;
            }

            if (hasVaryStar(responseRecord)) {
              fail(new TypeError_("Failed to execute 'addAll' on 'Cache': Vary header contains *"));
              return;
            }

            fetchSupport.readBody(responseRecord, (bytes) => {
              if (failed) return;
              const operation = ObjectCreate(null);
              operation.request = fetchSupport.serializeRequest(requestRecord);
              operation.response = fetchSupport.serializeResponse(responseRecord, bytes);
              operation.options = null;
              operations[i] = operation;
              if (--remaining === 0) batch(record, operations, deferred, () => undefined);
            }, fail);
          }, fail);
        }

        return deferred.promise;
      } catch (e) {
        return promiseRejectedWith(e);
      }
    }

    // https://w3c.github.io/ServiceWorker/#cache-put
    put(request, response) {
      try {
        const record = thisRecord(this, 'Cache');
        if (arguments.length < 2) throw new TypeError_(`Failed to execute 'put' on 'Cache': 2 arguments required, but only ${arguments.length} present.`);
        const requestRecord = fetchSupport.toRequestRecord(request);
        const responseRecord = fetchSupport.getResponseRecord(response);
        if (responseRecord === undefined) throw new TypeError_("Failed to execute 'put' on 'Cache': parameter 2 is not of type 'Response'.");
        const url = requestRecord.request.urlList[requestRecord.request.urlList.length - 1];
        if (!isHttpUrl(url)) throw new TypeError_("Failed to execute 'put' on 'Cache': Request scheme is unsupported");
        if (requestRecord.request.method !== 'GET') throw new TypeError_("Failed to execute 'put' on 'Cache': Request method is unsupported");
        if (responseRecord.response.status === 206) throw new TypeError_("Failed to execute 'put' on 'Cache': Partial response (status code 206) is unsupported");
        if (hasVaryStar(responseRecord)) throw new TypeError_("Failed to execute 'put' on 'Cache': Vary header contains *");
        if (fetchSupport.isDisturbedOrLocked(responseRecord)) throw new TypeError_("Failed to execute 'put' on 'Cache': Response body is already used");
        const serializedRequest = fetchSupport.serializeRequest(requestRecord);
        const deferred = newPromise();
        fetchSupport.readBody(responseRecord, (bytes) => {
          const operation = ObjectCreate(null);
          operation.request = serializedRequest;
          operation.response = fetchSupport.serializeResponse(responseRecord, bytes);
          operation.options = null;
          batch(record, [operation], deferred, () => undefined);
        }, (e) => deferred.reject(e));
        return deferred.promise;
      } catch (e) {
        return promiseRejectedWith(e);
      }
    }

    // https://w3c.github.io/ServiceWorker/#cache-delete
    delete(request, options = undefined) {
      try {
        const record = thisRecord(this, 'Cache');
        if (arguments.length < 1) throw new TypeError_("Failed to execute 'delete' on 'Cache': 1 argument required, but only 0 present.");
        const convertedOptions = convertOptions(options, false);
        const requestRecord = fetchSupport.toRequestRecord(request);
        const deferred = newPromise();
        if (requestRecord.request.method !== 'GET' && !convertedOptions.ignoreMethod) {
          hooks.queueTask(() => deferred.resolve(false));
          return deferred.promise;
        }

        const operation = ObjectCreate(null);
        operation.request = fetchSupport.serializeRequest(requestRecord);
        operation.response = null;
        operation.options = convertedOptions;
        batch(record, [operation], deferred, (results) => results[0] > 0);
        return deferred.promise;
      } catch (e) {
        return promiseRejectedWith(e);
      }
    }

    // https://w3c.github.io/ServiceWorker/#cache-keys
    keys(request = undefined, options = undefined) {
      try {
        const record = thisRecord(this, 'Cache');
        const convertedOptions = convertOptions(options, false);
        let serialized = null;
        if (request !== undefined) {
          const requestRecord = fetchSupport.toRequestRecord(request);
          if (requestRecord.request.method !== 'GET' && !convertedOptions.ignoreMethod) {
            const deferred = newPromise();
            hooks.queueTask(() => deferred.resolve(ObjectFreeze([])));
            return deferred.promise;
          }

          serialized = fetchSupport.serializeRequest(requestRecord);
        }

        const deferred = newPromise();
        settle(deferred, () => {
          const requests = hooks.queryRequests(record.handle, serialized, convertedOptions.ignoreSearch, convertedOptions.ignoreMethod, convertedOptions.ignoreVary);
          return () => {
            const list = [];
            for (const item of requests) defineProperty(list, list.length, { value: fetchSupport.createRequestObject(item), writable: true, enumerable: true, configurable: true });
            return ObjectFreeze(list);
          };
        });
        return deferred.promise;
      } catch (e) {
        return promiseRejectedWith(e);
      }
    }
  }

  defineInterface(Cache, 'Cache', { match: 1, matchAll: 0, add: 1, addAll: 1, put: 2, delete: 1, keys: 0 });

  // ------------------------------------------------------------------------------------------------------------------
  // CacheStorage (https://w3c.github.io/ServiceWorker/#cachestorage-interface)
  // ------------------------------------------------------------------------------------------------------------------

  class CacheStorage {
    constructor() {
      if (!constructing) throw new TypeError_('Illegal constructor');
    }

    // https://w3c.github.io/ServiceWorker/#cache-storage-match
    match(request, options = undefined) {
      try {
        thisRecord(this, 'CacheStorage');
        if (arguments.length < 1) throw new TypeError_("Failed to execute 'match' on 'CacheStorage': 1 argument required, but only 0 present.");
        const convertedOptions = convertOptions(options, true);
        const requestRecord = fetchSupport.toRequestRecord(request);
        const deferred = newPromise();
        if (requestRecord.request.method !== 'GET' && !convertedOptions.ignoreMethod) {
          hooks.queueTask(() => deferred.resolve(undefined));
          return deferred.promise;
        }

        const serialized = fetchSupport.serializeRequest(requestRecord);
        settle(deferred, () => {
          const response = hooks.storageMatch(serialized, convertedOptions.cacheName, convertedOptions.ignoreSearch, convertedOptions.ignoreMethod, convertedOptions.ignoreVary);
          return () => response === null ? undefined : fetchSupport.createResponseObject(response);
        });
        return deferred.promise;
      } catch (e) {
        return promiseRejectedWith(e);
      }
    }

    has(cacheName) {
      try {
        thisRecord(this, 'CacheStorage');
        if (arguments.length < 1) throw new TypeError_("Failed to execute 'has' on 'CacheStorage': 1 argument required, but only 0 present.");
        const name = `${cacheName}`;
        const deferred = newPromise();
        settle(deferred, () => {
          const result = hooks.storageHas(name);
          return () => result;
        });
        return deferred.promise;
      } catch (e) {
        return promiseRejectedWith(e);
      }
    }

    open(cacheName) {
      try {
        thisRecord(this, 'CacheStorage');
        if (arguments.length < 1) throw new TypeError_("Failed to execute 'open' on 'CacheStorage': 1 argument required, but only 0 present.");
        const name = `${cacheName}`;
        const deferred = newPromise();
        settle(deferred, () => {
          const handle = hooks.storageOpen(name);
          return () => createCache(handle);
        });
        return deferred.promise;
      } catch (e) {
        return promiseRejectedWith(e);
      }
    }

    delete(cacheName) {
      try {
        thisRecord(this, 'CacheStorage');
        if (arguments.length < 1) throw new TypeError_("Failed to execute 'delete' on 'CacheStorage': 1 argument required, but only 0 present.");
        const name = `${cacheName}`;
        const deferred = newPromise();
        settle(deferred, () => {
          const result = hooks.storageDelete(name);
          return () => result;
        });
        return deferred.promise;
      } catch (e) {
        return promiseRejectedWith(e);
      }
    }

    keys() {
      try {
        thisRecord(this, 'CacheStorage');
        const deferred = newPromise();
        settle(deferred, () => {
          const names = hooks.storageKeys();
          return () => names;
        });
        return deferred.promise;
      } catch (e) {
        return promiseRejectedWith(e);
      }
    }
  }

  defineInterface(CacheStorage, 'CacheStorage', { match: 1, has: 1, open: 1, delete: 1, keys: 0 });

  // The CacheStorage object of the global object
  constructing = true;
  let caches;
  try {
    caches = new CacheStorage();
  } finally {
    constructing = false;
  }

  const cachesRecord = ObjectCreate(null);
  cachesRecord.kind = 'CacheStorage';
  apply(weakMapSet, records, [caches, cachesRecord]);

  return { Cache, CacheStorage, caches };
});
