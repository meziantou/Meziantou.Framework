// The Web Cryptography API (https://w3c.github.io/webcrypto/), the secure curves (https://wicg.github.io/webcrypto-secure-curves/)
// and the modern algorithms (https://wicg.github.io/webcrypto-modern-algos/), implemented in JavaScript: the WebIDL layer
// (the conversions, the algorithm normalization, which converts a dictionary to a type that depends on the algorithm name,
// the cached objects of CryptoKey, the promises and their tasks) and the steps of the algorithms. The cryptographic
// primitives and the ASN.1 structures are implemented in .NET and given by the hooks of the host.
//
// The script is evaluated once per realm, when crypto or one of the interfaces is first used. It is a function called with
// the hooks of the host, and returns the interface objects, the Crypto object of the global object, and the functions used
// by the structured serialization. The internal slots of the objects are stored in records (objects without prototype)
// associated with the objects by a WeakMap shared by the realms of the agent, so that the keys can be used in any realm.
(function (hooks) {
  'use strict';

  // ------------------------------------------------------------------------------------------------------------------
  // Intrinsics
  // ------------------------------------------------------------------------------------------------------------------

  const apply = Reflect.apply;
  const ObjectCreate = Object.create;
  const defineProperty = Object.defineProperty;
  const getOwnPropertyDescriptor = Object.getOwnPropertyDescriptor;
  const getPrototypeOf = Object.getPrototypeOf;
  const ownKeys = Reflect.ownKeys;
  const getter = (o, k) => getOwnPropertyDescriptor(o, k).get;
  const TypeError_ = TypeError;
  const Promise_ = Promise;
  const weakMapGet = WeakMap.prototype.get;
  const weakMapSet = WeakMap.prototype.set;
  const SymbolIterator = Symbol.iterator;
  const SymbolToStringTag = Symbol.toStringTag;
  const ArrayBuffer_ = ArrayBuffer;
  const Array_ = Array;
  const Uint8Array_ = Uint8Array;
  const arrayBufferByteLength = getter(ArrayBuffer.prototype, 'byteLength');
  // %SharedArrayBuffer% (the global object has no SharedArrayBuffer property when the agent cluster is not cross-origin isolated)
  const sharedArrayBufferByteLength = typeof hooks.SharedArrayBuffer === 'function' ? getter(hooks.SharedArrayBuffer.prototype, 'byteLength') : null;
  const typedArrayPrototype = getPrototypeOf(Uint8Array.prototype);
  const typedArrayName = getter(typedArrayPrototype, SymbolToStringTag);
  const typedArrayBuffer = getter(typedArrayPrototype, 'buffer');
  const typedArrayByteOffset = getter(typedArrayPrototype, 'byteOffset');
  const typedArrayByteLength = getter(typedArrayPrototype, 'byteLength');
  const typedArraySet = typedArrayPrototype.set;
  const typedArraySlice = typedArrayPrototype.slice;
  const dataViewBuffer = getter(DataView.prototype, 'buffer');
  const dataViewByteOffset = getter(DataView.prototype, 'byteOffset');
  const dataViewByteLength = getter(DataView.prototype, 'byteLength');
  const arrayPush = Array.prototype.push;
  const arrayIncludes = Array.prototype.includes;
  const JSONParse = JSON.parse;
  const JSONStringify = JSON.stringify;
  const NumberIsNaN = Number.isNaN;
  const MathTrunc = Math.trunc;
  const MathFloor = Math.floor;
  const StringFromCharCode = String.fromCharCode;
  const stringCharCodeAt = String.prototype.charCodeAt;

  const { DOMException: DOMException_, QuotaExceededError: QuotaExceededError_ } = hooks.interfaces;
  const internals = hooks.internals;
  const isSecureContext = hooks.isSecureContext;
  const isObject = (value) => (typeof value === 'object' && value !== null) || typeof value === 'function';

  // ------------------------------------------------------------------------------------------------------------------
  // Records and interfaces
  // ------------------------------------------------------------------------------------------------------------------

  // The functions of this realm used by the records (e.g. the cached objects of a key are created in its realm)
  const realm = ObjectCreate(null);

  function createRecord(object, kind) {
    const record = ObjectCreate(null);
    record.kind = kind;
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
        const target = key[0] === '@' ? constructor[key.substring(1)] : prototype[key];
        defineProperty(target, 'length', { value: lengths[key], writable: false, enumerable: false, configurable: true });
      }
    }

    defineProperty(prototype, SymbolToStringTag, { value: name, writable: false, enumerable: false, configurable: true });
  }

  // Removes the [SecureContext] members of an interface (https://webidl.spec.whatwg.org/#SecureContext)
  function removeMembers(target, names) {
    for (const name of names) delete target[name];
  }

  const illegalConstructor = () => new TypeError_('Illegal constructor');

  // ------------------------------------------------------------------------------------------------------------------
  // Errors
  // ------------------------------------------------------------------------------------------------------------------

  const domException = (name, message) => new DOMException_(message, name);
  const notSupportedError = (message) => domException('NotSupportedError', message);
  const syntaxError = (message) => domException('SyntaxError', message);
  const invalidAccessError = (message) => domException('InvalidAccessError', message);
  const dataError = (message) => domException('DataError', message);
  const operationError = (message) => domException('OperationError', message);
  const typeError = (message) => new TypeError_(message);

  function requireArguments(count, required, operation) {
    if (count < required) throw typeError(`Failed to execute '${operation}' on 'SubtleCrypto': ${required} arguments required, but only ${count} present.`);
  }

  // ------------------------------------------------------------------------------------------------------------------
  // Bytes
  // ------------------------------------------------------------------------------------------------------------------

  function isArrayBuffer(value) {
    try { apply(arrayBufferByteLength, value, []); return true; } catch { return false; }
  }

  function isSharedArrayBuffer(value) {
    if (sharedArrayBufferByteLength === null) return false;
    try { apply(sharedArrayBufferByteLength, value, []); return true; } catch { return false; }
  }

  function isDataView(value) {
    try { apply(dataViewBuffer, value, []); return true; } catch { return false; }
  }

  const getTypedArrayName = (value) => isObject(value) ? apply(typedArrayName, value, []) : undefined;
  const isBufferSource = (value) => isObject(value) && (isArrayBuffer(value) || getTypedArrayName(value) !== undefined || isDataView(value));

  // https://webidl.spec.whatwg.org/#js-buffer-source-types: BufferSource is not [AllowShared]. The value is kept as
  // a reference: the bytes are copied when the algorithm says so.
  function convertBufferSource(value, what) {
    if (isObject(value)) {
      if (isArrayBuffer(value)) return value;
      let buffer;
      if (getTypedArrayName(value) !== undefined) buffer = apply(typedArrayBuffer, value, []);
      else if (isDataView(value)) buffer = apply(dataViewBuffer, value, []);
      if (buffer !== undefined) {
        if (isSharedArrayBuffer(buffer)) throw typeError(`The ${what} cannot view a SharedArrayBuffer`);
        return value;
      }
    }

    throw typeError(`The ${what} is not of type '(ArrayBuffer or ArrayBufferView)'`);
  }

  // https://webidl.spec.whatwg.org/#dfn-get-buffer-source-copy (a detached buffer has no bytes)
  function copyBytes(source) {
    if (isArrayBuffer(source)) {
      const length = apply(arrayBufferByteLength, source, []);
      return length === 0 ? new Uint8Array_(0) : new Uint8Array_(source).slice();
    }

    // The view of a detached buffer has no bytes (some engines throw when its offset is read)
    let buffer, offset, length;
    try {
      if (getTypedArrayName(source) !== undefined) {
        buffer = apply(typedArrayBuffer, source, []);
        offset = apply(typedArrayByteOffset, source, []);
        length = apply(typedArrayByteLength, source, []);
      } else {
        buffer = apply(dataViewBuffer, source, []);
        offset = apply(dataViewByteOffset, source, []);
        length = apply(dataViewByteLength, source, []);
      }
    } catch {
      return new Uint8Array_(0);
    }

    if (length === 0) return new Uint8Array_(0);
    return apply(typedArraySlice, new Uint8Array_(buffer, offset, length), []);
  }

  function bytesEqual(a, b) {
    if (a.length !== b.length) return false;
    let diff = 0;
    for (let i = 0; i < a.length; i++) diff |= a[i] ^ b[i];
    return diff === 0;
  }

  const sliceBytes = (bytes, start, end) => apply(typedArraySlice, bytes, [start, end]);

  // An ArrayBuffer of this realm containing the bytes
  function toArrayBuffer(bytes) {
    const buffer = new ArrayBuffer_(bytes.length);
    apply(typedArraySet, new Uint8Array_(buffer), [bytes]);
    return buffer;
  }

  // https://datatracker.ietf.org/doc/html/rfc7515#appendix-C: base64url without padding
  const base64UrlAlphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_';
  const base64UrlValues = ObjectCreate(null);
  for (let i = 0; i < 64; i++) base64UrlValues[base64UrlAlphabet[i]] = i;

  function base64UrlEncode(bytes) {
    let result = '';
    let i = 0;
    for (; i + 2 < bytes.length; i += 3) {
      const n = (bytes[i] << 16) | (bytes[i + 1] << 8) | bytes[i + 2];
      result += base64UrlAlphabet[n >> 18] + base64UrlAlphabet[(n >> 12) & 63] + base64UrlAlphabet[(n >> 6) & 63] + base64UrlAlphabet[n & 63];
    }

    if (i + 1 === bytes.length) {
      const n = bytes[i] << 16;
      result += base64UrlAlphabet[n >> 18] + base64UrlAlphabet[(n >> 12) & 63];
    } else if (i + 2 === bytes.length) {
      const n = (bytes[i] << 16) | (bytes[i + 1] << 8);
      result += base64UrlAlphabet[n >> 18] + base64UrlAlphabet[(n >> 12) & 63] + base64UrlAlphabet[(n >> 6) & 63];
    }

    return result;
  }

  // Returns undefined when the string is not base64url (the padding characters are not allowed)
  function base64UrlDecode(string) {
    if (string.length % 4 === 1) return undefined;
    const result = new Uint8Array_(MathFloor(string.length * 3 / 4));
    let buffer = 0, bits = 0, offset = 0;
    for (let i = 0; i < string.length; i++) {
      const value = base64UrlValues[string[i]];
      if (value === undefined) return undefined;
      buffer = (buffer << 6) | value;
      bits += 6;
      if (bits >= 8) {
        bits -= 8;
        result[offset++] = (buffer >> bits) & 0xFF;
      }
    }

    // The unused bits must be zero (a canonical encoding)
    if ((buffer & ((1 << bits) - 1)) !== 0) return undefined;
    return result;
  }

  function utf8Encode(string) {
    const bytes = [];
    for (let i = 0; i < string.length; i++) {
      let c = apply(stringCharCodeAt, string, [i]);
      if (c >= 0xD800 && c <= 0xDBFF && i + 1 < string.length) {
        const d = apply(stringCharCodeAt, string, [i + 1]);
        if (d >= 0xDC00 && d <= 0xDFFF) {
          c = 0x10000 + ((c - 0xD800) << 10) + (d - 0xDC00);
          i++;
        }
      }

      if (c >= 0xD800 && c <= 0xDFFF) c = 0xFFFD;
      if (c < 0x80) apply(arrayPush, bytes, [c]);
      else if (c < 0x800) apply(arrayPush, bytes, [0xC0 | (c >> 6), 0x80 | (c & 63)]);
      else if (c < 0x10000) apply(arrayPush, bytes, [0xE0 | (c >> 12), 0x80 | ((c >> 6) & 63), 0x80 | (c & 63)]);
      else apply(arrayPush, bytes, [0xF0 | (c >> 18), 0x80 | ((c >> 12) & 63), 0x80 | ((c >> 6) & 63), 0x80 | (c & 63)]);
    }

    return new Uint8Array_(bytes);
  }

  // ------------------------------------------------------------------------------------------------------------------
  // WebIDL conversions
  // ------------------------------------------------------------------------------------------------------------------

  const convertDOMString = (value) => {
    if (typeof value === 'symbol') throw typeError('Cannot convert a Symbol value to a string');
    return `${value}`;
  };

  const convertBoolean = (value) => !!value;

  // https://webidl.spec.whatwg.org/#abstract-opdef-converttoint with [EnforceRange]
  function convertEnforceRange(value, max, what) {
    if (typeof value === 'symbol' || typeof value === 'bigint') throw typeError(`The ${what} is not a number`);
    let x = +value;
    if (NumberIsNaN(x) || x === Infinity || x === -Infinity) throw typeError(`The ${what} is not a finite number`);
    x = MathTrunc(x);
    if (x === 0) x = 0;
    if (x < 0 || x > max) throw typeError(`The ${what} is outside of the range of the type`);
    return x;
  }

  // (object or DOMString)
  const convertAlgorithmIdentifier = (value) => isObject(value) ? value : convertDOMString(value);

  function convertCryptoKey(value, what) {
    const record = getRecord(value, 'CryptoKey');
    if (record === undefined) throw typeError(`The ${what} is not of type 'CryptoKey'`);
    return record;
  }

  // https://webidl.spec.whatwg.org/#js-sequence
  function convertSequence(value, convertItem, what) {
    if (!isObject(value)) throw typeError(`The ${what} is not a sequence`);
    const method = value[SymbolIterator];
    if (typeof method !== 'function') throw typeError(`The ${what} is not iterable`);
    const iterator = apply(method, value, []);
    if (!isObject(iterator)) throw typeError('The iterator is not an object');
    const next = iterator.next;
    const result = [];
    for (;;) {
      const step = apply(next, iterator, []);
      if (!isObject(step)) throw typeError('The iterator result is not an object');
      if (step.done) return result;
      apply(arrayPush, result, [convertItem(step.value)]);
    }
  }

  function convertEnum(value, values, name) {
    const string = convertDOMString(value);
    if (!apply(arrayIncludes, values, [string])) throw typeError(`The provided value '${string}' is not a valid enum value of type ${name}`);
    return string;
  }

  // https://wicg.github.io/webcrypto-modern-algos/#subtlecrypto-interface-keyformat
  const keyFormats = ['raw-public', 'raw-private', 'raw-seed', 'raw-secret', 'raw', 'spki', 'pkcs8', 'jwk'];
  // https://wicg.github.io/webcrypto-modern-algos/#subtlecrypto-interface-keyusage, in the order of the recognized key usage values
  const keyUsages = ['encrypt', 'decrypt', 'sign', 'verify', 'deriveKey', 'deriveBits', 'wrapKey', 'unwrapKey', 'encapsulateKey', 'encapsulateBits', 'decapsulateKey', 'decapsulateBits'];
  const convertKeyFormat = (value) => convertEnum(value, keyFormats, 'KeyFormat');
  const convertKeyUsages = (value) => convertSequence(value, (item) => convertEnum(item, keyUsages, 'KeyUsage'), 'key usages');

  // The dictionary types (https://webidl.spec.whatwg.org/#js-dictionary): the members of each dictionary, in lexicographical
  // order, and its inherited dictionary
  const dictionaries = ObjectCreate(null);

  function defineDictionary(name, parent, members) {
    const sorted = [...members];
    sorted.sort((a, b) => a[0] < b[0] ? -1 : a[0] > b[0] ? 1 : 0);
    const dictionary = ObjectCreate(null);
    dictionary.name = name;
    dictionary.parent = parent === null ? null : dictionaries[parent];
    dictionary.members = sorted;
    dictionaries[name] = dictionary;
  }

  function convertMember(value, type, what) {
    switch (type) {
      case 'DOMString': return convertDOMString(value);
      case 'boolean': return convertBoolean(value);
      case 'octet': return convertEnforceRange(value, 0xFF, what);
      case 'unsigned short': return convertEnforceRange(value, 0xFFFF, what);
      case 'unsigned long': return convertEnforceRange(value, 0xFFFFFFFF, what);
      case 'BufferSource': return convertBufferSource(value, what);
      case 'AlgorithmIdentifier':
      case 'HashAlgorithmIdentifier': return convertAlgorithmIdentifier(value);
      case 'CryptoKey': return convertCryptoKey(value, what);
      case 'BigInteger': {
        if (getTypedArrayName(value) !== 'Uint8Array') throw typeError(`The ${what} is not of type 'Uint8Array'`);
        if (isSharedArrayBuffer(apply(typedArrayBuffer, value, []))) throw typeError(`The ${what} cannot view a SharedArrayBuffer`);
        return copyBytes(value);
      }
      case 'sequence<DOMString>': return convertSequence(value, convertDOMString, what);
      case 'sequence<RsaOtherPrimesInfo>': return convertSequence(value, (item) => convertDictionary(item, 'RsaOtherPrimesInfo'), what);
    }

    throw typeError(`Unsupported type ${type}`);
  }

  // Converts a value to a dictionary type: the members that are not present are not defined in the result. The members of
  // skippedMembers are not read (e.g. the name of an algorithm, already read).
  function convertDictionary(value, name, skippedMembers) {
    if (value !== undefined && value !== null && !isObject(value)) throw typeError(`The value is not of type '${name}'`);
    const chain = [];
    for (let dictionary = dictionaries[name]; dictionary !== null; dictionary = dictionary.parent) chain.unshift(dictionary);
    const result = ObjectCreate(null);
    for (const dictionary of chain) {
      for (const [key, type, required] of dictionary.members) {
        if (skippedMembers !== undefined && skippedMembers[key] !== undefined) {
          result[key] = skippedMembers[key];
          continue;
        }

        const memberValue = value === undefined || value === null ? undefined : value[key];
        if (memberValue === undefined) {
          if (required) throw typeError(`Failed to read the '${key}' property from '${name}': Required member is undefined.`);
          continue;
        }

        result[key] = convertMember(memberValue, type, `'${key}' member of ${name}`);
      }
    }

    return result;
  }

  // ------------------------------------------------------------------------------------------------------------------
  // Algorithm normalization (https://w3c.github.io/webcrypto/#algorithm-normalization)
  // ------------------------------------------------------------------------------------------------------------------

  // supportedAlgorithms: the IDL dictionary type of each algorithm name, for each operation
  const supportedAlgorithms = ObjectCreate(null);
  // The implementations of the operations, by algorithm name
  const implementations = ObjectCreate(null);

  function defineAlgorithm(name, operations, implementation) {
    for (const op of ownKeys(operations)) {
      const registered = supportedAlgorithms[op] ??= ObjectCreate(null);
      registered[name] = operations[op];
    }

    implementations[name] = implementation;
  }

  const asciiLowerCase = (string) => {
    let result = '';
    for (let i = 0; i < string.length; i++) {
      const c = apply(stringCharCodeAt, string, [i]);
      result += c >= 0x41 && c <= 0x5A ? StringFromCharCode(c + 0x20) : string[i];
    }

    return result;
  };

  function findRegisteredName(registered, name) {
    if (registered === undefined) return undefined;
    const lower = asciiLowerCase(name);
    for (const key of ownKeys(registered)) {
      if (asciiLowerCase(key) === lower) return key;
    }

    return undefined;
  }

  // https://w3c.github.io/webcrypto/#dfn-normalize-an-algorithm
  function normalizeAlgorithm(alg, op) {
    if (typeof alg === 'string') {
      const algorithm = ObjectCreate(null);
      algorithm.name = alg;
      alg = algorithm;
    }

    const registered = supportedAlgorithms[op];
    const initialAlg = convertDictionary(alg, 'Algorithm');
    const algName = findRegisteredName(registered, initialAlg.name);
    if (algName === undefined) throw notSupportedError(`Unrecognized algorithm name '${initialAlg.name}' for the ${op} operation`);

    // The name member is read once, as in the browsers (the spec converts the object to Algorithm, then to the desired type)
    const desiredType = registered[algName];
    const normalizedAlgorithm = convertDictionary(alg, desiredType, initialAlg);
    normalizedAlgorithm.name = algName;

    const chain = [];
    for (let dictionary = dictionaries[desiredType]; dictionary !== null; dictionary = dictionary.parent) chain.unshift(dictionary);
    for (const dictionary of chain) {
      for (const [key, type] of dictionary.members) {
        const idlValue = normalizedAlgorithm[key];
        if (idlValue === undefined || key === 'name') continue;
        if (type === 'BufferSource') normalizedAlgorithm[key] = copyBytes(idlValue);
        else if (type === 'HashAlgorithmIdentifier') normalizedAlgorithm[key] = normalizeAlgorithm(idlValue, 'digest');
        else if (type === 'AlgorithmIdentifier') normalizedAlgorithm[key] = normalizeAlgorithm(idlValue, implementations[algName].identifierOperation);
      }
    }

    return normalizedAlgorithm;
  }

  // Normalizes an algorithm, or returns undefined when an error occurs
  function tryNormalizeAlgorithm(alg, op) {
    try { return normalizeAlgorithm(alg, op); } catch { return undefined; }
  }

  function getImplementation(name) {
    return implementations[name];
  }

  function getOperation(name, op) {
    const implementation = implementations[name];
    const operation = implementation === undefined ? undefined : implementation[op];
    if (operation === undefined) throw notSupportedError(`The ${name} algorithm does not support the ${op} operation`);
    return operation;
  }

  const supportsOperation = (name, op) => implementations[name] !== undefined && implementations[name][op] !== undefined;

  // ------------------------------------------------------------------------------------------------------------------
  // Keys
  // ------------------------------------------------------------------------------------------------------------------

  // https://w3c.github.io/webcrypto/#concept-usage-intersection
  function usageIntersection(a, b) {
    const result = [];
    for (const usage of keyUsages) {
      if (apply(arrayIncludes, a, [usage]) && apply(arrayIncludes, b, [usage])) apply(arrayPush, result, [usage]);
    }

    return result;
  }

  // https://w3c.github.io/webcrypto/#concept-normalized-usages
  const normalizeUsages = (usages) => usageIntersection(usages, keyUsages);

  function checkUsages(usages, allowed) {
    for (const usage of usages) {
      if (!apply(arrayIncludes, allowed, [usage])) throw syntaxError(`The key usage '${usage}' is not allowed for this key`);
    }
  }

  const hasUsage = (record, usage) => apply(arrayIncludes, record.usages, [usage]);

  // A new CryptoKey of this realm: the algorithm is a record (an object without prototype) of the KeyAlgorithm type of the
  // algorithm, the handle a record of the key material (Uint8Array members)
  function createKey(type, extractable, algorithm, usages, handle) {
    const key = ObjectCreate(CryptoKey.prototype);
    const record = createRecord(key, 'CryptoKey');
    record.type = type;
    record.extractable = extractable;
    record.algorithm = algorithm;
    record.usages = usages;
    record.handle = handle;
    record.algorithmCached = undefined;
    record.usagesCached = undefined;
    record.object = key;
    return record;
  }

  const keyAlgorithm = (name) => {
    const algorithm = ObjectCreate(null);
    algorithm.name = name;
    return algorithm;
  };

  // Converts a KeyAlgorithm record (or a JsonWebKey...) to an ECMAScript object of this realm
  // (https://webidl.spec.whatwg.org/#js-dictionary: the members are in the order of the dictionary)
  function toObject(value) {
    if (value instanceof Uint8Array_ || (isObject(value) && getTypedArrayName(value) === 'Uint8Array')) return new Uint8Array_(value);
    if (Array_.isArray(value)) {
      const array = [];
      for (const item of value) apply(arrayPush, array, [toObject(item)]);
      return array;
    }

    if (isObject(value)) {
      const object = {};
      for (const key of ownKeys(value)) {
        if (typeof key === 'string') object[key] = toObject(value[key]);
      }

      return object;
    }

    return value;
  }

  realm.toObject = toObject;

  // https://w3c.github.io/webcrypto/#dfn-CryptoKey-serialization: the record of a serialized key (ArrayBuffer objects for the bytes)
  function serializeValue(value) {
    if (isObject(value) && getTypedArrayName(value) === 'Uint8Array') {
      const record = ObjectCreate(null);
      record.bytes = toArrayBuffer(value);
      return record;
    }

    if (Array_.isArray(value)) {
      const array = [];
      for (const item of value) apply(arrayPush, array, [serializeValue(item)]);
      return array;
    }

    if (isObject(value)) {
      const object = ObjectCreate(null);
      for (const key of ownKeys(value)) {
        if (typeof key === 'string') object[key] = serializeValue(value[key]);
      }

      return object;
    }

    return value;
  }

  function deserializeValue(value) {
    if (Array_.isArray(value)) {
      const array = [];
      for (const item of value) apply(arrayPush, array, [deserializeValue(item)]);
      return array;
    }

    if (isObject(value)) {
      if (isArrayBuffer(value.bytes)) return new Uint8Array_(value.bytes).slice();
      const object = ObjectCreate(null);
      for (const key of ownKeys(value)) {
        if (typeof key === 'string') object[key] = deserializeValue(value[key]);
      }

      return object;
    }

    return value;
  }

  realm.serializeKey = (record) => {
    const serialized = ObjectCreate(null);
    serialized.type = record.type;
    serialized.extractable = record.extractable;
    serialized.algorithm = serializeValue(record.algorithm);
    serialized.usages = serializeValue(record.usages);
    serialized.handle = serializeValue(record.handle);
    return serialized;
  };

  function deserializeKey(serialized) {
    return createKey(serialized.type, serialized.extractable, deserializeValue(serialized.algorithm), deserializeValue(serialized.usages), deserializeValue(serialized.handle)).object;
  }

  // ------------------------------------------------------------------------------------------------------------------
  // JSON Web Key (https://www.rfc-editor.org/rfc/rfc7517)
  // ------------------------------------------------------------------------------------------------------------------

  defineDictionary('RsaOtherPrimesInfo', null, [['r', 'DOMString'], ['d', 'DOMString'], ['t', 'DOMString']]);
  defineDictionary('JsonWebKey', null, [
    ['kty', 'DOMString'], ['use', 'DOMString'], ['key_ops', 'sequence<DOMString>'], ['alg', 'DOMString'], ['ext', 'boolean'],
    ['crv', 'DOMString'], ['x', 'DOMString'], ['y', 'DOMString'], ['d', 'DOMString'], ['n', 'DOMString'], ['e', 'DOMString'],
    ['p', 'DOMString'], ['q', 'DOMString'], ['dp', 'DOMString'], ['dq', 'DOMString'], ['qi', 'DOMString'],
    ['oth', 'sequence<RsaOtherPrimesInfo>'], ['k', 'DOMString'], ['pub', 'DOMString'], ['priv', 'DOMString'],
  ]);

  // The JsonWebKey record of an exported key: the members in the order of the dictionary
  const jsonWebKeyOrder = ['alg', 'crv', 'd', 'dp', 'dq', 'e', 'ext', 'k', 'key_ops', 'kty', 'n', 'oth', 'p', 'priv', 'pub', 'q', 'qi', 'use', 'x', 'y'];

  function jwkToObject(jwk) {
    const object = {};
    for (const key of jsonWebKeyOrder) {
      if (jwk[key] !== undefined) object[key] = toObject(jwk[key]);
    }

    return object;
  }

  // https://w3c.github.io/webcrypto/#concept-parse-a-jwk
  function parseJwk(bytes) {
    let result;
    try {
      result = JSONParse(hooks.utf8Decode(bytes));
    } catch {
      throw dataError('The data is not a valid JSON Web Key');
    }

    const key = convertDictionary(result, 'JsonWebKey');
    if (key.kty === undefined) throw dataError('The kty member of the JSON Web Key is missing');
    return key;
  }

  // The key_ops checks of the import steps (https://www.rfc-editor.org/rfc/rfc7517#section-4.3: no duplicates)
  function checkJwkCommon(jwk, usages, extractable, expectedUse) {
    if (usages.length !== 0 && jwk.use !== undefined && jwk.use !== expectedUse) throw dataError(`The use member of the JSON Web Key must be '${expectedUse}'`);
    if (jwk.key_ops !== undefined) {
      const operations = jwk.key_ops;
      for (let i = 0; i < operations.length; i++) {
        for (let j = i + 1; j < operations.length; j++) {
          if (operations[i] === operations[j]) throw dataError('The key_ops member of the JSON Web Key contains duplicates');
        }
      }

      for (const usage of usages) {
        if (!apply(arrayIncludes, operations, [usage])) throw dataError(`The key_ops member of the JSON Web Key does not contain '${usage}'`);
      }
    }

    if (jwk.ext === false && extractable) throw dataError('The JSON Web Key is not extractable');
  }

  function decodeJwkMember(jwk, member) {
    const value = jwk[member];
    if (value === undefined) throw dataError(`The ${member} member of the JSON Web Key is missing`);
    const bytes = base64UrlDecode(value);
    if (bytes === undefined) throw dataError(`The ${member} member of the JSON Web Key is not valid base64url`);
    return bytes;
  }

  function newJwk(kty, key) {
    const jwk = ObjectCreate(null);
    jwk.kty = kty;
    jwk.key_ops = [...key.usages];
    jwk.ext = key.extractable;
    return jwk;
  }

  // ------------------------------------------------------------------------------------------------------------------
  // Dictionaries of the algorithms
  // ------------------------------------------------------------------------------------------------------------------

  defineDictionary('Algorithm', null, [['name', 'DOMString', true]]);
  defineDictionary('RsaKeyGenParams', 'Algorithm', [['modulusLength', 'unsigned long', true], ['publicExponent', 'BigInteger', true]]);
  defineDictionary('RsaHashedKeyGenParams', 'RsaKeyGenParams', [['hash', 'HashAlgorithmIdentifier', true]]);
  defineDictionary('RsaHashedImportParams', 'Algorithm', [['hash', 'HashAlgorithmIdentifier', true]]);
  defineDictionary('RsaPssParams', 'Algorithm', [['saltLength', 'unsigned long', true]]);
  defineDictionary('RsaOaepParams', 'Algorithm', [['label', 'BufferSource']]);
  defineDictionary('EcdsaParams', 'Algorithm', [['hash', 'HashAlgorithmIdentifier', true]]);
  defineDictionary('EcKeyGenParams', 'Algorithm', [['namedCurve', 'DOMString', true]]);
  defineDictionary('EcKeyImportParams', 'Algorithm', [['namedCurve', 'DOMString', true]]);
  defineDictionary('EcdhKeyDeriveParams', 'Algorithm', [['public', 'CryptoKey', true]]);
  defineDictionary('AesCtrParams', 'Algorithm', [['counter', 'BufferSource', true], ['length', 'octet', true]]);
  defineDictionary('AesKeyGenParams', 'Algorithm', [['length', 'unsigned short', true]]);
  defineDictionary('AesDerivedKeyParams', 'Algorithm', [['length', 'unsigned short', true]]);
  defineDictionary('AesCbcParams', 'Algorithm', [['iv', 'BufferSource', true]]);
  defineDictionary('AesGcmParams', 'Algorithm', [['iv', 'BufferSource', true], ['additionalData', 'BufferSource'], ['tagLength', 'octet']]);
  defineDictionary('HmacImportParams', 'Algorithm', [['hash', 'HashAlgorithmIdentifier', true], ['length', 'unsigned long']]);
  defineDictionary('HmacKeyGenParams', 'Algorithm', [['hash', 'HashAlgorithmIdentifier', true], ['length', 'unsigned long']]);
  defineDictionary('HkdfParams', 'Algorithm', [['hash', 'HashAlgorithmIdentifier', true], ['salt', 'BufferSource', true], ['info', 'BufferSource', true]]);
  defineDictionary('Pbkdf2Params', 'Algorithm', [['salt', 'BufferSource', true], ['iterations', 'unsigned long', true], ['hash', 'HashAlgorithmIdentifier', true]]);
  // https://wicg.github.io/webcrypto-secure-curves/#dfn-Ed448Params
  defineDictionary('Ed448Params', 'Algorithm', [['context', 'BufferSource']]);
  // https://wicg.github.io/webcrypto-modern-algos/
  defineDictionary('ContextParams', 'Algorithm', [['context', 'BufferSource']]);
  defineDictionary('AeadParams', 'Algorithm', [['iv', 'BufferSource', true], ['additionalData', 'BufferSource'], ['tagLength', 'octet']]);
  defineDictionary('CShakeParams', 'Algorithm', [['outputLength', 'unsigned long', true], ['functionName', 'BufferSource'], ['customization', 'BufferSource']]);
  defineDictionary('TurboShakeParams', 'Algorithm', [['outputLength', 'unsigned long', true], ['domainSeparation', 'octet']]);
  defineDictionary('KangarooTwelveParams', 'Algorithm', [['outputLength', 'unsigned long', true], ['customization', 'BufferSource']]);
  defineDictionary('KmacKeyGenParams', 'Algorithm', [['length', 'unsigned long']]);
  defineDictionary('KmacImportParams', 'Algorithm', [['length', 'unsigned long']]);
  defineDictionary('KmacParams', 'Algorithm', [['outputLength', 'unsigned long', true], ['customization', 'BufferSource']]);
  defineDictionary('Argon2Params', 'Algorithm', [
    ['nonce', 'BufferSource', true], ['parallelism', 'unsigned long', true], ['memory', 'unsigned long', true], ['passes', 'unsigned long', true],
    ['version', 'octet'], ['secretValue', 'BufferSource'], ['associatedData', 'BufferSource'],
  ]);

  // The hash functions usable by the algorithms (the names are the normalized ones)
  const hashLengths = { 'SHA-1': 160, 'SHA-256': 256, 'SHA-384': 384, 'SHA-512': 512 };
  const hashBlockSizes = { 'SHA-1': 512, 'SHA-256': 512, 'SHA-384': 1024, 'SHA-512': 1024 };

  // A hash function supported by the algorithms that use one (HMAC, HKDF, PBKDF2, RSA, ECDSA): the SHA-3 and the XOF
  // functions of the modern algorithms are only defined for digest()
  function checkHash(hash) {
    if (hashLengths[hash.name] === undefined) throw notSupportedError(`The ${hash.name} algorithm cannot be used as a hash function`);
    return hash.name;
  }

  // ------------------------------------------------------------------------------------------------------------------
  // SHA (https://w3c.github.io/webcrypto/#sha), SHA-3 (https://wicg.github.io/webcrypto-modern-algos/#sha3)
  // ------------------------------------------------------------------------------------------------------------------

  for (const name of ['SHA-1', 'SHA-256', 'SHA-384', 'SHA-512']) {
    defineAlgorithm(name, { digest: 'Algorithm' }, {
      digest: (normalizedAlgorithm, message) => hooks.digest(normalizedAlgorithm.name, message),
    });
  }

  // ------------------------------------------------------------------------------------------------------------------
  // Symmetric keys
  // ------------------------------------------------------------------------------------------------------------------

  // The import steps of the raw and jwk formats of a secret key ("raw-secret" is an alias of "raw")
  function importSecretBytes(format, keyData, kty) {
    if (format === 'raw' || format === 'raw-secret') return keyData;
    if (format === 'jwk') {
      if (keyData.kty !== kty) throw dataError(`The kty member of the JSON Web Key must be '${kty}'`);
      return decodeJwkMember(keyData, 'k');
    }

    throw notSupportedError(`The ${format} format is not supported`);
  }

  function exportSecretKey(format, key, alg) {
    if (format === 'raw' || format === 'raw-secret') return key.handle.secret;
    if (format === 'jwk') {
      const jwk = newJwk('oct', key);
      jwk.k = base64UrlEncode(key.handle.secret);
      if (alg !== undefined) jwk.alg = alg;
      return jwk;
    }

    throw notSupportedError(`The ${format} format is not supported`);
  }

  const secretHandle = (bytes) => {
    const handle = ObjectCreate(null);
    handle.secret = bytes;
    return handle;
  };

  // ------------------------------------------------------------------------------------------------------------------
  // AES (https://w3c.github.io/webcrypto/#aes-ctr ... #aes-kw), AES-OCB (https://wicg.github.io/webcrypto-modern-algos/#aes-ocb)
  // ------------------------------------------------------------------------------------------------------------------

  const aesKeyLength = (length) => {
    if (length !== 128 && length !== 192 && length !== 256) throw operationError('The AES key length must be 128, 192 or 256 bits');
    return length;
  };

  function defineAes(name, usages, operations, implementation) {
    const algSuffix = { 'AES-CTR': 'CTR', 'AES-CBC': 'CBC', 'AES-GCM': 'GCM', 'AES-KW': 'KW', 'AES-OCB': 'OCB' }[name];
    defineAlgorithm(name, { generateKey: 'AesKeyGenParams', importKey: 'Algorithm', exportKey: 'Algorithm', 'get key length': 'AesDerivedKeyParams', ...operations }, {
      ...implementation,
      generateKey(normalizedAlgorithm, extractable, keyUsages) {
        checkUsages(keyUsages, usages);
        const length = aesKeyLength(normalizedAlgorithm.length);
        const algorithm = keyAlgorithm(name);
        algorithm.length = length;
        return createKey('secret', extractable, algorithm, normalizeUsages(keyUsages), secretHandle(hooks.randomBytes(length / 8)));
      },
      importKey(format, keyData, normalizedAlgorithm, extractable, keyUsages) {
        checkUsages(keyUsages, usages);
        const data = importSecretBytes(format, keyData, 'oct');
        const length = data.length * 8;
        if (length !== 128 && length !== 192 && length !== 256) throw dataError('The AES key length must be 128, 192 or 256 bits');
        if (format === 'jwk') {
          if (keyData.alg !== undefined && keyData.alg !== `A${length}${algSuffix}`) throw dataError(`The alg member of the JSON Web Key must be 'A${length}${algSuffix}'`);
          checkJwkCommon(keyData, keyUsages, extractable, 'enc');
        }

        const algorithm = keyAlgorithm(name);
        algorithm.length = length;
        return createKey('secret', extractable, algorithm, keyUsages, secretHandle(data));
      },
      exportKey: (format, key) => exportSecretKey(format, key, `A${key.algorithm.length}${algSuffix}`),
      getKeyLength: (normalizedAlgorithm) => {
        const length = normalizedAlgorithm.length;
        if (length !== 128 && length !== 192 && length !== 256) throw operationError('The AES key length must be 128, 192 or 256 bits');
        return length;
      },
    });
  }

  const encryptionUsages = ['encrypt', 'decrypt', 'wrapKey', 'unwrapKey'];

  // The CTR encryption and decryption are the same operation
  function aesCtr(normalizedAlgorithm, key, input) {
    if (normalizedAlgorithm.counter.length !== 16) throw operationError('The counter must be 16 bytes');
    if (normalizedAlgorithm.length === 0 || normalizedAlgorithm.length > 128) throw operationError('The counter length must be between 1 and 128 bits');
    return hooks.aesCtr(key.handle.secret, normalizedAlgorithm.counter, normalizedAlgorithm.length, input);
  }

  defineAes('AES-CTR', encryptionUsages, { encrypt: 'AesCtrParams', decrypt: 'AesCtrParams' }, { encrypt: aesCtr, decrypt: aesCtr });

  defineAes('AES-CBC', encryptionUsages, { encrypt: 'AesCbcParams', decrypt: 'AesCbcParams' }, {
    encrypt(normalizedAlgorithm, key, plaintext) {
      if (normalizedAlgorithm.iv.length !== 16) throw operationError('The iv must be 16 bytes');
      return hooks.aesCbc(true, key.handle.secret, normalizedAlgorithm.iv, plaintext);
    },
    decrypt(normalizedAlgorithm, key, ciphertext) {
      if (normalizedAlgorithm.iv.length !== 16) throw operationError('The iv must be 16 bytes');
      if (ciphertext.length === 0 || ciphertext.length % 16 !== 0) throw operationError('The ciphertext length must be a non-zero multiple of 16 bytes');
      const plaintext = hooks.aesCbc(false, key.handle.secret, normalizedAlgorithm.iv, ciphertext);
      if (plaintext === null) throw operationError('The padding of the plaintext is invalid');
      return plaintext;
    },
  });

  function gcmTagLength(normalizedAlgorithm) {
    const tagLength = normalizedAlgorithm.tagLength;
    if (tagLength === undefined) return 128;
    if (tagLength === 32 || tagLength === 64 || tagLength === 96 || tagLength === 104 || tagLength === 112 || tagLength === 120 || tagLength === 128) return tagLength;
    throw operationError('The tag length must be 32, 64, 96, 104, 112, 120 or 128 bits');
  }

  defineAes('AES-GCM', encryptionUsages, { encrypt: 'AesGcmParams', decrypt: 'AesGcmParams' }, {
    encrypt(normalizedAlgorithm, key, plaintext) {
      const tagLength = gcmTagLength(normalizedAlgorithm);
      return hooks.aesGcm(true, key.handle.secret, normalizedAlgorithm.iv, normalizedAlgorithm.additionalData ?? new Uint8Array_(0), tagLength / 8, plaintext);
    },
    decrypt(normalizedAlgorithm, key, ciphertext) {
      const tagLength = gcmTagLength(normalizedAlgorithm);
      if (ciphertext.length * 8 < tagLength) throw operationError('The ciphertext is shorter than the tag');
      const plaintext = hooks.aesGcm(false, key.handle.secret, normalizedAlgorithm.iv, normalizedAlgorithm.additionalData ?? new Uint8Array_(0), tagLength / 8, ciphertext);
      if (plaintext === null) throw operationError('The authentication tag is invalid');
      return plaintext;
    },
  });

  defineAes('AES-KW', ['wrapKey', 'unwrapKey'], { wrapKey: 'Algorithm', unwrapKey: 'Algorithm' }, {
    wrapKey(normalizedAlgorithm, key, plaintext) {
      if (plaintext.length % 8 !== 0) throw operationError('The data to wrap must be a multiple of 64 bits');
      return hooks.aesKeyWrap(true, key.handle.secret, plaintext);
    },
    unwrapKey(normalizedAlgorithm, key, ciphertext) {
      const plaintext = hooks.aesKeyWrap(false, key.handle.secret, ciphertext);
      if (plaintext === null) throw operationError('The wrapped key is invalid');
      return plaintext;
    },
  });

  // ------------------------------------------------------------------------------------------------------------------
  // HMAC (https://w3c.github.io/webcrypto/#hmac)
  // ------------------------------------------------------------------------------------------------------------------

  const hmacJwkAlgs = { 'SHA-1': 'HS1', 'SHA-256': 'HS256', 'SHA-384': 'HS384', 'SHA-512': 'HS512' };

  // The first length bits of the data (the other bits of the last byte are zero)
  function truncateBits(data, length) {
    const bytes = sliceBytes(data, 0, (length + 7) >> 3);
    if (length % 8 !== 0) bytes[bytes.length - 1] &= (0xFF << (8 - (length % 8))) & 0xFF;
    return bytes;
  }

  defineAlgorithm('HMAC', { sign: 'Algorithm', verify: 'Algorithm', generateKey: 'HmacKeyGenParams', importKey: 'HmacImportParams', exportKey: 'Algorithm', 'get key length': 'HmacImportParams' }, {
    sign: (normalizedAlgorithm, key, message) => hooks.hmac(key.algorithm.hash.name, key.handle.secret, message),
    verify: (normalizedAlgorithm, key, message, signature) => bytesEqual(hooks.hmac(key.algorithm.hash.name, key.handle.secret, message), signature),
    generateKey(normalizedAlgorithm, extractable, keyUsages) {
      checkUsages(keyUsages, ['sign', 'verify']);
      const hash = checkHash(normalizedAlgorithm.hash);
      let length;
      if (normalizedAlgorithm.length === undefined) length = hashBlockSizes[hash];
      else if (normalizedAlgorithm.length !== 0) length = normalizedAlgorithm.length;
      else throw operationError('The HMAC key length must not be zero');
      const algorithm = keyAlgorithm('HMAC');
      algorithm.hash = keyAlgorithm(hash);
      algorithm.length = length;
      return createKey('secret', extractable, algorithm, normalizeUsages(keyUsages), secretHandle(truncateBits(hooks.randomBytes((length + 7) >> 3), length)));
    },
    importKey(format, keyData, normalizedAlgorithm, extractable, keyUsages) {
      if (normalizedAlgorithm.length === 0) throw dataError('The HMAC key length must not be zero');
      checkUsages(keyUsages, ['sign', 'verify']);
      const hash = checkHash(normalizedAlgorithm.hash);
      const data = importSecretBytes(format, keyData, 'oct');
      if (format === 'jwk') {
        if (keyData.alg !== undefined && keyData.alg !== hmacJwkAlgs[hash]) throw dataError(`The alg member of the JSON Web Key must be '${hmacJwkAlgs[hash]}'`);
        checkJwkCommon(keyData, keyUsages, extractable, 'sig');
      }

      let length = data.length * 8;
      if (length === 0) throw dataError('The HMAC key must not be empty');
      if (normalizedAlgorithm.length !== undefined) {
        if (normalizedAlgorithm.length > length) throw dataError('The HMAC key length is greater than the length of the data');
        if (normalizedAlgorithm.length <= length - 8) throw dataError('The HMAC key length is too short for the data');
        length = normalizedAlgorithm.length;
      }

      const algorithm = keyAlgorithm('HMAC');
      algorithm.hash = keyAlgorithm(hash);
      algorithm.length = length;
      return createKey('secret', extractable, algorithm, keyUsages, secretHandle(truncateBits(data, length)));
    },
    exportKey: (format, key) => exportSecretKey(format, key, hmacJwkAlgs[key.algorithm.hash.name]),
    getKeyLength(normalizedAlgorithm) {
      const hash = checkHash(normalizedAlgorithm.hash);
      if (normalizedAlgorithm.length === undefined) return hashBlockSizes[hash];
      if (normalizedAlgorithm.length !== 0) return normalizedAlgorithm.length;
      throw typeError('The HMAC key length must not be zero');
    },
  });

  // ------------------------------------------------------------------------------------------------------------------
  // HKDF (https://w3c.github.io/webcrypto/#hkdf), PBKDF2 (https://w3c.github.io/webcrypto/#pbkdf2)
  // ------------------------------------------------------------------------------------------------------------------

  function importKdfKey(name, format, keyData, extractable, keyUsages) {
    if (format !== 'raw' && format !== 'raw-secret') throw notSupportedError(`The ${format} format is not supported`);
    checkUsages(keyUsages, ['deriveKey', 'deriveBits']);
    if (extractable !== false) throw syntaxError(`A ${name} key cannot be extractable`);
    return createKey('secret', extractable, keyAlgorithm(name), keyUsages, secretHandle(keyData));
  }

  defineAlgorithm('HKDF', { deriveBits: 'HkdfParams', importKey: 'Algorithm', 'get key length': 'Algorithm' }, {
    deriveBits(normalizedAlgorithm, key, length) {
      if (length === null || length % 8 !== 0) throw operationError('The length must be a multiple of 8 bits');
      const hash = checkHash(normalizedAlgorithm.hash);
      if (length > 255 * hashLengths[hash]) throw operationError('The length is too large');
      return hooks.hkdf(hash, key.handle.secret, normalizedAlgorithm.salt, normalizedAlgorithm.info, length / 8);
    },
    importKey: (format, keyData, normalizedAlgorithm, extractable, keyUsages) => importKdfKey('HKDF', format, keyData, extractable, keyUsages),
    getKeyLength: () => null,
  });

  defineAlgorithm('PBKDF2', { deriveBits: 'Pbkdf2Params', importKey: 'Algorithm', 'get key length': 'Algorithm' }, {
    deriveBits(normalizedAlgorithm, key, length) {
      if (length === null || length % 8 !== 0) throw operationError('The length must be a multiple of 8 bits');
      if (normalizedAlgorithm.iterations === 0) throw operationError('The number of iterations must not be zero');
      const hash = checkHash(normalizedAlgorithm.hash);
      if (length === 0) return new Uint8Array_(0);
      return hooks.pbkdf2(hash, key.handle.secret, normalizedAlgorithm.salt, normalizedAlgorithm.iterations, length / 8);
    },
    importKey: (format, keyData, normalizedAlgorithm, extractable, keyUsages) => importKdfKey('PBKDF2', format, keyData, extractable, keyUsages),
    getKeyLength: () => null,
  });

  // ------------------------------------------------------------------------------------------------------------------
  // Asymmetric keys
  // ------------------------------------------------------------------------------------------------------------------

  // A new CryptoKeyPair record (https://w3c.github.io/webcrypto/#keypair)
  function keyPair(publicKey, privateKey) {
    const pair = ObjectCreate(null);
    pair.publicKey = publicKey;
    pair.privateKey = privateKey;
    return pair;
  }

  const isPublicFormat = (format) => format === 'raw' || format === 'raw-public';

  // The usages checks of the jwk format, and the common members of the JSON Web Key
  function checkAsymmetricJwk(jwk, keyUsages, extractable, kty, publicUsages, privateUsages, expectedUse) {
    checkUsages(keyUsages, jwk.d !== undefined ? privateUsages : publicUsages);
    if (jwk.kty !== kty) throw dataError(`The kty member of the JSON Web Key must be '${kty}'`);
    checkJwkCommon(jwk, keyUsages, extractable, expectedUse);
  }

  function bitLength(bytes) {
    let start = 0;
    while (start < bytes.length && bytes[start] === 0) start++;
    if (start === bytes.length) return 0;
    let bits = (bytes.length - start - 1) * 8;
    for (let b = bytes[start]; b !== 0; b >>= 1) bits++;
    return bits;
  }

  // The derive bits result of a key agreement: the first length bits of the secret
  function truncateSecret(secret, length) {
    if (length === null) return secret;
    if (secret.length * 8 < length) throw operationError('The length is greater than the length of the shared secret');
    return truncateBits(secret, length);
  }

  // ------------------------------------------------------------------------------------------------------------------
  // RSA (https://w3c.github.io/webcrypto/#rsassa-pkcs1, #rsa-pss, #rsa-oaep)
  // ------------------------------------------------------------------------------------------------------------------

  const rsaJwkAlgs = {
    'RSASSA-PKCS1-v1_5': { 'SHA-1': 'RS1', 'SHA-256': 'RS256', 'SHA-384': 'RS384', 'SHA-512': 'RS512' },
    'RSA-PSS': { 'SHA-1': 'PS1', 'SHA-256': 'PS256', 'SHA-384': 'PS384', 'SHA-512': 'PS512' },
    'RSA-OAEP': { 'SHA-1': 'RSA-OAEP', 'SHA-256': 'RSA-OAEP-256', 'SHA-384': 'RSA-OAEP-384', 'SHA-512': 'RSA-OAEP-512' },
  };

  function rsaKeyAlgorithm(name, modulusLength, publicExponent, hash) {
    const algorithm = keyAlgorithm(name);
    algorithm.modulusLength = modulusLength;
    algorithm.publicExponent = publicExponent;
    algorithm.hash = keyAlgorithm(hash);
    return algorithm;
  }

  function rsaPublicHandle(handle) {
    const result = ObjectCreate(null);
    result.n = handle.n;
    result.e = handle.e;
    return result;
  }

  function defineRsa(name, publicUsages, privateUsages, expectedUse, operations, implementation) {
    const jwkAlgs = rsaJwkAlgs[name];
    defineAlgorithm(name, { generateKey: 'RsaHashedKeyGenParams', importKey: 'RsaHashedImportParams', exportKey: 'Algorithm', ...operations }, {
      ...implementation,
      generateKey(normalizedAlgorithm, extractable, keyUsages) {
        checkUsages(keyUsages, [...publicUsages, ...privateUsages]);
        const hash = checkHash(normalizedAlgorithm.hash);
        const handle = hooks.rsaGenerate(normalizedAlgorithm.modulusLength, normalizedAlgorithm.publicExponent);
        const algorithm = rsaKeyAlgorithm(name, normalizedAlgorithm.modulusLength, normalizedAlgorithm.publicExponent, hash);
        return keyPair(
          createKey('public', true, algorithm, usageIntersection(keyUsages, publicUsages), rsaPublicHandle(handle)),
          createKey('private', extractable, algorithm, usageIntersection(keyUsages, privateUsages), handle));
      },
      importKey(format, keyData, normalizedAlgorithm, extractable, keyUsages) {
        const hash = checkHash(normalizedAlgorithm.hash);
        let handle, type;
        if (format === 'spki') {
          checkUsages(keyUsages, publicUsages);
          handle = hooks.rsaImport('spki', keyData);
          type = 'public';
        } else if (format === 'pkcs8') {
          checkUsages(keyUsages, privateUsages);
          handle = hooks.rsaImport('pkcs8', keyData);
          type = 'private';
        } else if (format === 'jwk') {
          checkAsymmetricJwk(keyData, keyUsages, extractable, 'RSA', publicUsages, privateUsages, expectedUse);
          if (keyData.alg !== undefined) {
            let jwkHash;
            for (const key of ownKeys(jwkAlgs)) if (jwkAlgs[key] === keyData.alg) jwkHash = key;
            if (jwkHash === undefined) throw dataError(`The alg member of the JSON Web Key is not supported: '${keyData.alg}'`);
            if (jwkHash !== hash) throw dataError('The alg member of the JSON Web Key does not match the hash function');
          }

          const n = decodeJwkMember(keyData, 'n');
          const e = decodeJwkMember(keyData, 'e');
          if (keyData.d !== undefined) {
            if (keyData.oth !== undefined) throw dataError('The RSA keys with more than two primes are not supported');
            const optional = (member) => keyData[member] === undefined ? undefined : decodeJwkMember(keyData, member);
            handle = hooks.rsaFromComponents(n, e, decodeJwkMember(keyData, 'd'), optional('p'), optional('q'), optional('dp'), optional('dq'), optional('qi'));
            type = 'private';
          } else {
            handle = hooks.rsaFromComponents(n, e, undefined, undefined, undefined, undefined, undefined, undefined);
            type = 'public';
          }
        } else {
          throw notSupportedError(`The ${format} format is not supported`);
        }

        const algorithm = rsaKeyAlgorithm(name, bitLength(handle.n), handle.e, hash);
        return createKey(type, extractable, algorithm, keyUsages, type === 'public' ? rsaPublicHandle(handle) : handle);
      },
      exportKey(format, key) {
        if (format === 'spki') {
          if (key.type !== 'public') throw invalidAccessError('Only a public key can be exported in the spki format');
          return hooks.rsaExport('spki', key.handle);
        }

        if (format === 'pkcs8') {
          if (key.type !== 'private') throw invalidAccessError('Only a private key can be exported in the pkcs8 format');
          return hooks.rsaExport('pkcs8', key.handle);
        }

        if (format === 'jwk') {
          const jwk = newJwk('RSA', key);
          const alg = jwkAlgs[key.algorithm.hash.name];
          if (alg === undefined) throw notSupportedError('The hash function has no JSON Web Algorithm');
          jwk.alg = alg;
          jwk.n = base64UrlEncode(key.handle.n);
          jwk.e = base64UrlEncode(key.handle.e);
          if (key.type === 'private') {
            for (const member of ['d', 'p', 'q', 'dp', 'dq', 'qi']) jwk[member] = base64UrlEncode(key.handle[member]);
          }

          return jwk;
        }

        throw notSupportedError(`The ${format} format is not supported`);
      },
      getPublicKey: (key) => rsaPublicHandle(key.handle),
      publicUsages,
    });
  }

  function requireKeyType(key, type) {
    if (key.type !== type) throw invalidAccessError(`The key is not a ${type} key`);
  }

  defineRsa('RSASSA-PKCS1-v1_5', ['verify'], ['sign'], 'sig', { sign: 'Algorithm', verify: 'Algorithm' }, {
    sign(normalizedAlgorithm, key, message) {
      requireKeyType(key, 'private');
      return hooks.rsaSign('pkcs1', key.algorithm.hash.name, key.handle, message, 0);
    },
    verify(normalizedAlgorithm, key, message, signature) {
      requireKeyType(key, 'public');
      return hooks.rsaVerify('pkcs1', key.algorithm.hash.name, key.handle, message, signature, 0);
    },
  });

  defineRsa('RSA-PSS', ['verify'], ['sign'], 'sig', { sign: 'RsaPssParams', verify: 'RsaPssParams' }, {
    sign(normalizedAlgorithm, key, message) {
      requireKeyType(key, 'private');
      return hooks.rsaSign('pss', key.algorithm.hash.name, key.handle, message, normalizedAlgorithm.saltLength);
    },
    verify(normalizedAlgorithm, key, message, signature) {
      requireKeyType(key, 'public');
      return hooks.rsaVerify('pss', key.algorithm.hash.name, key.handle, message, signature, normalizedAlgorithm.saltLength);
    },
  });

  defineRsa('RSA-OAEP', ['encrypt', 'wrapKey'], ['decrypt', 'unwrapKey'], 'enc', { encrypt: 'RsaOaepParams', decrypt: 'RsaOaepParams' }, {
    encrypt(normalizedAlgorithm, key, plaintext) {
      requireKeyType(key, 'public');
      return hooks.rsaOaep(true, key.algorithm.hash.name, key.handle, normalizedAlgorithm.label ?? new Uint8Array_(0), plaintext);
    },
    decrypt(normalizedAlgorithm, key, ciphertext) {
      requireKeyType(key, 'private');
      return hooks.rsaOaep(false, key.algorithm.hash.name, key.handle, normalizedAlgorithm.label ?? new Uint8Array_(0), ciphertext);
    },
  });

  // ------------------------------------------------------------------------------------------------------------------
  // ECDSA (https://w3c.github.io/webcrypto/#ecdsa), ECDH (https://w3c.github.io/webcrypto/#ecdh)
  // ------------------------------------------------------------------------------------------------------------------

  const namedCurves = ['P-256', 'P-384', 'P-521'];
  const ecFieldLengths = { 'P-256': 256, 'P-384': 384, 'P-521': 528 };
  const ecdsaJwkAlgs = { 'P-256': 'ES256', 'P-384': 'ES384', 'P-521': 'ES512' };

  function ecKeyAlgorithm(name, namedCurve) {
    const algorithm = keyAlgorithm(name);
    algorithm.namedCurve = namedCurve;
    return algorithm;
  }

  function ecPublicHandle(handle) {
    const result = ObjectCreate(null);
    result.crv = handle.crv;
    result.x = handle.x;
    result.y = handle.y;
    return result;
  }

  function defineEc(name, publicUsages, privateUsages, expectedUse, operations, implementation) {
    defineAlgorithm(name, { generateKey: 'EcKeyGenParams', importKey: 'EcKeyImportParams', exportKey: 'Algorithm', ...operations }, {
      ...implementation,
      generateKey(normalizedAlgorithm, extractable, keyUsages) {
        checkUsages(keyUsages, [...publicUsages, ...privateUsages]);
        const namedCurve = normalizedAlgorithm.namedCurve;
        if (!apply(arrayIncludes, namedCurves, [namedCurve])) throw notSupportedError(`The named curve '${namedCurve}' is not supported`);
        const handle = hooks.ecGenerate(namedCurve);
        const algorithm = ecKeyAlgorithm(name, namedCurve);
        return keyPair(
          createKey('public', true, algorithm, usageIntersection(keyUsages, publicUsages), ecPublicHandle(handle)),
          createKey('private', extractable, algorithm, usageIntersection(keyUsages, privateUsages), handle));
      },
      importKey(format, keyData, normalizedAlgorithm, extractable, keyUsages) {
        const namedCurve = normalizedAlgorithm.namedCurve;
        if (!apply(arrayIncludes, namedCurves, [namedCurve])) throw notSupportedError(`The named curve '${namedCurve}' is not supported`);
        let handle, type;
        if (format === 'spki' || format === 'pkcs8') {
          checkUsages(keyUsages, format === 'spki' ? publicUsages : privateUsages);
          handle = hooks.ecImport(format, namedCurve, keyData);
          if (handle === null) throw dataError('The named curve of the key is not supported');
          if (handle.crv !== namedCurve) throw dataError('The named curve of the key does not match the algorithm');
          type = format === 'spki' ? 'public' : 'private';
        } else if (format === 'jwk') {
          checkAsymmetricJwk(keyData, keyUsages, extractable, 'EC', publicUsages, privateUsages, expectedUse);
          if (keyData.crv !== namedCurve) throw dataError('The crv member of the JSON Web Key does not match the named curve');
          if (name === 'ECDSA' && keyData.alg !== undefined && keyData.alg !== ecdsaJwkAlgs[namedCurve]) throw dataError('The alg member of the JSON Web Key does not match the named curve');
          const x = decodeJwkMember(keyData, 'x');
          const y = decodeJwkMember(keyData, 'y');
          handle = hooks.ecFromJwk(namedCurve, x, y, keyData.d === undefined ? undefined : decodeJwkMember(keyData, 'd'));
          type = keyData.d === undefined ? 'public' : 'private';
        } else if (isPublicFormat(format)) {
          checkUsages(keyUsages, publicUsages);
          handle = hooks.ecImport('raw', namedCurve, keyData);
          type = 'public';
        } else {
          throw notSupportedError(`The ${format} format is not supported`);
        }

        return createKey(type, extractable, ecKeyAlgorithm(name, namedCurve), keyUsages, handle);
      },
      exportKey(format, key) {
        if (format === 'spki' || isPublicFormat(format)) {
          if (key.type !== 'public') throw invalidAccessError(`Only a public key can be exported in the ${format} format`);
          return hooks.ecExport(format === 'spki' ? 'spki' : 'raw', key.handle);
        }

        if (format === 'pkcs8') {
          if (key.type !== 'private') throw invalidAccessError('Only a private key can be exported in the pkcs8 format');
          return hooks.ecExport('pkcs8', key.handle);
        }

        if (format === 'jwk') {
          const jwk = newJwk('EC', key);
          jwk.crv = key.algorithm.namedCurve;
          jwk.x = base64UrlEncode(key.handle.x);
          jwk.y = base64UrlEncode(key.handle.y);
          if (key.type === 'private') jwk.d = base64UrlEncode(key.handle.d);
          return jwk;
        }

        throw notSupportedError(`The ${format} format is not supported`);
      },
      getPublicKey: (key) => ecPublicHandle(key.handle),
      publicUsages,
    });
  }

  defineEc('ECDSA', ['verify'], ['sign'], 'sig', { sign: 'EcdsaParams', verify: 'EcdsaParams' }, {
    sign(normalizedAlgorithm, key, message) {
      requireKeyType(key, 'private');
      return hooks.ecSign(checkHash(normalizedAlgorithm.hash), key.handle, message);
    },
    verify(normalizedAlgorithm, key, message, signature) {
      requireKeyType(key, 'public');
      return hooks.ecVerify(checkHash(normalizedAlgorithm.hash), key.handle, message, signature);
    },
  });

  // The checks of the public member of the derive bits operations (https://w3c.github.io/webcrypto/#ecdh-operations-derive-bits)
  function checkPeerKey(normalizedAlgorithm, key, length, maximumLength) {
    const publicKey = normalizedAlgorithm.public;
    if (publicKey.type !== 'public') throw invalidAccessError('The public member must be a public key');
    if (publicKey.algorithm.name !== normalizedAlgorithm.name) throw invalidAccessError(`The public member must be a ${normalizedAlgorithm.name} key`);
    if (length !== null && length > maximumLength) throw operationError('The length is greater than the length of the shared secret');
    // supports(): the next steps need the base key
    if (key === null) return publicKey;
    if (key.type !== 'private') throw invalidAccessError('The base key must be a private key');
    if (publicKey.algorithm.name !== key.algorithm.name) throw invalidAccessError('The keys do not have the same algorithm');
    return publicKey;
  }

  defineEc('ECDH', [], ['deriveKey', 'deriveBits'], 'enc', { deriveBits: 'EcdhKeyDeriveParams' }, {
    deriveBits(normalizedAlgorithm, key, length) {
      const publicKey = checkPeerKey(normalizedAlgorithm, key, length, ecFieldLengths[normalizedAlgorithm.public.algorithm.namedCurve] ?? 0);
      if (publicKey.algorithm.namedCurve !== key.algorithm.namedCurve) throw invalidAccessError('The keys are not on the same curve');
      return truncateSecret(hooks.ecDerive(key.handle, publicKey.handle), length);
    },
  });

  // ------------------------------------------------------------------------------------------------------------------
  // Ed25519, X25519 (https://w3c.github.io/webcrypto/#ed25519), Ed448, X448 (https://wicg.github.io/webcrypto-secure-curves/)
  // ------------------------------------------------------------------------------------------------------------------

  const okpKeyLengths = { Ed25519: 32, X25519: 32, Ed448: 57, X448: 56 };

  function okpPublicHandle(handle) {
    const result = ObjectCreate(null);
    result.pub = handle.pub;
    return result;
  }

  function defineOkp(name, publicUsages, privateUsages, expectedUse, jwkAlgs, operations, implementation) {
    defineAlgorithm(name, { generateKey: 'Algorithm', importKey: 'Algorithm', exportKey: 'Algorithm', ...operations }, {
      ...implementation,
      generateKey(normalizedAlgorithm, extractable, keyUsages) {
        checkUsages(keyUsages, [...publicUsages, ...privateUsages]);
        const handle = hooks.okpGenerate(name);
        const algorithm = keyAlgorithm(name);
        return keyPair(
          createKey('public', true, algorithm, usageIntersection(keyUsages, publicUsages), okpPublicHandle(handle)),
          createKey('private', extractable, algorithm, usageIntersection(keyUsages, privateUsages), handle));
      },
      importKey(format, keyData, normalizedAlgorithm, extractable, keyUsages) {
        let handle, type;
        if (format === 'spki' || format === 'pkcs8') {
          checkUsages(keyUsages, format === 'spki' ? publicUsages : privateUsages);
          handle = hooks.okpImport(name, format, keyData);
          type = format === 'spki' ? 'public' : 'private';
          if (type === 'public') handle = okpPublicHandle(handle);
        } else if (format === 'jwk') {
          checkUsages(keyUsages, keyData.d !== undefined ? privateUsages : publicUsages);
          if (keyData.kty !== 'OKP') throw dataError("The kty member of the JSON Web Key must be 'OKP'");
          if (keyData.crv !== name) throw dataError(`The crv member of the JSON Web Key must be '${name}'`);
          if (jwkAlgs !== null && keyData.alg !== undefined && !apply(arrayIncludes, jwkAlgs, [keyData.alg])) throw dataError('The alg member of the JSON Web Key is not supported');
          checkJwkCommon(keyData, keyUsages, extractable, expectedUse);
          const x = decodeJwkMember(keyData, 'x');
          if (x.length !== okpKeyLengths[name]) throw dataError('The x member of the JSON Web Key does not have the length of the key');
          handle = ObjectCreate(null);
          if (keyData.d !== undefined) {
            const d = decodeJwkMember(keyData, 'd');
            if (d.length !== okpKeyLengths[name]) throw dataError('The d member of the JSON Web Key does not have the length of the key');
            if (!bytesEqual(hooks.okpPublicKey(name, d), x)) throw dataError('The x member of the JSON Web Key does not match the private key');
            handle.priv = d;
            type = 'private';
          } else {
            type = 'public';
          }

          handle.pub = x;
        } else if (isPublicFormat(format)) {
          checkUsages(keyUsages, publicUsages);
          if (keyData.length !== okpKeyLengths[name]) throw dataError(`The ${name} public key must be ${okpKeyLengths[name]} bytes`);
          handle = ObjectCreate(null);
          handle.pub = keyData;
          type = 'public';
        } else {
          throw notSupportedError(`The ${format} format is not supported`);
        }

        return createKey(type, extractable, keyAlgorithm(name), keyUsages, handle);
      },
      exportKey(format, key) {
        if (format === 'spki' || isPublicFormat(format)) {
          if (key.type !== 'public') throw invalidAccessError(`Only a public key can be exported in the ${format} format`);
          return format === 'spki' ? hooks.okpExport(name, 'spki', key.handle.pub) : key.handle.pub;
        }

        if (format === 'pkcs8') {
          if (key.type !== 'private') throw invalidAccessError('Only a private key can be exported in the pkcs8 format');
          return hooks.okpExport(name, 'pkcs8', key.handle.priv);
        }

        if (format === 'jwk') {
          const jwk = newJwk('OKP', key);
          if (jwkAlgs !== null) jwk.alg = name;
          jwk.crv = name;
          jwk.x = base64UrlEncode(key.handle.pub);
          if (key.type === 'private') jwk.d = base64UrlEncode(key.handle.priv);
          return jwk;
        }

        throw notSupportedError(`The ${format} format is not supported`);
      },
      getPublicKey: (key) => okpPublicHandle(key.handle),
      publicUsages,
    });
  }

  function defineEdDsa(name, params) {
    defineOkp(name, ['verify'], ['sign'], 'sig', [name, 'EdDSA'], { sign: params, verify: params }, {
      sign(normalizedAlgorithm, key, message) {
        requireKeyType(key, 'private');
        const context = normalizedAlgorithm.context ?? new Uint8Array_(0);
        if (context.length > 255) throw operationError('The context must not be longer than 255 bytes');
        return hooks.okpSign(name, key.handle.priv, message, context);
      },
      verify(normalizedAlgorithm, key, message, signature) {
        requireKeyType(key, 'public');
        const context = normalizedAlgorithm.context ?? new Uint8Array_(0);
        if (context.length > 255) throw operationError('The context must not be longer than 255 bytes');
        return hooks.okpVerify(name, key.handle.pub, message, signature, context);
      },
    });
  }

  function defineXdh(name) {
    defineOkp(name, [], ['deriveKey', 'deriveBits'], 'enc', null, { deriveBits: 'EcdhKeyDeriveParams' }, {
      deriveBits(normalizedAlgorithm, key, length) {
        const publicKey = checkPeerKey(normalizedAlgorithm, key, length, okpKeyLengths[name] * 8);
        return truncateSecret(hooks.okpDerive(name, key.handle.priv, publicKey.handle.pub), length);
      },
    });
  }

  defineEdDsa('Ed25519', 'Algorithm');
  defineXdh('X25519');
  defineEdDsa('Ed448', 'Ed448Params');
  defineXdh('X448');

  // ------------------------------------------------------------------------------------------------------------------
  // AES-OCB (https://wicg.github.io/webcrypto-modern-algos/#aes-ocb)
  // ------------------------------------------------------------------------------------------------------------------

  function ocbTagLength(normalizedAlgorithm) {
    const tagLength = normalizedAlgorithm.tagLength ?? 128;
    if (tagLength !== 64 && tagLength !== 96 && tagLength !== 128) throw operationError('The tag length must be 64, 96 or 128 bits');
    return tagLength / 8;
  }

  function checkOcbIv(normalizedAlgorithm) {
    if (normalizedAlgorithm.iv.length === 0 || normalizedAlgorithm.iv.length > 15) throw operationError('The iv must be between 1 and 15 bytes');
  }

  defineAes('AES-OCB', encryptionUsages, { encrypt: 'AeadParams', decrypt: 'AeadParams' }, {
    encrypt(normalizedAlgorithm, key, plaintext) {
      checkOcbIv(normalizedAlgorithm);
      const tagLength = ocbTagLength(normalizedAlgorithm);
      return hooks.aesOcb(true, key.handle.secret, normalizedAlgorithm.iv, normalizedAlgorithm.additionalData ?? new Uint8Array_(0), tagLength, plaintext);
    },
    decrypt(normalizedAlgorithm, key, ciphertext) {
      checkOcbIv(normalizedAlgorithm);
      const tagLength = ocbTagLength(normalizedAlgorithm);
      if (ciphertext.length < tagLength) throw operationError('The ciphertext is shorter than the tag');
      const plaintext = hooks.aesOcb(false, key.handle.secret, normalizedAlgorithm.iv, normalizedAlgorithm.additionalData ?? new Uint8Array_(0), tagLength, ciphertext);
      if (plaintext === null) throw operationError('The authentication tag is invalid');
      return plaintext;
    },
  });

  // ------------------------------------------------------------------------------------------------------------------
  // Argon2 (https://wicg.github.io/webcrypto-modern-algos/#argon2)
  // ------------------------------------------------------------------------------------------------------------------

  for (const name of ['Argon2d', 'Argon2i', 'Argon2id']) {
    defineAlgorithm(name, { deriveBits: 'Argon2Params', importKey: 'Algorithm', 'get key length': 'Algorithm' }, {
      deriveBits(normalizedAlgorithm, key, length) {
        if (length === null || length % 8 !== 0 || length < 32) throw operationError('The length must be a multiple of 8 bits, at least 32 bits');
        if (normalizedAlgorithm.version !== undefined && normalizedAlgorithm.version !== 0x13) throw operationError('The Argon2 version must be 19');
        const { nonce, parallelism, memory, passes } = normalizedAlgorithm;
        if (parallelism === 0 || parallelism > 0xFFFFFF) throw operationError('The parallelism must be between 1 and 2^24-1');
        if (memory < 8 * parallelism) throw operationError('The memory must be at least 8 times the parallelism');
        if (passes === 0) throw operationError('The number of passes must not be zero');
        return hooks.argon2(name, key.handle.secret, nonce, parallelism, memory, passes, 0x13, normalizedAlgorithm.secretValue ?? new Uint8Array_(0), normalizedAlgorithm.associatedData ?? new Uint8Array_(0), length / 8);
      },
      importKey(format, keyData, normalizedAlgorithm, extractable, keyUsages) {
        if (format !== 'raw-secret') throw notSupportedError(`The ${format} format is not supported`);
        checkUsages(keyUsages, ['deriveKey', 'deriveBits']);
        if (extractable !== false) throw syntaxError(`A ${name} key cannot be extractable`);
        return createKey('secret', extractable, keyAlgorithm(name), keyUsages, secretHandle(keyData));
      },
      getKeyLength: () => null,
    });
  }

  // ------------------------------------------------------------------------------------------------------------------
  // SHA-3, cSHAKE, TurboSHAKE, KangarooTwelve (https://wicg.github.io/webcrypto-modern-algos/#sha3)
  // ------------------------------------------------------------------------------------------------------------------

  for (const name of ['SHA3-256', 'SHA3-384', 'SHA3-512']) {
    defineAlgorithm(name, { digest: 'Algorithm' }, {
      digest: (normalizedAlgorithm, message) => hooks.digest(normalizedAlgorithm.name, message),
    });
  }

  function xofOutputLength(normalizedAlgorithm) {
    const outputLength = normalizedAlgorithm.outputLength;
    if (outputLength === 0 || outputLength % 8 !== 0) throw operationError('The output length must be a non-zero multiple of 8 bits');
    return outputLength / 8;
  }

  const empty = () => new Uint8Array_(0);

  // The output of cSHAKE can be empty (https://wicg.github.io/webcrypto-modern-algos/#cshake-operations-digest)
  function cShakeOutputLength(normalizedAlgorithm) {
    if (normalizedAlgorithm.outputLength % 8 !== 0) throw operationError('The output length must be a multiple of 8 bits');
    return normalizedAlgorithm.outputLength / 8;
  }

  for (const name of ['cSHAKE128', 'cSHAKE256']) {
    defineAlgorithm(name, { digest: 'CShakeParams' }, {
      digest(normalizedAlgorithm, message) {
        const length = cShakeOutputLength(normalizedAlgorithm);
        if (length === 0) return empty();
        return hooks.xof(name, message, length, normalizedAlgorithm.functionName ?? empty(), normalizedAlgorithm.customization ?? empty(), 0);
      },
      checkDigest: cShakeOutputLength,
    });
  }

  for (const name of ['TurboSHAKE128', 'TurboSHAKE256']) {
    const domainSeparation = (normalizedAlgorithm) => {
      const value = normalizedAlgorithm.domainSeparation ?? 0x1F;
      if (value < 0x01 || value > 0x7F) throw operationError('The domain separation byte must be between 0x01 and 0x7F');
      return value;
    };
    defineAlgorithm(name, { digest: 'TurboShakeParams' }, {
      digest(normalizedAlgorithm, message) {
        const length = xofOutputLength(normalizedAlgorithm);
        return hooks.xof(name, message, length, empty(), empty(), domainSeparation(normalizedAlgorithm));
      },
      checkDigest(normalizedAlgorithm) {
        xofOutputLength(normalizedAlgorithm);
        domainSeparation(normalizedAlgorithm);
      },
    });
  }

  for (const name of ['KT128', 'KT256']) {
    defineAlgorithm(name, { digest: 'KangarooTwelveParams' }, {
      digest(normalizedAlgorithm, message) {
        const length = xofOutputLength(normalizedAlgorithm);
        return hooks.xof(name, message, length, empty(), normalizedAlgorithm.customization ?? empty(), 0);
      },
      checkDigest: xofOutputLength,
    });
  }

  // ------------------------------------------------------------------------------------------------------------------
  // KMAC (https://wicg.github.io/webcrypto-modern-algos/#kmac)
  // ------------------------------------------------------------------------------------------------------------------

  for (const name of ['KMAC128', 'KMAC256']) {
    const jwkAlg = name === 'KMAC128' ? 'K128' : 'K256';
    const defaultLength = name === 'KMAC128' ? 128 : 256;
    const mac = (normalizedAlgorithm, key, message) => {
      const outputLength = normalizedAlgorithm.outputLength;
      if (outputLength % 8 !== 0) throw operationError('The output length must be a multiple of 8 bits');
      return hooks.kmac(name, key.handle.secret, message, outputLength / 8, normalizedAlgorithm.customization ?? empty());
    };
    defineAlgorithm(name, { sign: 'KmacParams', verify: 'KmacParams', generateKey: 'KmacKeyGenParams', importKey: 'KmacImportParams', exportKey: 'Algorithm', 'get key length': 'KmacImportParams' }, {
      sign: mac,
      verify: (normalizedAlgorithm, key, message, signature) => bytesEqual(mac(normalizedAlgorithm, key, message), signature),
      generateKey(normalizedAlgorithm, extractable, keyUsages) {
        checkUsages(keyUsages, ['sign', 'verify']);
        const length = normalizedAlgorithm.length ?? defaultLength;
        const algorithm = keyAlgorithm(name);
        algorithm.length = length;
        return createKey('secret', extractable, algorithm, normalizeUsages(keyUsages), secretHandle(truncateBits(hooks.randomBytes((length + 7) >> 3), length)));
      },
      importKey(format, keyData, normalizedAlgorithm, extractable, keyUsages) {
        checkUsages(keyUsages, ['sign', 'verify']);
        let data;
        if (format === 'raw-secret') {
          data = keyData;
        } else if (format === 'jwk') {
          if (keyData.kty !== 'oct') throw dataError("The kty member of the JSON Web Key must be 'oct'");
          data = decodeJwkMember(keyData, 'k');
          if (keyData.alg !== undefined && keyData.alg !== jwkAlg) throw dataError(`The alg member of the JSON Web Key must be '${jwkAlg}'`);
          checkJwkCommon(keyData, keyUsages, extractable, 'sig');
        } else {
          throw notSupportedError(`The ${format} format is not supported`);
        }

        let length = data.length * 8;
        if (normalizedAlgorithm.length !== undefined) {
          if (normalizedAlgorithm.length > length) throw dataError('The KMAC key length is greater than the length of the data');
          if (normalizedAlgorithm.length <= length - 8) throw dataError('The KMAC key length is too short for the data');
          length = normalizedAlgorithm.length;
        }

        const algorithm = keyAlgorithm(name);
        algorithm.length = length;
        return createKey('secret', extractable, algorithm, keyUsages, secretHandle(truncateBits(data, length)));
      },
      exportKey(format, key) {
        if (format === 'raw') throw notSupportedError('The raw format is not supported');
        return exportSecretKey(format, key, jwkAlg);
      },
      getKeyLength: (normalizedAlgorithm) => normalizedAlgorithm.length ?? defaultLength,
    });
  }

  // ------------------------------------------------------------------------------------------------------------------
  // ChaCha20-Poly1305 (https://wicg.github.io/webcrypto-modern-algos/#chacha20-poly1305)
  // ------------------------------------------------------------------------------------------------------------------

  function checkChaChaParams(normalizedAlgorithm) {
    if (normalizedAlgorithm.iv.length !== 12) throw operationError('The iv must be 12 bytes');
    if (normalizedAlgorithm.tagLength !== undefined && normalizedAlgorithm.tagLength !== 128) throw operationError('The tag length must be 128 bits');
  }

  defineAlgorithm('ChaCha20-Poly1305', { encrypt: 'AeadParams', decrypt: 'AeadParams', generateKey: 'Algorithm', importKey: 'Algorithm', exportKey: 'Algorithm', 'get key length': 'Algorithm' }, {
    encrypt(normalizedAlgorithm, key, plaintext) {
      checkChaChaParams(normalizedAlgorithm);
      return hooks.chacha20Poly1305(true, key.handle.secret, normalizedAlgorithm.iv, normalizedAlgorithm.additionalData ?? empty(), plaintext);
    },
    decrypt(normalizedAlgorithm, key, ciphertext) {
      checkChaChaParams(normalizedAlgorithm);
      if (ciphertext.length < 16) throw operationError('The ciphertext is shorter than the tag');
      const plaintext = hooks.chacha20Poly1305(false, key.handle.secret, normalizedAlgorithm.iv, normalizedAlgorithm.additionalData ?? empty(), ciphertext);
      if (plaintext === null) throw operationError('The authentication tag is invalid');
      return plaintext;
    },
    checkEncrypt: checkChaChaParams,
    generateKey(normalizedAlgorithm, extractable, keyUsages) {
      checkUsages(keyUsages, encryptionUsages);
      return createKey('secret', extractable, keyAlgorithm('ChaCha20-Poly1305'), normalizeUsages(keyUsages), secretHandle(hooks.randomBytes(32)));
    },
    importKey(format, keyData, normalizedAlgorithm, extractable, keyUsages) {
      checkUsages(keyUsages, encryptionUsages);
      let data;
      if (format === 'raw-secret') {
        data = keyData;
        if (data.length !== 32) throw dataError('The ChaCha20-Poly1305 key must be 256 bits');
      } else if (format === 'jwk') {
        if (keyData.kty !== 'oct') throw dataError("The kty member of the JSON Web Key must be 'oct'");
        data = decodeJwkMember(keyData, 'k');
        if (data.length !== 32) throw dataError('The ChaCha20-Poly1305 key must be 256 bits');
        if (keyData.alg !== undefined && keyData.alg !== 'C20P') throw dataError("The alg member of the JSON Web Key must be 'C20P'");
        checkJwkCommon(keyData, keyUsages, extractable, 'enc');
      } else {
        throw notSupportedError(`The ${format} format is not supported`);
      }

      return createKey('secret', extractable, keyAlgorithm('ChaCha20-Poly1305'), keyUsages, secretHandle(data));
    },
    exportKey(format, key) {
      if (format === 'raw') throw notSupportedError('The raw format is not supported');
      return exportSecretKey(format, key, 'C20P');
    },
    getKeyLength: () => 256,
  });

  // ------------------------------------------------------------------------------------------------------------------
  // ML-KEM (https://wicg.github.io/webcrypto-modern-algos/#ml-kem), ML-DSA (https://wicg.github.io/webcrypto-modern-algos/#ml-dsa)
  // and the hybrid KEMs (https://wicg.github.io/webcrypto-modern-algos/#hybrid-kems): the "AKP" keys of RFC 9964
  // ------------------------------------------------------------------------------------------------------------------

  function seedPublicHandle(handle) {
    const result = ObjectCreate(null);
    result.pub = handle.pub;
    return result;
  }

  // keys: the hooks of the key family ({ generate, fromSeed, importAsn1 (or null), exportAsn1, publicKeyLength, seedLength })
  function defineAkp(name, publicUsages, privateUsages, expectedUse, operations, implementation, keys) {
    defineAlgorithm(name, { generateKey: 'Algorithm', importKey: 'Algorithm', exportKey: 'Algorithm', ...operations }, {
      ...implementation,
      generateKey(normalizedAlgorithm, extractable, keyUsages) {
        checkUsages(keyUsages, [...publicUsages, ...privateUsages]);
        const handle = keys.generate(name);
        const algorithm = keyAlgorithm(name);
        return keyPair(
          createKey('public', true, algorithm, usageIntersection(keyUsages, publicUsages), seedPublicHandle(handle)),
          createKey('private', extractable, algorithm, usageIntersection(keyUsages, privateUsages), handle));
      },
      importKey(format, keyData, normalizedAlgorithm, extractable, keyUsages) {
        let handle, type;
        if ((format === 'spki' || format === 'pkcs8') && keys.importAsn1 !== null) {
          checkUsages(keyUsages, format === 'spki' ? publicUsages : privateUsages);
          handle = keys.importAsn1(name, format, keyData);
          type = format === 'spki' ? 'public' : 'private';
        } else if (format === 'raw-public') {
          checkUsages(keyUsages, publicUsages);
          if (keyData.length !== keys.publicKeyLength(name)) throw dataError('The public key does not have the length of the algorithm');
          handle = ObjectCreate(null);
          handle.pub = keyData;
          type = 'public';
        } else if (format === 'raw-seed') {
          checkUsages(keyUsages, privateUsages);
          if (keyData.length !== keys.seedLength(name)) throw dataError('The seed does not have the length of the algorithm');
          handle = keys.fromSeed(name, keyData);
          type = 'private';
        } else if (format === 'jwk') {
          checkUsages(keyUsages, keyData.priv !== undefined ? privateUsages : publicUsages);
          if (keyData.kty !== 'AKP') throw dataError("The kty member of the JSON Web Key must be 'AKP'");
          if (keyData.alg !== name) throw dataError(`The alg member of the JSON Web Key must be '${name}'`);
          checkJwkCommon(keyData, keyUsages, extractable, expectedUse);
          const pub = decodeJwkMember(keyData, 'pub');
          if (keyData.priv !== undefined) {
            const seed = decodeJwkMember(keyData, 'priv');
            if (seed.length !== keys.seedLength(name)) throw dataError('The priv member of the JSON Web Key does not have the length of a seed');
            handle = keys.fromSeed(name, seed);
            if (!bytesEqual(handle.pub, pub)) throw dataError('The pub member of the JSON Web Key does not match the private key');
            type = 'private';
          } else {
            if (pub.length !== keys.publicKeyLength(name)) throw dataError('The pub member of the JSON Web Key does not have the length of a public key');
            handle = ObjectCreate(null);
            handle.pub = pub;
            type = 'public';
          }
        } else {
          throw notSupportedError(`The ${format} format is not supported`);
        }

        if (type === 'public') handle = seedPublicHandle(handle);
        return createKey(type, extractable, keyAlgorithm(name), keyUsages, handle);
      },
      exportKey(format, key) {
        if ((format === 'spki' && keys.importAsn1 !== null) || format === 'raw-public') {
          if (key.type !== 'public') throw invalidAccessError(`Only a public key can be exported in the ${format} format`);
          return format === 'spki' ? keys.exportAsn1(name, 'spki', key.handle.pub) : key.handle.pub;
        }

        if ((format === 'pkcs8' && keys.importAsn1 !== null) || format === 'raw-seed') {
          if (key.type !== 'private') throw invalidAccessError(`Only a private key can be exported in the ${format} format`);
          return format === 'pkcs8' ? keys.exportAsn1(name, 'pkcs8', key.handle.seed) : key.handle.seed;
        }

        if (format === 'jwk') {
          const jwk = newJwk('AKP', key);
          jwk.alg = name;
          jwk.pub = base64UrlEncode(key.handle.pub);
          if (key.type === 'private') jwk.priv = base64UrlEncode(key.handle.seed);
          return jwk;
        }

        throw notSupportedError(`The ${format} format is not supported`);
      },
      getPublicKey: (key) => seedPublicHandle(key.handle),
      publicUsages,
    });
  }

  const postQuantumKeys = {
    generate: (name) => hooks.pqGenerate(name),
    fromSeed: (name, seed) => hooks.pqFromSeed(name, seed),
    importAsn1: (name, format, keyData) => hooks.pqImport(name, format, keyData),
    exportAsn1: (name, format, keyData) => hooks.pqExport(name, format, keyData),
    publicKeyLength: (name) => hooks.pqPublicKeyLength(name),
    seedLength: (name) => name.startsWith('ML-KEM') ? 64 : 32,
  };

  const kemPublicUsages = ['encapsulateKey', 'encapsulateBits'];
  const kemPrivateUsages = ['decapsulateKey', 'decapsulateBits'];

  for (const name of ['ML-KEM-512', 'ML-KEM-768', 'ML-KEM-1024']) {
    defineAkp(name, kemPublicUsages, kemPrivateUsages, 'enc', { encapsulate: 'Algorithm', decapsulate: 'Algorithm', 'get shared key length': 'Algorithm' }, {
      encapsulate(normalizedAlgorithm, key) {
        requireKeyType(key, 'public');
        return hooks.pqEncapsulate(name, key.handle.pub);
      },
      decapsulate(normalizedAlgorithm, key, ciphertext) {
        requireKeyType(key, 'private');
        return hooks.pqDecapsulate(name, key.handle.seed, ciphertext);
      },
      getSharedKeyLength: () => 256,
    }, postQuantumKeys);
  }

  function checkContext(normalizedAlgorithm) {
    const context = normalizedAlgorithm.context ?? empty();
    if (context.length > 255) throw operationError('The context must not be longer than 255 bytes');
    return context;
  }

  for (const name of ['ML-DSA-44', 'ML-DSA-65', 'ML-DSA-87']) {
    defineAkp(name, ['verify'], ['sign'], 'sig', { sign: 'ContextParams', verify: 'ContextParams' }, {
      sign(normalizedAlgorithm, key, message) {
        const context = checkContext(normalizedAlgorithm);
        requireKeyType(key, 'private');
        return hooks.pqSign(name, key.handle.seed, message, context);
      },
      verify(normalizedAlgorithm, key, message, signature) {
        const context = checkContext(normalizedAlgorithm);
        requireKeyType(key, 'public');
        return hooks.pqVerify(name, key.handle.pub, message, signature, context);
      },
      checkSign: checkContext,
    }, postQuantumKeys);
  }

  {
    const hybridKeys = {
      generate: (name) => hooks.hybridFromSeed(name, hooks.randomBytes(32)),
      fromSeed: (name, seed) => hooks.hybridFromSeed(name, seed),
      importAsn1: null,
      exportAsn1: null,
      publicKeyLength: (name) => hooks.hybridPublicKeyLength(name),
      seedLength: () => 32,
    };

    for (const name of ['MLKEM768-P256', 'MLKEM768-X25519', 'MLKEM1024-P384']) {
      defineAkp(name, kemPublicUsages, kemPrivateUsages, 'enc', { encapsulate: 'Algorithm', decapsulate: 'Algorithm', 'get shared key length': 'Algorithm' }, {
        encapsulate(normalizedAlgorithm, key) {
          requireKeyType(key, 'public');
          return hooks.hybridEncapsulate(name, key.handle.pub);
        },
        decapsulate(normalizedAlgorithm, key, ciphertext) {
          requireKeyType(key, 'private');
          return hooks.hybridDecapsulate(name, key.handle.seed, ciphertext);
        },
        getSharedKeyLength: () => 256,
      }, hybridKeys);
    }
  }

  // ------------------------------------------------------------------------------------------------------------------
  // Checking support for an algorithm (https://wicg.github.io/webcrypto-modern-algos/#checking-support-for-an-algorithm)
  // ------------------------------------------------------------------------------------------------------------------

  const supportedOperations = ['encrypt', 'decrypt', 'sign', 'verify', 'digest', 'generateKey', 'deriveKey', 'deriveBits', 'importKey', 'exportKey', 'wrapKey', 'unwrapKey', 'encapsulateKey', 'encapsulateBits', 'decapsulateKey', 'decapsulateBits', 'getPublicKey'];

  // The second overload of supports(): the additional algorithm is the derived, wrapped, unwrapped or shared key
  function supportsWithAdditionalAlgorithm(operation, algorithm, additionalAlgorithm) {
    if ((operation === 'deriveKey' || operation === 'unwrapKey') && !checkSupport('importKey', additionalAlgorithm, null)) return false;
    if (operation === 'wrapKey' && !checkSupport('exportKey', additionalAlgorithm, null)) return false;
    if (operation === 'encapsulateKey' || operation === 'decapsulateKey') {
      const normalizedAlgorithm = tryNormalizeAlgorithm(algorithm, 'get shared key length');
      if (normalizedAlgorithm === undefined) return false;
      const sharedKeyLength = getOperation(normalizedAlgorithm.name, 'getSharedKeyLength')(normalizedAlgorithm);
      const normalizedAdditionalAlgorithm = tryNormalizeAlgorithm(additionalAlgorithm, 'importKey');
      if (normalizedAdditionalAlgorithm === undefined) return false;
      if (!determineSupport('importKey', normalizedAdditionalAlgorithm, null)) return false;
      const implementation = implementations[normalizedAdditionalAlgorithm.name];
      if (implementation.acceptsSecret === undefined || !implementation.acceptsSecret(normalizedAdditionalAlgorithm, sharedKeyLength)) return false;
    }

    let length = null;
    if (operation === 'deriveKey') {
      if (!checkSupport('get key length', additionalAlgorithm, null)) return false;
      const normalizedAdditionalAlgorithm = normalizeAlgorithm(additionalAlgorithm, 'get key length');
      length = getOperation(normalizedAdditionalAlgorithm.name, 'getKeyLength')(normalizedAdditionalAlgorithm);
      operation = 'deriveBits';
    }

    return checkSupport(operation, algorithm, length);
  }

  // https://wicg.github.io/webcrypto-modern-algos/#dfn-check-support-for-an-algorithm
  function checkSupport(op, alg, length) {
    if (op === 'encapsulateKey' || op === 'encapsulateBits') op = 'encapsulate';
    if (op === 'decapsulateKey' || op === 'decapsulateBits') op = 'decapsulate';
    if (op === 'getPublicKey') {
      const normalizedAlgorithm = tryNormalizeAlgorithm(alg, 'exportKey');
      return normalizedAlgorithm !== undefined && implementations[normalizedAlgorithm.name].getPublicKey !== undefined;
    }

    // deriveKey is checked as deriveBits (the first overload, without the derived key type)
    const normalizedOp = op === 'deriveKey' ? 'deriveBits' : op;
    const normalizedAlgorithm = tryNormalizeAlgorithm(alg, normalizedOp);
    if (normalizedAlgorithm === undefined) {
      if (op === 'wrapKey') return checkSupport('encrypt', alg, length);
      if (op === 'unwrapKey') return checkSupport('decrypt', alg, length);
      return false;
    }

    return determineSupport(normalizedOp, normalizedAlgorithm, length);
  }

  // https://wicg.github.io/webcrypto-modern-algos/#dfn-determine-support-from-operation-steps: the steps of the operation
  // are run until a step needs a key or data (supported) or throws (not supported). The implementations define these
  // steps as check functions (checkEncrypt, checkGenerateKey...); an operation without check function is supported.
  function determineSupport(op, normalizedAlgorithm, length) {
    const implementation = implementations[normalizedAlgorithm.name];
    const check = {
      encrypt: implementation.checkEncrypt,
      decrypt: implementation.checkDecrypt ?? implementation.checkEncrypt,
      sign: implementation.checkSign,
      verify: implementation.checkSign,
      digest: implementation.checkDigest,
      deriveBits: implementation.checkDeriveBits,
      generateKey: implementation.checkGenerateKey,
      importKey: implementation.checkImportKey,
      'get key length': implementation.getKeyLength,
    }[op];
    if (check === undefined) return true;
    try {
      check(normalizedAlgorithm, length);
      return true;
    } catch {
      return false;
    }
  }

  // The checks of the operations that do not need a key or data
  function defineChecks(names, checks) {
    for (const name of names) {
      const implementation = implementations[name];
      if (implementation === undefined) continue;
      for (const key of ownKeys(checks)) implementation[key] = checks[key];
    }
  }

  const aesNames = ['AES-CTR', 'AES-CBC', 'AES-GCM', 'AES-KW', 'AES-OCB'];
  defineChecks(aesNames, {
    checkGenerateKey: (normalizedAlgorithm) => aesKeyLength(normalizedAlgorithm.length),
    acceptsSecret: (normalizedAlgorithm, bits) => bits === 128 || bits === 192 || bits === 256,
  });
  defineChecks(['AES-CTR'], {
    checkEncrypt(normalizedAlgorithm) {
      if (normalizedAlgorithm.counter.length !== 16) throw operationError('The counter must be 16 bytes');
      if (normalizedAlgorithm.length === 0 || normalizedAlgorithm.length > 128) throw operationError('The counter length must be between 1 and 128 bits');
    },
  });
  defineChecks(['AES-CBC'], {
    checkEncrypt(normalizedAlgorithm) {
      if (normalizedAlgorithm.iv.length !== 16) throw operationError('The iv must be 16 bytes');
    },
  });
  defineChecks(['AES-GCM'], { checkEncrypt: gcmTagLength });
  defineChecks(['AES-OCB'], {
    checkEncrypt(normalizedAlgorithm) {
      checkOcbIv(normalizedAlgorithm);
      ocbTagLength(normalizedAlgorithm);
    },
  });
  defineChecks(['ChaCha20-Poly1305'], { acceptsSecret: (normalizedAlgorithm, bits) => bits === 256 });

  // HMAC and KMAC: the length of an imported key is its length in bits, or the length member (at most 7 bits shorter)
  const acceptsMacSecret = (normalizedAlgorithm, bits) => bits !== 0 && (normalizedAlgorithm.length === undefined || (normalizedAlgorithm.length <= bits && normalizedAlgorithm.length > bits - 8));
  defineChecks(['HMAC'], {
    checkGenerateKey(normalizedAlgorithm) {
      checkHash(normalizedAlgorithm.hash);
      if (normalizedAlgorithm.length === 0) throw operationError('The HMAC key length must not be zero');
    },
    checkImportKey(normalizedAlgorithm) {
      if (normalizedAlgorithm.length === 0) throw dataError('The HMAC key length must not be zero');
      checkHash(normalizedAlgorithm.hash);
    },
    acceptsSecret: acceptsMacSecret,
  });
  defineChecks(['KMAC128', 'KMAC256'], { acceptsSecret: acceptsMacSecret });

  const acceptsAnySecret = () => true;
  defineChecks(['HKDF'], {
    checkDeriveBits(normalizedAlgorithm, length) {
      if (length === null || length % 8 !== 0) throw operationError('The length must be a multiple of 8 bits');
      if (length > 255 * hashLengths[checkHash(normalizedAlgorithm.hash)]) throw operationError('The length is too large');
    },
    acceptsSecret: acceptsAnySecret,
  });
  defineChecks(['PBKDF2'], {
    checkDeriveBits(normalizedAlgorithm, length) {
      if (length === null || length % 8 !== 0) throw operationError('The length must be a multiple of 8 bits');
      if (normalizedAlgorithm.iterations === 0) throw operationError('The number of iterations must not be zero');
      checkHash(normalizedAlgorithm.hash);
    },
    acceptsSecret: acceptsAnySecret,
  });
  defineChecks(['Argon2d', 'Argon2i', 'Argon2id'], {
    checkDeriveBits(normalizedAlgorithm, length) {
      if (length === null || length % 8 !== 0 || length < 32) throw operationError('The length must be a multiple of 8 bits, at least 32 bits');
      if (normalizedAlgorithm.version !== undefined && normalizedAlgorithm.version !== 0x13) throw operationError('The Argon2 version must be 19');
      if (normalizedAlgorithm.parallelism === 0 || normalizedAlgorithm.parallelism > 0xFFFFFF) throw operationError('The parallelism is invalid');
      if (normalizedAlgorithm.memory < 8 * normalizedAlgorithm.parallelism) throw operationError('The memory is too small');
      if (normalizedAlgorithm.passes === 0) throw operationError('The number of passes must not be zero');
    },
    acceptsSecret: acceptsAnySecret,
  });

  // https://w3c.github.io/webcrypto/#rsa-keygen-params-validation, with the moduli supported by the implementation
  function checkRsaKeyGeneration(normalizedAlgorithm) {
    checkHash(normalizedAlgorithm.hash);
    const modulusLength = normalizedAlgorithm.modulusLength;
    let publicExponent = 0n;
    for (const b of normalizedAlgorithm.publicExponent) publicExponent = (publicExponent << 8n) | BigInt(b);
    if (modulusLength < 256 || modulusLength > 16384 || modulusLength % 8 !== 0) throw operationError('The RSA modulus length is not supported');
    if (publicExponent < 3n || publicExponent % 2n === 0n || publicExponent >= (1n << BigInt(modulusLength)) - 1n) throw operationError('The RSA public exponent is invalid');
  }

  defineChecks(['RSASSA-PKCS1-v1_5', 'RSA-PSS', 'RSA-OAEP'], {
    checkGenerateKey: checkRsaKeyGeneration,
    checkImportKey: (normalizedAlgorithm) => checkHash(normalizedAlgorithm.hash),
  });

  function checkNamedCurve(normalizedAlgorithm) {
    if (!apply(arrayIncludes, namedCurves, [normalizedAlgorithm.namedCurve])) throw notSupportedError('The named curve is not supported');
  }

  defineChecks(['ECDSA', 'ECDH'], { checkGenerateKey: checkNamedCurve, checkImportKey: checkNamedCurve });
  defineChecks(['ECDSA'], { checkSign: (normalizedAlgorithm) => checkHash(normalizedAlgorithm.hash) });
  defineChecks(['ECDH'], {
    checkDeriveBits: (normalizedAlgorithm, length) => checkPeerKey(normalizedAlgorithm, null, length, ecFieldLengths[normalizedAlgorithm.public.algorithm.namedCurve] ?? 0),
  });
  defineChecks(['X25519', 'X448'], {
    checkDeriveBits: (normalizedAlgorithm, length) => checkPeerKey(normalizedAlgorithm, null, length, okpKeyLengths[normalizedAlgorithm.name] * 8),
  });
  defineChecks(['Ed448', 'ML-DSA-44', 'ML-DSA-65', 'ML-DSA-87'], { checkSign: checkContext });

  // ------------------------------------------------------------------------------------------------------------------
  // Promises and tasks
  // ------------------------------------------------------------------------------------------------------------------

  function newPromise() {
    const deferred = ObjectCreate(null);
    deferred.promise = new Promise_((resolve, reject) => {
      deferred.resolve = resolve;
      deferred.reject = reject;
    });
    return deferred;
  }

  // Runs the steps of a method that returns a promise: the synchronous steps (the conversions and the normalization: their
  // errors reject the promise), then the steps "in parallel" (performed synchronously), whose result or error settles the
  // promise in a task queued on the crypto task source. The result of the parallel steps is a function that returns the
  // value of the promise, called in the task (e.g. the ArrayBuffer is created in the task).
  function runMethod(synchronousSteps) {
    let parallelSteps;
    try {
      parallelSteps = synchronousSteps();
    } catch (e) {
      return apply(Promise_.reject, Promise_, [e]);
    }

    const deferred = newPromise();
    let settle;
    try {
      const result = parallelSteps();
      settle = () => deferred.resolve(result());
    } catch (e) {
      settle = () => deferred.reject(e);
    }

    hooks.queueTask(settle);
    return deferred.promise;
  }

  const resultBuffer = (bytes) => () => toArrayBuffer(bytes);

  function checkKeyAlgorithm(normalizedAlgorithm, key, usage) {
    if (normalizedAlgorithm.name !== key.algorithm.name) throw invalidAccessError(`The key is not a ${normalizedAlgorithm.name} key`);
    if (!hasUsage(key, usage)) throw invalidAccessError(`The key does not support the '${usage}' operation`);
  }

  // https://w3c.github.io/webcrypto/#dfn-SubtleCrypto-method-generateKey: the keys of a pair, or a key
  function checkCreatedKeyUsages(result) {
    if (result.privateKey !== undefined) {
      if (result.privateKey.usages.length === 0) throw syntaxError('The usages of the private key must not be empty');
    } else if ((result.type === 'secret' || result.type === 'private') && result.usages.length === 0) {
      throw syntaxError('The usages of the key must not be empty');
    }
  }

  function keyResult(result) {
    if (result.privateKey !== undefined) {
      return () => {
        const pair = {};
        pair.publicKey = result.publicKey.object;
        pair.privateKey = result.privateKey.object;
        return pair;
      };
    }

    return () => result.object;
  }

  // The import key operation: the normalized usages and the extractable flag are set on the key
  function importKeyRecord(normalizedAlgorithm, format, keyData, extractable, keyUsages, checkEmptyUsages = true) {
    const result = getOperation(normalizedAlgorithm.name, 'importKey')(format, keyData, normalizedAlgorithm, extractable, keyUsages);
    if (checkEmptyUsages && (result.type === 'secret' || result.type === 'private') && keyUsages.length === 0) throw syntaxError('The usages of the key must not be empty');
    result.extractable = extractable;
    result.usages = normalizeUsages(keyUsages);
    return result;
  }

  function exportKeyRecord(format, key) {
    const implementation = getImplementation(key.algorithm.name);
    if (implementation === undefined || implementation.exportKey === undefined) throw notSupportedError(`The ${key.algorithm.name} algorithm does not support the export key operation`);
    if (!key.extractable) throw invalidAccessError('The key is not extractable');
    return implementation.exportKey(format, key);
  }

  // ------------------------------------------------------------------------------------------------------------------
  // SubtleCrypto (https://w3c.github.io/webcrypto/#subtlecrypto-interface)
  // ------------------------------------------------------------------------------------------------------------------

  class SubtleCrypto {
    constructor() {
      throw illegalConstructor();
    }

    encrypt(algorithm, key, data) {
      const count = arguments.length;
      return runMethod(() => {
        thisRecord(this, 'SubtleCrypto');
        requireArguments(count, 3, 'encrypt');
        algorithm = convertAlgorithmIdentifier(algorithm);
        key = convertCryptoKey(key, 'key');
        data = convertBufferSource(data, 'data');
        const normalizedAlgorithm = normalizeAlgorithm(algorithm, 'encrypt');
        const bytes = copyBytes(data);
        return () => {
          checkKeyAlgorithm(normalizedAlgorithm, key, 'encrypt');
          return resultBuffer(getOperation(normalizedAlgorithm.name, 'encrypt')(normalizedAlgorithm, key, bytes));
        };
      });
    }

    decrypt(algorithm, key, data) {
      const count = arguments.length;
      return runMethod(() => {
        thisRecord(this, 'SubtleCrypto');
        requireArguments(count, 3, 'decrypt');
        algorithm = convertAlgorithmIdentifier(algorithm);
        key = convertCryptoKey(key, 'key');
        data = convertBufferSource(data, 'data');
        const normalizedAlgorithm = normalizeAlgorithm(algorithm, 'decrypt');
        const bytes = copyBytes(data);
        return () => {
          checkKeyAlgorithm(normalizedAlgorithm, key, 'decrypt');
          return resultBuffer(getOperation(normalizedAlgorithm.name, 'decrypt')(normalizedAlgorithm, key, bytes));
        };
      });
    }

    sign(algorithm, key, data) {
      const count = arguments.length;
      return runMethod(() => {
        thisRecord(this, 'SubtleCrypto');
        requireArguments(count, 3, 'sign');
        algorithm = convertAlgorithmIdentifier(algorithm);
        key = convertCryptoKey(key, 'key');
        data = convertBufferSource(data, 'data');
        const normalizedAlgorithm = normalizeAlgorithm(algorithm, 'sign');
        const bytes = copyBytes(data);
        return () => {
          checkKeyAlgorithm(normalizedAlgorithm, key, 'sign');
          return resultBuffer(getOperation(normalizedAlgorithm.name, 'sign')(normalizedAlgorithm, key, bytes));
        };
      });
    }

    verify(algorithm, key, signature, data) {
      const count = arguments.length;
      return runMethod(() => {
        thisRecord(this, 'SubtleCrypto');
        requireArguments(count, 4, 'verify');
        algorithm = convertAlgorithmIdentifier(algorithm);
        key = convertCryptoKey(key, 'key');
        signature = convertBufferSource(signature, 'signature');
        data = convertBufferSource(data, 'data');
        const normalizedAlgorithm = normalizeAlgorithm(algorithm, 'verify');
        const signatureBytes = copyBytes(signature);
        const bytes = copyBytes(data);
        return () => {
          checkKeyAlgorithm(normalizedAlgorithm, key, 'verify');
          const result = getOperation(normalizedAlgorithm.name, 'verify')(normalizedAlgorithm, key, bytes, signatureBytes);
          return () => result;
        };
      });
    }

    digest(algorithm, data) {
      const count = arguments.length;
      return runMethod(() => {
        thisRecord(this, 'SubtleCrypto');
        requireArguments(count, 2, 'digest');
        algorithm = convertAlgorithmIdentifier(algorithm);
        data = convertBufferSource(data, 'data');
        const normalizedAlgorithm = normalizeAlgorithm(algorithm, 'digest');
        const bytes = copyBytes(data);
        return () => resultBuffer(getOperation(normalizedAlgorithm.name, 'digest')(normalizedAlgorithm, bytes));
      });
    }

    generateKey(algorithm, extractable, keyUsages) {
      const count = arguments.length;
      return runMethod(() => {
        thisRecord(this, 'SubtleCrypto');
        requireArguments(count, 3, 'generateKey');
        algorithm = convertAlgorithmIdentifier(algorithm);
        extractable = convertBoolean(extractable);
        keyUsages = convertKeyUsages(keyUsages);
        const normalizedAlgorithm = normalizeAlgorithm(algorithm, 'generateKey');
        return () => {
          const result = getOperation(normalizedAlgorithm.name, 'generateKey')(normalizedAlgorithm, extractable, keyUsages);
          checkCreatedKeyUsages(result);
          return keyResult(result);
        };
      });
    }

    deriveKey(algorithm, baseKey, derivedKeyType, extractable, keyUsages) {
      const count = arguments.length;
      return runMethod(() => {
        thisRecord(this, 'SubtleCrypto');
        requireArguments(count, 5, 'deriveKey');
        algorithm = convertAlgorithmIdentifier(algorithm);
        baseKey = convertCryptoKey(baseKey, 'baseKey');
        derivedKeyType = convertAlgorithmIdentifier(derivedKeyType);
        extractable = convertBoolean(extractable);
        keyUsages = convertKeyUsages(keyUsages);
        const normalizedAlgorithm = normalizeAlgorithm(algorithm, 'deriveBits');
        const normalizedDerivedKeyAlgorithmImport = normalizeAlgorithm(derivedKeyType, 'importKey');
        const normalizedDerivedKeyAlgorithmLength = normalizeAlgorithm(derivedKeyType, 'get key length');
        return () => {
          checkKeyAlgorithm(normalizedAlgorithm, baseKey, 'deriveKey');
          const length = getOperation(normalizedDerivedKeyAlgorithmLength.name, 'getKeyLength')(normalizedDerivedKeyAlgorithmLength);
          const secret = getOperation(normalizedAlgorithm.name, 'deriveBits')(normalizedAlgorithm, baseKey, length);
          const result = importKeyRecord(normalizedDerivedKeyAlgorithmImport, 'raw-secret', secret, extractable, keyUsages);
          return keyResult(result);
        };
      });
    }

    deriveBits(algorithm, baseKey, length = null) {
      const count = arguments.length;
      return runMethod(() => {
        thisRecord(this, 'SubtleCrypto');
        requireArguments(count, 2, 'deriveBits');
        algorithm = convertAlgorithmIdentifier(algorithm);
        baseKey = convertCryptoKey(baseKey, 'baseKey');
        length = length === null || length === undefined ? null : convertEnforceRange(length, 0xFFFFFFFF, 'length');
        const normalizedAlgorithm = normalizeAlgorithm(algorithm, 'deriveBits');
        return () => {
          checkKeyAlgorithm(normalizedAlgorithm, baseKey, 'deriveBits');
          return resultBuffer(getOperation(normalizedAlgorithm.name, 'deriveBits')(normalizedAlgorithm, baseKey, length));
        };
      });
    }

    importKey(format, keyData, algorithm, extractable, keyUsages) {
      const count = arguments.length;
      return runMethod(() => {
        thisRecord(this, 'SubtleCrypto');
        requireArguments(count, 5, 'importKey');
        format = convertKeyFormat(format);
        // (BufferSource or JsonWebKey)
        const isJwk = !isBufferSource(keyData);
        if (isJwk) keyData = convertDictionary(keyData, 'JsonWebKey');
        else keyData = convertBufferSource(keyData, 'keyData');
        algorithm = convertAlgorithmIdentifier(algorithm);
        extractable = convertBoolean(extractable);
        keyUsages = convertKeyUsages(keyUsages);
        const normalizedAlgorithm = normalizeAlgorithm(algorithm, 'importKey');
        if (format === 'jwk') {
          if (!isJwk) throw typeError('The key data must be a JsonWebKey dictionary for the jwk format');
        } else {
          if (isJwk) throw typeError(`The key data must be a BufferSource for the ${format} format`);
          keyData = copyBytes(keyData);
        }

        return () => keyResult(importKeyRecord(normalizedAlgorithm, format, keyData, extractable, keyUsages));
      });
    }

    exportKey(format, key) {
      const count = arguments.length;
      return runMethod(() => {
        thisRecord(this, 'SubtleCrypto');
        requireArguments(count, 2, 'exportKey');
        format = convertKeyFormat(format);
        key = convertCryptoKey(key, 'key');
        return () => {
          const result = exportKeyRecord(format, key);
          return format === 'jwk' ? () => jwkToObject(result) : resultBuffer(result);
        };
      });
    }

    wrapKey(format, key, wrappingKey, wrapAlgorithm) {
      const count = arguments.length;
      return runMethod(() => {
        thisRecord(this, 'SubtleCrypto');
        requireArguments(count, 4, 'wrapKey');
        format = convertKeyFormat(format);
        key = convertCryptoKey(key, 'key');
        wrappingKey = convertCryptoKey(wrappingKey, 'wrappingKey');
        wrapAlgorithm = convertAlgorithmIdentifier(wrapAlgorithm);
        let normalizedAlgorithm = tryNormalizeAlgorithm(wrapAlgorithm, 'wrapKey');
        if (normalizedAlgorithm === undefined) normalizedAlgorithm = normalizeAlgorithm(wrapAlgorithm, 'encrypt');
        return () => {
          checkKeyAlgorithm(normalizedAlgorithm, wrappingKey, 'wrapKey');
          const exportedKey = exportKeyRecord(format, key);
          const bytes = format === 'jwk' ? utf8Encode(JSONStringify(jwkToObject(exportedKey))) : exportedKey;
          let result;
          if (supportsOperation(normalizedAlgorithm.name, 'wrapKey')) result = getOperation(normalizedAlgorithm.name, 'wrapKey')(normalizedAlgorithm, wrappingKey, bytes);
          else if (supportsOperation(normalizedAlgorithm.name, 'encrypt')) result = getOperation(normalizedAlgorithm.name, 'encrypt')(normalizedAlgorithm, wrappingKey, bytes);
          else throw notSupportedError(`The ${normalizedAlgorithm.name} algorithm does not support the wrap key operation`);
          return resultBuffer(result);
        };
      });
    }

    unwrapKey(format, wrappedKey, unwrappingKey, unwrapAlgorithm, unwrappedKeyAlgorithm, extractable, keyUsages) {
      const count = arguments.length;
      return runMethod(() => {
        thisRecord(this, 'SubtleCrypto');
        requireArguments(count, 7, 'unwrapKey');
        format = convertKeyFormat(format);
        wrappedKey = convertBufferSource(wrappedKey, 'wrappedKey');
        unwrappingKey = convertCryptoKey(unwrappingKey, 'unwrappingKey');
        unwrapAlgorithm = convertAlgorithmIdentifier(unwrapAlgorithm);
        unwrappedKeyAlgorithm = convertAlgorithmIdentifier(unwrappedKeyAlgorithm);
        extractable = convertBoolean(extractable);
        keyUsages = convertKeyUsages(keyUsages);
        let normalizedAlgorithm = tryNormalizeAlgorithm(unwrapAlgorithm, 'unwrapKey');
        if (normalizedAlgorithm === undefined) normalizedAlgorithm = normalizeAlgorithm(unwrapAlgorithm, 'decrypt');
        const normalizedKeyAlgorithm = normalizeAlgorithm(unwrappedKeyAlgorithm, 'importKey');
        const bytes = copyBytes(wrappedKey);
        return () => {
          checkKeyAlgorithm(normalizedAlgorithm, unwrappingKey, 'unwrapKey');
          let key;
          if (supportsOperation(normalizedAlgorithm.name, 'unwrapKey')) key = getOperation(normalizedAlgorithm.name, 'unwrapKey')(normalizedAlgorithm, unwrappingKey, bytes);
          else if (supportsOperation(normalizedAlgorithm.name, 'decrypt')) key = getOperation(normalizedAlgorithm.name, 'decrypt')(normalizedAlgorithm, unwrappingKey, bytes);
          else throw notSupportedError(`The ${normalizedAlgorithm.name} algorithm does not support the unwrap key operation`);
          if (format === 'jwk') key = parseJwk(key);
          return keyResult(importKeyRecord(normalizedKeyAlgorithm, format, key, extractable, keyUsages));
        };
      });
    }

    // https://wicg.github.io/webcrypto-modern-algos/#SubtleCrypto-method-encapsulateKey
    encapsulateKey(encapsulationAlgorithm, encapsulationKey, sharedKeyAlgorithm, extractable, keyUsages) {
      const count = arguments.length;
      return runMethod(() => {
        thisRecord(this, 'SubtleCrypto');
        requireArguments(count, 5, 'encapsulateKey');
        encapsulationAlgorithm = convertAlgorithmIdentifier(encapsulationAlgorithm);
        encapsulationKey = convertCryptoKey(encapsulationKey, 'encapsulationKey');
        sharedKeyAlgorithm = convertAlgorithmIdentifier(sharedKeyAlgorithm);
        extractable = convertBoolean(extractable);
        keyUsages = convertKeyUsages(keyUsages);
        const normalizedEncapsulationAlgorithm = normalizeAlgorithm(encapsulationAlgorithm, 'encapsulate');
        const normalizedSharedKeyAlgorithm = normalizeAlgorithm(sharedKeyAlgorithm, 'importKey');
        return () => {
          checkKeyAlgorithm(normalizedEncapsulationAlgorithm, encapsulationKey, 'encapsulateKey');
          const encapsulatedBits = getOperation(encapsulationKey.algorithm.name, 'encapsulate')(normalizedEncapsulationAlgorithm, encapsulationKey);
          const sharedKey = importKeyRecord(normalizedSharedKeyAlgorithm, 'raw-secret', encapsulatedBits.sharedKey, extractable, keyUsages, false);
          return () => {
            const result = {};
            result.ciphertext = toArrayBuffer(encapsulatedBits.ciphertext);
            result.sharedKey = sharedKey.object;
            return result;
          };
        };
      });
    }

    // https://wicg.github.io/webcrypto-modern-algos/#SubtleCrypto-method-encapsulateBits
    encapsulateBits(encapsulationAlgorithm, encapsulationKey) {
      const count = arguments.length;
      return runMethod(() => {
        thisRecord(this, 'SubtleCrypto');
        requireArguments(count, 2, 'encapsulateBits');
        encapsulationAlgorithm = convertAlgorithmIdentifier(encapsulationAlgorithm);
        encapsulationKey = convertCryptoKey(encapsulationKey, 'encapsulationKey');
        const normalizedEncapsulationAlgorithm = normalizeAlgorithm(encapsulationAlgorithm, 'encapsulate');
        return () => {
          checkKeyAlgorithm(normalizedEncapsulationAlgorithm, encapsulationKey, 'encapsulateBits');
          const encapsulatedBits = getOperation(encapsulationKey.algorithm.name, 'encapsulate')(normalizedEncapsulationAlgorithm, encapsulationKey);
          return () => {
            const result = {};
            result.ciphertext = toArrayBuffer(encapsulatedBits.ciphertext);
            result.sharedKey = toArrayBuffer(encapsulatedBits.sharedKey);
            return result;
          };
        };
      });
    }

    // https://wicg.github.io/webcrypto-modern-algos/#SubtleCrypto-method-decapsulateKey
    decapsulateKey(decapsulationAlgorithm, decapsulationKey, ciphertext, sharedKeyAlgorithm, extractable, keyUsages) {
      const count = arguments.length;
      return runMethod(() => {
        thisRecord(this, 'SubtleCrypto');
        requireArguments(count, 6, 'decapsulateKey');
        decapsulationAlgorithm = convertAlgorithmIdentifier(decapsulationAlgorithm);
        decapsulationKey = convertCryptoKey(decapsulationKey, 'decapsulationKey');
        ciphertext = convertBufferSource(ciphertext, 'ciphertext');
        sharedKeyAlgorithm = convertAlgorithmIdentifier(sharedKeyAlgorithm);
        extractable = convertBoolean(extractable);
        keyUsages = convertKeyUsages(keyUsages);
        const normalizedDecapsulationAlgorithm = normalizeAlgorithm(decapsulationAlgorithm, 'decapsulate');
        const normalizedSharedKeyAlgorithm = normalizeAlgorithm(sharedKeyAlgorithm, 'importKey');
        const bytes = copyBytes(ciphertext);
        return () => {
          checkKeyAlgorithm(normalizedDecapsulationAlgorithm, decapsulationKey, 'decapsulateKey');
          const decapsulatedBits = getOperation(decapsulationKey.algorithm.name, 'decapsulate')(normalizedDecapsulationAlgorithm, decapsulationKey, bytes);
          return keyResult(importKeyRecord(normalizedSharedKeyAlgorithm, 'raw-secret', decapsulatedBits, extractable, keyUsages, false));
        };
      });
    }

    // https://wicg.github.io/webcrypto-modern-algos/#SubtleCrypto-method-decapsulateBits
    decapsulateBits(decapsulationAlgorithm, decapsulationKey, ciphertext) {
      const count = arguments.length;
      return runMethod(() => {
        thisRecord(this, 'SubtleCrypto');
        requireArguments(count, 3, 'decapsulateBits');
        decapsulationAlgorithm = convertAlgorithmIdentifier(decapsulationAlgorithm);
        decapsulationKey = convertCryptoKey(decapsulationKey, 'decapsulationKey');
        ciphertext = convertBufferSource(ciphertext, 'ciphertext');
        const normalizedDecapsulationAlgorithm = normalizeAlgorithm(decapsulationAlgorithm, 'decapsulate');
        const bytes = copyBytes(ciphertext);
        return () => {
          checkKeyAlgorithm(normalizedDecapsulationAlgorithm, decapsulationKey, 'decapsulateBits');
          return resultBuffer(getOperation(decapsulationKey.algorithm.name, 'decapsulate')(normalizedDecapsulationAlgorithm, decapsulationKey, bytes));
        };
      });
    }

    // https://wicg.github.io/webcrypto-modern-algos/#SubtleCrypto-method-getPublicKey
    getPublicKey(key, keyUsages) {
      const count = arguments.length;
      return runMethod(() => {
        thisRecord(this, 'SubtleCrypto');
        requireArguments(count, 2, 'getPublicKey');
        key = convertCryptoKey(key, 'key');
        keyUsages = convertKeyUsages(keyUsages);
        const implementation = getImplementation(key.algorithm.name);
        if (implementation === undefined || implementation.getPublicKey === undefined) throw notSupportedError(`The ${key.algorithm.name} algorithm does not support deriving a public key`);
        return () => {
          if (key.type !== 'private') throw invalidAccessError('The key is not a private key');
          checkUsages(keyUsages, implementation.publicUsages);
          return keyResult(createKey('public', true, key.algorithm, normalizeUsages(keyUsages), implementation.getPublicKey(key)));
        };
      });
    }

    // https://wicg.github.io/webcrypto-modern-algos/#SubtleCrypto-method-supports: the overloads are distinguished by the
    // third argument (https://webidl.spec.whatwg.org/#es-overloads): an object or a string is an AlgorithmIdentifier
    static supports(operation, algorithm, lengthOrAdditionalAlgorithm = undefined) {
      if (arguments.length < 2) throw typeError(`Failed to execute 'supports' on 'SubtleCrypto': 2 arguments required, but only ${arguments.length} present.`);
      operation = convertDOMString(operation);
      algorithm = convertAlgorithmIdentifier(algorithm);
      const third = lengthOrAdditionalAlgorithm;
      if (third === undefined || third === null || typeof third === 'number' || typeof third === 'boolean' || typeof third === 'bigint') {
        const length = third === undefined || third === null ? null : convertEnforceRange(third, 0xFFFFFFFF, 'length');
        if (!apply(arrayIncludes, supportedOperations, [operation])) return false;
        return checkSupport(operation, algorithm, length);
      }

      const additionalAlgorithm = convertAlgorithmIdentifier(third);
      if (!apply(arrayIncludes, supportedOperations, [operation])) return false;
      return supportsWithAdditionalAlgorithm(operation, algorithm, additionalAlgorithm);
    }
  }

  defineInterface(SubtleCrypto, 'SubtleCrypto', { '@supports': 2 });

  // ------------------------------------------------------------------------------------------------------------------
  // CryptoKey (https://w3c.github.io/webcrypto/#cryptokey-interface)
  // ------------------------------------------------------------------------------------------------------------------

  class CryptoKey {
    constructor() {
      throw illegalConstructor();
    }

    get type() {
      return thisRecord(this, 'CryptoKey').type;
    }

    get extractable() {
      return thisRecord(this, 'CryptoKey').extractable;
    }

    // https://w3c.github.io/webcrypto/#dfn-CryptoKey-algorithm: the cached object, created in the realm of the key
    get algorithm() {
      const record = thisRecord(this, 'CryptoKey');
      return record.algorithmCached ??= record.realm.toObject(record.algorithm);
    }

    get usages() {
      const record = thisRecord(this, 'CryptoKey');
      return record.usagesCached ??= record.realm.toObject(record.usages);
    }
  }

  defineInterface(CryptoKey, 'CryptoKey');

  // ------------------------------------------------------------------------------------------------------------------
  // Crypto (https://w3c.github.io/webcrypto/#crypto-interface)
  // ------------------------------------------------------------------------------------------------------------------

  const integerArrays = ['Int8Array', 'Uint8Array', 'Uint8ClampedArray', 'Int16Array', 'Uint16Array', 'Int32Array', 'Uint32Array', 'BigInt64Array', 'BigUint64Array'];

  class Crypto {
    constructor() {
      throw illegalConstructor();
    }

    get subtle() {
      return thisRecord(this, 'Crypto').subtle;
    }

    // https://w3c.github.io/webcrypto/#Crypto-method-getRandomValues
    getRandomValues(array) {
      thisRecord(this, 'Crypto');
      if (arguments.length < 1) throw typeError("Failed to execute 'getRandomValues' on 'Crypto': 1 argument required, but only 0 present.");
      // ArrayBufferView (not [AllowShared])
      const name = getTypedArrayName(array);
      if (name === undefined && !(isObject(array) && isDataView(array))) throw typeError("The value is not of type 'ArrayBufferView'");
      const buffer = name !== undefined ? apply(typedArrayBuffer, array, []) : apply(dataViewBuffer, array, []);
      if (isSharedArrayBuffer(buffer)) throw typeError('The ArrayBufferView cannot view a SharedArrayBuffer');
      if (name === undefined || !apply(arrayIncludes, integerArrays, [name])) throw domException('TypeMismatchError', 'The array is not an integer array');
      const byteLength = apply(typedArrayByteLength, array, []);
      if (byteLength > 65536) throw new QuotaExceededError_(`The array byte length (${byteLength}) exceeds the number of bytes of entropy available (65536)`);
      if (byteLength !== 0) hooks.fillRandom(array);
      return array;
    }

    // https://w3c.github.io/webcrypto/#Crypto-method-randomUUID
    randomUUID() {
      thisRecord(this, 'Crypto');
      const bytes = hooks.randomBytes(16);
      bytes[6] = (bytes[6] & 0x0F) | 0x40;
      bytes[8] = (bytes[8] & 0x3F) | 0x80;
      let result = '';
      for (let i = 0; i < 16; i++) {
        if (i === 4 || i === 6 || i === 8 || i === 10) result += '-';
        const hex = '0123456789abcdef';
        result += hex[bytes[i] >> 4] + hex[bytes[i] & 15];
      }

      return result;
    }
  }

  defineInterface(Crypto, 'Crypto');
  if (!isSecureContext) removeMembers(Crypto.prototype, ['subtle', 'randomUUID']);

  // The objects of the global object ([SameObject] crypto and its subtle attribute)
  const subtle = ObjectCreate(SubtleCrypto.prototype);
  createRecord(subtle, 'SubtleCrypto');
  const crypto = ObjectCreate(Crypto.prototype);
  createRecord(crypto, 'Crypto').subtle = subtle;

  return {
    crypto,
    Crypto,
    CryptoKey,
    SubtleCrypto,
    deserializeKey,
  };
})
