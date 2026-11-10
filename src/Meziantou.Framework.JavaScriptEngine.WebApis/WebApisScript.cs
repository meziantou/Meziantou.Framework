namespace Meziantou.Framework.JavaScriptEngine.WebApis;

// The bindings of the web APIs, written in JavaScript: the classes, the WebIDL conversions of the arguments and the brand
// checks (private fields) are the ones of the language. The algorithms run in .NET (the natives), with the code shared
// with Meziantou.Framework.WebPlatform. The intrinsics are captured when the script runs, before the user code can
// replace them.
internal static class WebApisScript
{
    internal const string Source = """
        (function (natives, global, options, structuredCloneFactory) {
          'use strict';
          const { defineProperty, getOwnPropertyDescriptor, getOwnPropertyNames, getPrototypeOf, setPrototypeOf, create: objectCreate, keys: objectKeys, is: objectIs } = Object;
          const { apply, ownKeys } = Reflect;
          const String_ = String;
          const TypeError_ = TypeError;
          const RangeError_ = RangeError;
          const WeakSet_ = WeakSet;
          const WeakMap_ = WeakMap;
          const Map_ = Map;
          const Set_ = Set;
          const SymbolIterator = Symbol.iterator;
          const SymbolToStringTag = Symbol.toStringTag;
          const arrayPush = Array.prototype.push;
          const arrayJoin = Array.prototype.join;
          const isArray = Array.isArray;
          const weakSetAdd = WeakSet.prototype.add;
          const weakSetHas = WeakSet.prototype.has;
          const weakSetDelete = WeakSet.prototype.delete;
          const weakMapGet = WeakMap.prototype.get;
          const weakMapSet = WeakMap.prototype.set;
          const weakMapHas = WeakMap.prototype.has;
          const mapGet = Map.prototype.get;
          const mapSet = Map.prototype.set;
          const mapHas = Map.prototype.has;
          const mapDelete = Map.prototype.delete;
          const mapEntries = Map.prototype.entries;
          const mapSize = getOwnPropertyDescriptor(Map.prototype, 'size').get;
          const setValues = Set.prototype.values;
          const setSize = getOwnPropertyDescriptor(Set.prototype, 'size').get;
          const functionToString = Function.prototype.toString;
          const objectToString = Object.prototype.toString;
          const hasOwn = Object.prototype.hasOwnProperty;
          const dateToISOString = Date.prototype.toISOString;
          const dateGetTime = Date.prototype.getTime;
          const regExpToString = RegExp.prototype.toString;
          const symbolToString = Symbol.prototype.toString;
          const parseInt_ = parseInt;
          const parseFloat_ = parseFloat;
          const NaN_ = NaN;
          const stringReplace = String.prototype.replace;
          const typedArrayPrototype = getPrototypeOf(Uint8Array.prototype);
          const typedArrayToStringTag = getOwnPropertyDescriptor(typedArrayPrototype, SymbolToStringTag).get;
          const typedArrayLength = getOwnPropertyDescriptor(typedArrayPrototype, 'length').get;
          const arrayBufferByteLength = getOwnPropertyDescriptor(ArrayBuffer.prototype, 'byteLength').get;
          const iteratorPrototype = getPrototypeOf(getPrototypeOf([][SymbolIterator]()));
          const errorStackGetter = getOwnPropertyDescriptor(Error.prototype, 'stack')?.get ?? null;
          const tryCall = (f, v) => { try { return { ok: true, value: apply(f, v, []) }; } catch { return { ok: false }; } };
          const append = (array, value) => apply(arrayPush, array, [value]);

          // ---------------------------------------------------------------------------------------------------------
          // WebIDL helpers (https://webidl.spec.whatwg.org/)
          // ---------------------------------------------------------------------------------------------------------

          // The DOMString and USVString conversions (the lone surrogates of a USVString are replaced by the natives): a
          // template literal throws a TypeError for a symbol, as ToString does
          const toStringValue = (value) => `${value}`;
          const isObject = (value) => (typeof value === 'object' && value !== null) || typeof value === 'function';
          const requireArguments = (count, required, name) => {
            if (count < required) throw new TypeError_(`Failed to execute '${name}': ${required} argument${required > 1 ? 's' : ''} required, but only ${count} present.`);
          };
          const isUint8Array = (value) => isObject(value) && apply(typedArrayToStringTag, value, []) === 'Uint8Array';
          const isBufferSource = (value) => {
            if (!isObject(value)) return false;
            if (apply(typedArrayToStringTag, value, []) !== undefined) return true;
            return natives.isBufferSource(value);
          };

          // The members of an interface are enumerable, and its prototype has a @@toStringTag
          // (https://webidl.spec.whatwg.org/#es-interfaces)
          const defineInterface = (constructor, name) => {
            const prototype = constructor.prototype;
            for (const key of ownKeys(prototype)) {
              if (key === 'constructor') continue;
              const descriptor = getOwnPropertyDescriptor(prototype, key);
              descriptor.enumerable = true;
              defineProperty(prototype, key, descriptor);
            }

            for (const key of getOwnPropertyNames(constructor)) {
              if (key === 'length' || key === 'name' || key === 'prototype') continue;
              const descriptor = getOwnPropertyDescriptor(constructor, key);
              descriptor.enumerable = true;
              defineProperty(constructor, key, descriptor);
            }

            defineProperty(constructor, 'prototype', { writable: false, enumerable: false, configurable: false });
            defineProperty(prototype, SymbolToStringTag, { value: name, writable: false, enumerable: false, configurable: true });
            return constructor;
          };

          // A pair iterator (https://webidl.spec.whatwg.org/#es-iterators): it reads the list at each step
          const createIteratorPrototype = (name, getEntry) => {
            const iteratorTargets = new WeakMap_();
            const prototype = objectCreate(iteratorPrototype, {
              next: {
                value: function next() {
                  const state = isObject(this) ? apply(weakMapGet, iteratorTargets, [this]) : undefined;
                  if (state === undefined) throw new TypeError_(`'next' called on an object that is not a ${name} Iterator`);
                  const entry = getEntry(state.target, state.index);
                  if (entry === undefined) return { value: undefined, done: true };
                  state.index++;
                  const value = state.kind === 'key' ? entry[0] : state.kind === 'value' ? entry[1] : [entry[0], entry[1]];
                  return { value, done: false };
                },
                writable: true, enumerable: true, configurable: true,
              },
              [SymbolToStringTag]: { value: `${name} Iterator`, writable: false, enumerable: false, configurable: true },
            });
            return (target, kind) => {
              const iterator = objectCreate(prototype);
              apply(weakMapSet, iteratorTargets, [iterator, { target, kind, index: 0 }]);
              return iterator;
            };
          };

          const exposed = objectCreate(null);
          const expose = (name, value, enabled) => {
            if (enabled) exposed[name] = value;
          };

          // ---------------------------------------------------------------------------------------------------------
          // DOMException (https://webidl.spec.whatwg.org/#idl-DOMException)
          // ---------------------------------------------------------------------------------------------------------

          const legacyCodes = objectCreate(null);
          const legacyCodeNames = [
            ['INDEX_SIZE_ERR', 'IndexSizeError', 1], ['DOMSTRING_SIZE_ERR', null, 2], ['HIERARCHY_REQUEST_ERR', 'HierarchyRequestError', 3],
            ['WRONG_DOCUMENT_ERR', 'WrongDocumentError', 4], ['INVALID_CHARACTER_ERR', 'InvalidCharacterError', 5], ['NO_DATA_ALLOWED_ERR', null, 6],
            ['NO_MODIFICATION_ALLOWED_ERR', 'NoModificationAllowedError', 7], ['NOT_FOUND_ERR', 'NotFoundError', 8], ['NOT_SUPPORTED_ERR', 'NotSupportedError', 9],
            ['INUSE_ATTRIBUTE_ERR', 'InUseAttributeError', 10], ['INVALID_STATE_ERR', 'InvalidStateError', 11], ['SYNTAX_ERR', 'SyntaxError', 12],
            ['INVALID_MODIFICATION_ERR', 'InvalidModificationError', 13], ['NAMESPACE_ERR', 'NamespaceError', 14], ['INVALID_ACCESS_ERR', 'InvalidAccessError', 15],
            ['VALIDATION_ERR', null, 16], ['TYPE_MISMATCH_ERR', 'TypeMismatchError', 17], ['SECURITY_ERR', 'SecurityError', 18], ['NETWORK_ERR', 'NetworkError', 19],
            ['ABORT_ERR', 'AbortError', 20], ['URL_MISMATCH_ERR', 'URLMismatchError', 21], ['QUOTA_EXCEEDED_ERR', 'QuotaExceededError', 22],
            ['TIMEOUT_ERR', 'TimeoutError', 23], ['INVALID_NODE_TYPE_ERR', 'InvalidNodeTypeError', 24], ['DATA_CLONE_ERR', 'DataCloneError', 25],
          ];
          for (const [, name, code] of legacyCodeNames) {
            if (name !== null) legacyCodes[name] = code;
          }

          const domExceptions = new WeakMap_();
          const getDomException = (value, member) => {
            const state = isObject(value) ? apply(weakMapGet, domExceptions, [value]) : undefined;
            if (state === undefined) throw new TypeError_(`'${member}' called on an object that is not a DOMException`);
            return state;
          };

          class DOMException extends Error {
            constructor(message = undefined, name = undefined) {
              super();
              const state = { message: message === undefined ? '' : toStringValue(message), name: name === undefined ? 'Error' : toStringValue(name) };
              apply(weakMapSet, domExceptions, [this, state]);
              // The stack trace starts with the name and the message, as the one of the other errors
              if (errorStackGetter !== null) {
                const stack = tryCall(errorStackGetter, this);
                if (stack.ok && typeof stack.value === 'string') {
                  const newLine = stack.value.indexOf('\n');
                  const header = state.message === '' ? state.name : `${state.name}: ${state.message}`;
                  natives.setErrorStack(this, newLine < 0 ? header : header + stack.value.slice(newLine));
                }
              }
            }

            get name() { return getDomException(this, 'name').name; }
            get message() { return getDomException(this, 'message').message; }
            get code() { const name = getDomException(this, 'code').name; return apply(hasOwn, legacyCodes, [name]) ? legacyCodes[name] : 0; }
          }

          defineInterface(DOMException, 'DOMException');
          for (const [constant, , code] of legacyCodeNames) {
            defineProperty(DOMException, constant, { value: code, writable: false, enumerable: true, configurable: false });
            defineProperty(DOMException.prototype, constant, { value: code, writable: false, enumerable: true, configurable: false });
          }

          const createDomException = (message, name) => new DOMException(message, name);
          expose('DOMException', DOMException, options.domException);

          // ---------------------------------------------------------------------------------------------------------
          // atob and btoa (https://html.spec.whatwg.org/multipage/webappapis.html#atob)
          // ---------------------------------------------------------------------------------------------------------

          expose('btoa', function btoa(data) {
            requireArguments(arguments.length, 1, 'btoa');
            const result = natives.btoa(toStringValue(data));
            if (result === null) throw createDomException('The string to be encoded contains characters outside of the Latin1 range.', 'InvalidCharacterError');
            return result;
          }, options.base64);
          expose('atob', function atob(data) {
            requireArguments(arguments.length, 1, 'atob');
            const result = natives.atob(toStringValue(data));
            if (result === null) throw createDomException('The string to be decoded is not correctly encoded.', 'InvalidCharacterError');
            return result;
          }, options.base64);

          // ---------------------------------------------------------------------------------------------------------
          // queueMicrotask (https://html.spec.whatwg.org/multipage/timers-and-user-prompts.html#microtask-queuing)
          // ---------------------------------------------------------------------------------------------------------

          expose('queueMicrotask', function queueMicrotask(callback) {
            requireArguments(arguments.length, 1, 'queueMicrotask');
            if (typeof callback !== 'function') throw new TypeError_("Failed to execute 'queueMicrotask': parameter 1 is not of type 'Function'.");
            natives.queueMicrotask(callback);
          }, options.queueMicrotask);

          // ---------------------------------------------------------------------------------------------------------
          // Timers (https://html.spec.whatwg.org/multipage/timers-and-user-prompts.html#timers)
          // ---------------------------------------------------------------------------------------------------------

          expose('setTimeout', function setTimeout(handler, timeout = 0, ...args) {
            requireArguments(arguments.length, 1, 'setTimeout');
            if (typeof handler !== 'function') throw new TypeError_("Failed to execute 'setTimeout': parameter 1 is not of type 'Function'.");
            return natives.setTimer(handler, timeout, args, false);
          }, options.timers);
          expose('setInterval', function setInterval(handler, timeout = 0, ...args) {
            requireArguments(arguments.length, 1, 'setInterval');
            if (typeof handler !== 'function') throw new TypeError_("Failed to execute 'setInterval': parameter 1 is not of type 'Function'.");
            return natives.setTimer(handler, timeout, args, true);
          }, options.timers);
          expose('clearTimeout', function clearTimeout(id = 0) { natives.clearTimer(id); }, options.timers);
          expose('clearInterval', function clearInterval(id = 0) { natives.clearTimer(id); }, options.timers);

          // ---------------------------------------------------------------------------------------------------------
          // structuredClone (https://html.spec.whatwg.org/multipage/structured-data.html#dom-structuredclone)
          // ---------------------------------------------------------------------------------------------------------

          if (options.structuredClone) {
            // The DOMException objects are the only serializable platform objects
            const platformSerialize = (value) => {
              const state = isObject(value) ? apply(weakMapGet, domExceptions, [value]) : undefined;
              return state === undefined ? undefined : { name: state.name, message: state.message };
            };
            const platformDeserialize = (record) => createDomException(record.message, record.name);
            const notTransferable = () => false;
            const notTransferred = () => { throw new TypeError_('The object cannot be transferred'); };
            const dataCloneError = (message) => createDomException(message, 'DataCloneError');
            const helpers = structuredCloneFactory(
              platformSerialize, platformDeserialize, notTransferable, notTransferred, dataCloneError,
              /* crossOriginIsolated */ true, /* scriptPlatformObjects */ new WeakMap_(), /* fetchPlatformObjects */ null, /* cryptoPlatformObjects */ null,
              /* platformDeserializeCryptoKey */ null, natives.hasInternalSlot, natives.setErrorStack, global.SharedArrayBuffer,
              natives.shareBuffer, natives.receiveBuffer);
            const serialize = helpers.serialize;
            const deserialize = helpers.deserialize;
            expose('structuredClone', function structuredClone(value, options = undefined) {
              requireArguments(arguments.length, 1, 'structuredClone');
              const transfer = [];
              if (options !== undefined && options !== null) {
                if (!isObject(options)) throw new TypeError_("Failed to execute 'structuredClone': The provided value is not of type 'StructuredSerializeOptions'.");
                const transferList = options.transfer;
                if (transferList !== undefined) {
                  if (!isObject(transferList) || typeof transferList[SymbolIterator] !== 'function') throw new TypeError_("Failed to execute 'structuredClone': The provided value cannot be converted to a sequence.");
                  for (const item of transferList) {
                    if (!isObject(item)) throw new TypeError_("Failed to execute 'structuredClone': Value is not an object.");
                    append(transfer, item);
                  }
                }
              }

              return deserialize(serialize(value, transfer, false));
            }, true);
          }

          // ---------------------------------------------------------------------------------------------------------
          // URL and URLSearchParams (https://url.spec.whatwg.org/#api)
          // ---------------------------------------------------------------------------------------------------------

          if (options.url) {
            // The handle of the URLSearchParams object of a URL (adopted by the constructor)
            let adoptedHandle = null;

            const createSearchParamsIterator = createIteratorPrototype('URLSearchParams', (target, index) => natives.searchParamsEntry(target, index));

            class URLSearchParams {
              #handle;

              constructor(init = '') {
                if (adoptedHandle !== null) {
                  this.#handle = adoptedHandle;
                  adoptedHandle = null;
                  return;
                }

                if (isObject(init)) {
                  const handle = natives.searchParamsCreate('');
                  const method = init[SymbolIterator];
                  if (method !== undefined && method !== null) {
                    if (typeof method !== 'function') throw new TypeError_("Failed to construct 'URLSearchParams': The object must have a callable @@iterator property.");
                    for (const pair of { [SymbolIterator]: () => apply(method, init, []) }) {
                      if (!isObject(pair) || typeof pair[SymbolIterator] !== 'function') throw new TypeError_("Failed to construct 'URLSearchParams': The provided value cannot be converted to a sequence.");
                      const items = [];
                      for (const item of pair) append(items, toStringValue(item));
                      if (items.length !== 2) throw new TypeError_("Failed to construct 'URLSearchParams': Sequence initializer must only contain pair elements");
                      natives.searchParamsAppendInitial(handle, items[0], items[1]);
                    }
                  } else {
                    for (const key of ownKeys(init)) {
                      const descriptor = getOwnPropertyDescriptor(init, key);
                      if (descriptor === undefined || !descriptor.enumerable) continue;
                      natives.searchParamsAppendInitial(handle, toStringValue(key), toStringValue(init[key]));
                    }
                  }

                  this.#handle = handle;
                } else {
                  this.#handle = natives.searchParamsCreate(toStringValue(init));
                }
              }

              get size() { return natives.searchParamsSize(this.#handle); }

              append(name, value) {
                const handle = this.#handle;
                requireArguments(arguments.length, 2, 'append');
                natives.searchParamsAppend(handle, toStringValue(name), toStringValue(value));
              }

              delete(name, value = undefined) {
                const handle = this.#handle;
                requireArguments(arguments.length, 1, 'delete');
                natives.searchParamsDelete(handle, toStringValue(name), value === undefined ? undefined : toStringValue(value));
              }

              get(name) {
                const handle = this.#handle;
                requireArguments(arguments.length, 1, 'get');
                return natives.searchParamsGet(handle, toStringValue(name));
              }

              getAll(name) {
                const handle = this.#handle;
                requireArguments(arguments.length, 1, 'getAll');
                return natives.searchParamsGetAll(handle, toStringValue(name));
              }

              has(name, value = undefined) {
                const handle = this.#handle;
                requireArguments(arguments.length, 1, 'has');
                return natives.searchParamsHas(handle, toStringValue(name), value === undefined ? undefined : toStringValue(value));
              }

              set(name, value) {
                const handle = this.#handle;
                requireArguments(arguments.length, 2, 'set');
                natives.searchParamsSet(handle, toStringValue(name), toStringValue(value));
              }

              sort() { natives.searchParamsSort(this.#handle); }

              entries() { return createSearchParamsIterator(this.#handle, 'key+value'); }

              keys() { return createSearchParamsIterator(this.#handle, 'key'); }

              values() { return createSearchParamsIterator(this.#handle, 'value'); }

              forEach(callback, thisArg = undefined) {
                const handle = this.#handle;
                requireArguments(arguments.length, 1, 'forEach');
                if (typeof callback !== 'function') throw new TypeError_("Failed to execute 'forEach' on 'URLSearchParams': parameter 1 is not of type 'Function'.");
                for (let index = 0; ; index++) {
                  const entry = natives.searchParamsEntry(handle, index);
                  if (entry === undefined) break;
                  apply(callback, thisArg, [entry[1], entry[0], this]);
                }
              }

              toString() { return natives.searchParamsToString(this.#handle); }
            }

            defineInterface(URLSearchParams, 'URLSearchParams');
            defineProperty(URLSearchParams.prototype, SymbolIterator, { value: URLSearchParams.prototype.entries, writable: true, enumerable: false, configurable: true });

            const urlSearchParams = new WeakMap_();

            class URL {
              #handle;

              constructor(url, base = undefined) {
                requireArguments(arguments.length, 1, 'URL');
                url = toStringValue(url);
                const handle = natives.urlParse(url, base === undefined ? undefined : toStringValue(base));
                if (handle === null) throw new TypeError_(`Failed to construct 'URL': Invalid URL '${url}'`);
                this.#handle = handle;
              }

              static parse(url, base = undefined) {
                requireArguments(arguments.length, 1, 'parse');
                url = toStringValue(url);
                base = base === undefined ? undefined : toStringValue(base);
                return natives.urlParse(url, base) === null ? null : new URL(url, base);
              }

              static canParse(url, base = undefined) {
                requireArguments(arguments.length, 1, 'canParse');
                url = toStringValue(url);
                return natives.urlParse(url, base === undefined ? undefined : toStringValue(base)) !== null;
              }

              get href() { return natives.urlGet(this.#handle, 'href'); }
              set href(value) {
                const handle = this.#handle;
                value = toStringValue(value);
                if (!natives.urlSet(handle, 'href', value)) throw new TypeError_(`Failed to set the 'href' property on 'URL': Invalid URL '${value}'`);
              }

              get origin() { return natives.urlGet(this.#handle, 'origin'); }
              get protocol() { return natives.urlGet(this.#handle, 'protocol'); }
              set protocol(value) { const handle = this.#handle; natives.urlSet(handle, 'protocol', toStringValue(value)); }
              get username() { return natives.urlGet(this.#handle, 'username'); }
              set username(value) { const handle = this.#handle; natives.urlSet(handle, 'username', toStringValue(value)); }
              get password() { return natives.urlGet(this.#handle, 'password'); }
              set password(value) { const handle = this.#handle; natives.urlSet(handle, 'password', toStringValue(value)); }
              get host() { return natives.urlGet(this.#handle, 'host'); }
              set host(value) { const handle = this.#handle; natives.urlSet(handle, 'host', toStringValue(value)); }
              get hostname() { return natives.urlGet(this.#handle, 'hostname'); }
              set hostname(value) { const handle = this.#handle; natives.urlSet(handle, 'hostname', toStringValue(value)); }
              get port() { return natives.urlGet(this.#handle, 'port'); }
              set port(value) { const handle = this.#handle; natives.urlSet(handle, 'port', toStringValue(value)); }
              get pathname() { return natives.urlGet(this.#handle, 'pathname'); }
              set pathname(value) { const handle = this.#handle; natives.urlSet(handle, 'pathname', toStringValue(value)); }
              get search() { return natives.urlGet(this.#handle, 'search'); }
              set search(value) { const handle = this.#handle; natives.urlSet(handle, 'search', toStringValue(value)); }

              get searchParams() {
                const handle = this.#handle;
                let searchParams = apply(weakMapGet, urlSearchParams, [this]);
                if (searchParams === undefined) {
                  adoptedHandle = natives.urlSearchParams(handle);
                  try { searchParams = new URLSearchParams(); } finally { adoptedHandle = null; }
                  apply(weakMapSet, urlSearchParams, [this, searchParams]);
                }

                return searchParams;
              }

              get hash() { return natives.urlGet(this.#handle, 'hash'); }
              set hash(value) { const handle = this.#handle; natives.urlSet(handle, 'hash', toStringValue(value)); }

              toJSON() { return natives.urlGet(this.#handle, 'href'); }

              toString() { return natives.urlGet(this.#handle, 'href'); }
            }

            defineInterface(URL, 'URL');
            expose('URL', URL, true);
            expose('URLSearchParams', URLSearchParams, true);
          }

          // ---------------------------------------------------------------------------------------------------------
          // TextEncoder and TextDecoder (https://encoding.spec.whatwg.org/#api)
          // ---------------------------------------------------------------------------------------------------------

          if (options.encoding) {
            class TextEncoder {
              #brand = true;

              get encoding() { this.#brand; return 'utf-8'; }

              encode(input = '') {
                this.#brand;
                return natives.encode(toStringValue(input));
              }

              encodeInto(source, destination) {
                this.#brand;
                requireArguments(arguments.length, 2, 'encodeInto');
                source = toStringValue(source);
                if (!isUint8Array(destination)) throw new TypeError_("Failed to execute 'encodeInto' on 'TextEncoder': parameter 2 is not of type 'Uint8Array'.");
                return natives.encodeInto(source, destination);
              }
            }

            defineInterface(TextEncoder, 'TextEncoder');

            class TextDecoder {
              #handle;

              constructor(label = 'utf-8', options = undefined) {
                label = toStringValue(label);
                if (options !== undefined && options !== null && !isObject(options)) throw new TypeError_("Failed to construct 'TextDecoder': The provided value is not of type 'TextDecoderOptions'.");
                const fatal = options === undefined || options === null ? false : !!options.fatal;
                const ignoreBOM = options === undefined || options === null ? false : !!options.ignoreBOM;
                const handle = natives.decoderCreate(label, fatal, ignoreBOM);
                if (handle === null) throw new RangeError_(`Failed to construct 'TextDecoder': The encoding label provided ('${label}') is invalid.`);
                this.#handle = handle;
              }

              get encoding() { return natives.decoderGet(this.#handle, 'encoding'); }
              get fatal() { return natives.decoderGet(this.#handle, 'fatal'); }
              get ignoreBOM() { return natives.decoderGet(this.#handle, 'ignoreBOM'); }

              decode(input = undefined, options = undefined) {
                const handle = this.#handle;
                if (input !== undefined && !isBufferSource(input)) throw new TypeError_("Failed to execute 'decode' on 'TextDecoder': The provided value is not of type '(ArrayBuffer or ArrayBufferView)'.");
                if (options !== undefined && options !== null && !isObject(options)) throw new TypeError_("Failed to execute 'decode' on 'TextDecoder': The provided value is not of type 'TextDecodeOptions'.");
                const stream = options === undefined || options === null ? false : !!options.stream;
                const result = natives.decode(handle, input, stream);
                if (result === null) throw new TypeError_("Failed to execute 'decode' on 'TextDecoder': The encoded data was not valid.");
                return result;
              }
            }

            defineInterface(TextDecoder, 'TextDecoder');
            expose('TextEncoder', TextEncoder, true);
            expose('TextDecoder', TextDecoder, true);
          }

          // ---------------------------------------------------------------------------------------------------------
          // console (https://console.spec.whatwg.org/)
          // ---------------------------------------------------------------------------------------------------------

          if (options.console) {
            const maxDepth = 2;
            const maxItems = 100;
            const quote = (value) => `'${apply(stringReplace, apply(stringReplace, value, [/[\\']/g, '\\$&']), [/\n/g, '\\n'])}'`;
            const formatKey = (key) => typeof key === 'symbol' ? `[${apply(symbolToString, key, [])}]` : /^[A-Za-z_$][\w$]*$/.test(key) ? key : quote(key);

            const describeFunction = (value) => {
              const source = tryCall(functionToString, value);
              const isClass = source.ok && /^class[\s{]/.test(source.value);
              const nameDescriptor = getOwnPropertyDescriptor(value, 'name');
              const name = nameDescriptor !== undefined && typeof nameDescriptor.value === 'string' ? nameDescriptor.value : '';
              if (isClass) return name === '' ? '[class (anonymous)]' : `[class ${name}]`;
              return name === '' ? '[Function (anonymous)]' : `[Function: ${name}]`;
            };

            // A representation of a value, as the browsers and Node.js show it: the accessors are not called
            const inspect = (value, depth, seen, quoteStrings) => {
              switch (typeof value) {
                case 'string': return quoteStrings ? quote(value) : value;
                case 'number': return objectIs(value, -0) ? '-0' : `${value}`;
                case 'bigint': return `${value}n`;
                case 'symbol': return apply(symbolToString, value, []);
                case 'undefined': return 'undefined';
                case 'boolean': return `${value}`;
                case 'function': return describeFunction(value);
              }

              if (value === null) return 'null';
              if (apply(weakSetHas, seen, [value])) return '[Circular]';
              try {
                apply(weakSetAdd, seen, [value]);
                return inspectObject(value, depth, seen);
              } catch {
                return '[object]';
              } finally {
                apply(weakSetDelete, seen, [value]);
              }
            };

            const inspectProperties = (value, depth, seen, items, skipIndices) => {
              for (const key of ownKeys(value)) {
                if (skipIndices && typeof key === 'string' && /^(0|[1-9]\d*)$/.test(key)) continue;
                const descriptor = getOwnPropertyDescriptor(value, key);
                if (descriptor === undefined || !descriptor.enumerable) continue;
                if (items.length >= maxItems) { append(items, '...'); break; }
                let text;
                if ('value' in descriptor) text = inspect(descriptor.value, depth + 1, seen, true);
                else text = descriptor.get !== undefined ? (descriptor.set !== undefined ? '[Getter/Setter]' : '[Getter]') : '[Setter]';
                append(items, `${formatKey(key)}: ${text}`);
              }
            };

            const wrap = (prefix, items, open, close) => {
              const body = apply(arrayJoin, items, [', ']);
              if (items.length === 0) return `${prefix}${open}${close}`;
              return `${prefix}${open} ${body} ${close}`;
            };

            const inspectObject = (value, depth, seen) => {
              const errorState = apply(weakMapGet, domExceptions, [value]);
              if (errorState !== undefined || apply(objectToString, value, []) === '[object Error]') {
                const stack = errorStackGetter === null ? { ok: false } : tryCall(errorStackGetter, value);
                if (stack.ok && typeof stack.value === 'string' && stack.value !== '') return stack.value;
                const name = errorState?.name ?? value.name;
                const message = errorState?.message ?? value.message;
                return message === undefined || message === '' ? `${name}` : `${name}: ${message}`;
              }

              const time = tryCall(dateGetTime, value);
              if (time.ok) return time.value !== time.value ? 'Invalid Date' : apply(dateToISOString, value, []);
              const regExp = tryCall(regExpToString, value);
              if (regExp.ok && apply(objectToString, value, []) === '[object RegExp]') return regExp.value;

              const tag = apply(typedArrayToStringTag, value, []);
              const items = [];
              if (tag !== undefined) {
                if (depth > maxDepth) return `[${tag}]`;
                const length = apply(typedArrayLength, value, []);
                for (let i = 0; i < length && i < maxItems; i++) append(items, inspect(value[i], depth + 1, seen, true));
                if (length > maxItems) append(items, `... ${length - maxItems} more items`);
                return wrap(`${tag}(${length}) `, items, '[', ']');
              }

              const size = tryCall(mapSize, value);
              if (size.ok) {
                if (depth > maxDepth) return '[Map]';
                for (const [k, v] of apply(mapEntries, value, [])) {
                  if (items.length >= maxItems) { append(items, '...'); break; }
                  append(items, `${inspect(k, depth + 1, seen, true)} => ${inspect(v, depth + 1, seen, true)}`);
                }
                return wrap(`Map(${size.value}) `, items, '{', '}');
              }

              const setSizeResult = tryCall(setSize, value);
              if (setSizeResult.ok) {
                if (depth > maxDepth) return '[Set]';
                for (const v of apply(setValues, value, [])) {
                  if (items.length >= maxItems) { append(items, '...'); break; }
                  append(items, inspect(v, depth + 1, seen, true));
                }
                return wrap(`Set(${setSizeResult.value}) `, items, '{', '}');
              }

              const byteLength = tryCall(arrayBufferByteLength, value);
              if (byteLength.ok) return `ArrayBuffer { byteLength: ${byteLength.value} }`;

              if (isArray(value)) {
                if (depth > maxDepth) return '[Array]';
                const length = value.length;
                for (let i = 0; i < length && i < maxItems; i++) {
                  const descriptor = getOwnPropertyDescriptor(value, `${i}`);
                  if (descriptor === undefined) append(items, '<empty item>');
                  else append(items, 'value' in descriptor ? inspect(descriptor.value, depth + 1, seen, true) : '[Getter]');
                }
                if (length > maxItems) append(items, `... ${length - maxItems} more items`);
                inspectProperties(value, depth, seen, items, true);
                return wrap('', items, '[', ']');
              }

              const prototype = getPrototypeOf(value);
              let prefix = '';
              if (prototype === null) prefix = '[Object: null prototype] ';
              else {
                const constructor = getOwnPropertyDescriptor(prototype, 'constructor')?.value;
                const name = typeof constructor === 'function' ? getOwnPropertyDescriptor(constructor, 'name')?.value : undefined;
                const toStringTag = getOwnPropertyDescriptor(prototype, SymbolToStringTag)?.value;
                if (typeof toStringTag === 'string' && toStringTag !== '' && toStringTag !== name) prefix = `${name ?? 'Object'} [${toStringTag}] `;
                else if (typeof name === 'string' && name !== '' && name !== 'Object') prefix = `${name} `;
              }

              if (depth > maxDepth) return prefix === '' ? '[Object]' : `[${prefix.trim()}]`;
              inspectProperties(value, depth, seen, items, false);
              return wrap(prefix, items, '{', '}');
            };

            const inspectValue = (value, quoteStrings) => inspect(value, 0, new WeakSet_(), quoteStrings);

            // https://console.spec.whatwg.org/#formatter
            const format = (args) => {
              if (args.length === 0) return '';
              const parts = [];
              let index = 0;
              if (typeof args[0] === 'string' && args.length > 1) {
                const target = args[0];
                let text = '';
                let next = 1;
                for (let i = 0; i < target.length; i++) {
                  const c = target[i];
                  if (c !== '%' || i + 1 >= target.length) { text += c; continue; }
                  const specifier = target[i + 1];
                  if (specifier === '%') { text += '%'; i++; continue; }
                  if ('sdifoOc'.indexOf(specifier) < 0 || next >= args.length) { text += c; continue; }
                  const element = args[next++];
                  i++;
                  switch (specifier) {
                    case 's': text += typeof element === 'string' ? element : typeof element === 'symbol' ? apply(symbolToString, element, []) : typeof element === 'bigint' ? `${element}n` : isObject(element) ? inspectValue(element, false) : `${element}`; break;
                    case 'd':
                    case 'i': text += typeof element === 'symbol' ? `${NaN_}` : typeof element === 'bigint' ? `${element}n` : `${parseInt_(element, 10)}`; break;
                    case 'f': text += typeof element === 'symbol' ? `${NaN_}` : `${parseFloat_(element)}`; break;
                    case 'o':
                    case 'O': text += inspectValue(element, true); break;
                    case 'c': break;
                  }
                }
                append(parts, text);
                index = next;
              }

              for (; index < args.length; index++) {
                const value = args[index];
                append(parts, typeof value === 'string' ? value : inspectValue(value, true));
              }

              return apply(arrayJoin, parts, [' ']);
            };

            let groupDepth = 0;
            const counts = new Map_();
            const timers = new Map_();
            const print = (method, level, text) => natives.print(method, level, text, groupDepth);
            const logger = (method, level, args) => print(method, level, format(args));
            const formatDuration = (start) => {
              const duration = natives.now() - start;
              return `${+duration.toFixed(3)}ms`;
            };

            const console = objectCreate(Object.prototype);
            const methods = {
              log(...data) { logger('log', 'log', data); },
              info(...data) { logger('info', 'info', data); },
              warn(...data) { logger('warn', 'warn', data); },
              error(...data) { logger('error', 'error', data); },
              debug(...data) { logger('debug', 'debug', data); },
              trace(...data) {
                const text = format(data);
                const stack = natives.stackTrace();
                print('trace', 'log', stack === '' ? (text === '' ? 'Trace' : `Trace: ${text}`) : `${text === '' ? 'Trace' : `Trace: ${text}`}\n${stack}`);
              },
              assert(condition = false, ...data) {
                if (condition) return;
                const message = 'Assertion failed';
                if (data.length === 0) data = [message];
                else if (typeof data[0] === 'string') data[0] = `${message}: ${data[0]}`;
                else data = [message, ...data];
                logger('assert', 'error', data);
              },
              clear() { groupDepth = 0; },
              count(label = 'default') {
                label = toStringValue(label);
                const count = (apply(mapGet, counts, [label]) ?? 0) + 1;
                apply(mapSet, counts, [label, count]);
                print('count', 'log', `${label}: ${count}`);
              },
              countReset(label = 'default') {
                label = toStringValue(label);
                if (apply(mapHas, counts, [label])) apply(mapSet, counts, [label, 0]);
                else print('countReset', 'warn', `Count for '${label}' does not exist`);
              },
              group(...data) {
                if (data.length > 0) logger('group', 'log', data);
                groupDepth++;
              },
              groupCollapsed(...data) {
                if (data.length > 0) logger('groupCollapsed', 'log', data);
                groupDepth++;
              },
              groupEnd() { if (groupDepth > 0) groupDepth--; },
              table(tabularData = undefined, properties = undefined) { logger('table', 'log', arguments.length === 0 ? [] : [tabularData]); },
              dir(item = undefined, options = undefined) { print('dir', 'log', inspectValue(item, true)); },
              dirxml(...data) { logger('dirxml', 'log', data); },
              time(label = 'default') {
                label = toStringValue(label);
                if (apply(mapHas, timers, [label])) { print('time', 'warn', `Timer '${label}' already exists`); return; }
                apply(mapSet, timers, [label, natives.now()]);
              },
              timeLog(label = 'default', ...data) {
                label = toStringValue(label);
                if (!apply(mapHas, timers, [label])) { print('timeLog', 'warn', `Timer '${label}' does not exist`); return; }
                const text = `${label}: ${formatDuration(apply(mapGet, timers, [label]))}`;
                print('timeLog', 'log', data.length === 0 ? text : `${text} ${format(data)}`);
              },
              timeEnd(label = 'default') {
                label = toStringValue(label);
                if (!apply(mapHas, timers, [label])) { print('timeEnd', 'warn', `Timer '${label}' does not exist`); return; }
                const start = apply(mapGet, timers, [label]);
                apply(mapDelete, timers, [label]);
                print('timeEnd', 'log', `${label}: ${formatDuration(start)}`);
              },
            };
            for (const name of objectKeys(methods)) {
              defineProperty(console, name, { value: methods[name], writable: true, enumerable: true, configurable: true });
            }

            defineProperty(console, SymbolToStringTag, { value: 'console', writable: false, enumerable: false, configurable: true });
            expose('console', console, true);
          }

          // The interfaces and the namespaces are not enumerable; the operations of the global object are
          // (https://webidl.spec.whatwg.org/#es-operations)
          for (const name of objectKeys(exposed)) {
            const value = exposed[name];
            const isInterfaceOrNamespace = name === 'console' || /^[A-Z]/.test(name);
            defineProperty(global, name, { value, writable: true, enumerable: !isInterfaceOrNamespace, configurable: true });
          }
        })
        """;
}
