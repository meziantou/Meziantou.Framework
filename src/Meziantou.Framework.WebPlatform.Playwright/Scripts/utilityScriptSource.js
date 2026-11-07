
var __commonJS = obj => {
  let required = false;
  let result;
  return function __require() {
    if (!required) {
      required = true;
      let fn;
      for (const name in obj) { fn = obj[name]; break; }
      const module = { exports: {} };
      fn(module.exports, module);
      result = module.exports;
    }
    return result;
  }
};
var __export = (target, all) => {for (var name in all) target[name] = all[name];};
var __toESM = mod => ({ ...mod, 'default': mod });
var __toCommonJS = mod => ({ ...mod, __esModule: true });


// packages/injected/src/utilityScript.ts
var utilityScript_exports = {};
__export(utilityScript_exports, {
  UtilityScript: () => UtilityScript
});
module.exports = __toCommonJS(utilityScript_exports);

// packages/isomorphic/utilityScriptSerializers.ts
var kFunctionBindingPrefix = "__pw_fn_";
var kBindingsControllerProperty = "__playwright__binding__controller__";
function isRegExp(obj) {
  try {
    return obj instanceof RegExp || Object.prototype.toString.call(obj) === "[object RegExp]";
  } catch (error) {
    return false;
  }
}
function isDate(obj) {
  try {
    return obj instanceof Date || Object.prototype.toString.call(obj) === "[object Date]";
  } catch (error) {
    return false;
  }
}
function isURL(obj) {
  try {
    return obj instanceof URL || Object.prototype.toString.call(obj) === "[object URL]";
  } catch (error) {
    return false;
  }
}
function isError(obj) {
  var _a;
  try {
    return obj instanceof Error || obj && ((_a = Object.getPrototypeOf(obj)) == null ? void 0 : _a.name) === "Error";
  } catch (error) {
    return false;
  }
}
function isTypedArray(obj, constructor) {
  try {
    return obj instanceof constructor || Object.prototype.toString.call(obj) === `[object ${constructor.name}]`;
  } catch (error) {
    return false;
  }
}
function isArrayBuffer(obj) {
  try {
    return obj instanceof ArrayBuffer || Object.prototype.toString.call(obj) === "[object ArrayBuffer]";
  } catch (error) {
    return false;
  }
}
function isMap(obj) {
  try {
    return obj instanceof Map || Object.prototype.toString.call(obj) === "[object Map]";
  } catch (error) {
    return false;
  }
}
function isSet(obj) {
  try {
    return obj instanceof Set || Object.prototype.toString.call(obj) === "[object Set]";
  } catch (error) {
    return false;
  }
}
var typedArrayConstructors = {
  i8: Int8Array,
  ui8: Uint8Array,
  ui8c: Uint8ClampedArray,
  i16: Int16Array,
  ui16: Uint16Array,
  i32: Int32Array,
  ui32: Uint32Array,
  // TODO: add Float16Array once it's in baseline
  f32: Float32Array,
  f64: Float64Array,
  bi64: BigInt64Array,
  bui64: BigUint64Array
};
function typedArrayToBase64(array) {
  if ("toBase64" in array)
    return array.toBase64();
  const binary = Array.from(new Uint8Array(array.buffer, array.byteOffset, array.byteLength)).map((b) => String.fromCharCode(b)).join("");
  return btoa(binary);
}
function base64ToTypedArray(base64, TypedArrayConstructor) {
  const binary = atob(base64);
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++)
    bytes[i] = binary.charCodeAt(i);
  return new TypedArrayConstructor(bytes.buffer);
}
function parseEvaluationResultValue(value, handles = [], refs = /* @__PURE__ */ new Map()) {
  if (Object.is(value, void 0))
    return void 0;
  if (typeof value === "object" && value) {
    if ("ref" in value)
      return refs.get(value.ref);
    if ("v" in value) {
      if (value.v === "undefined")
        return void 0;
      if (value.v === "null")
        return null;
      if (value.v === "NaN")
        return NaN;
      if (value.v === "Infinity")
        return Infinity;
      if (value.v === "-Infinity")
        return -Infinity;
      if (value.v === "-0")
        return -0;
      return void 0;
    }
    if ("d" in value) {
      return new Date(value.d);
    }
    if ("u" in value)
      return new URL(value.u);
    if ("bi" in value)
      return BigInt(value.bi);
    if ("e" in value) {
      const error = new Error(value.e.m);
      error.name = value.e.n;
      error.stack = value.e.s;
      return error;
    }
    if ("r" in value)
      return new RegExp(value.r.p, value.r.f);
    if ("a" in value) {
      const result = [];
      refs.set(value.id, result);
      for (const a of value.a)
        result.push(parseEvaluationResultValue(a, handles, refs));
      return result;
    }
    if ("o" in value) {
      const result = {};
      refs.set(value.id, result);
      for (const { k, v } of value.o) {
        if (k === "__proto__")
          continue;
        result[k] = parseEvaluationResultValue(v, handles, refs);
      }
      return result;
    }
    if ("m" in value) {
      const result = /* @__PURE__ */ new Map();
      refs.set(value.id, result);
      for (const { k, v } of value.m)
        result.set(parseEvaluationResultValue(k, handles, refs), parseEvaluationResultValue(v, handles, refs));
      return result;
    }
    if ("s" in value) {
      const result = /* @__PURE__ */ new Set();
      refs.set(value.id, result);
      for (const item of value.s)
        result.add(parseEvaluationResultValue(item, handles, refs));
      return result;
    }
    if ("h" in value)
      return handles[value.h];
    if ("fn" in value) {
      const name = value.fn;
      return (...args) => globalThis[kBindingsControllerProperty].callBinding(name, ...args);
    }
    if ("ta" in value)
      return base64ToTypedArray(value.ta.b, typedArrayConstructors[value.ta.k]);
    if ("ab" in value)
      return base64ToTypedArray(value.ab.b, Uint8Array).buffer;
  }
  return value;
}
function serializeAsCallArgument(value, handleSerializer, options = {}) {
  return serialize(value, handleSerializer, { visited: /* @__PURE__ */ new Map(), lastId: 0, extendedSerialization: options.extendedSerialization });
}
function serialize(value, handleSerializer, visitorInfo) {
  if (value && typeof value === "object") {
    if (typeof globalThis.Window === "function" && value instanceof globalThis.Window)
      return "ref: <Window>";
    if (typeof globalThis.Document === "function" && value instanceof globalThis.Document)
      return "ref: <Document>";
    if (typeof globalThis.Node === "function" && value instanceof globalThis.Node)
      return "ref: <Node>";
  }
  return innerSerialize(value, handleSerializer, visitorInfo);
}
function innerSerialize(value, handleSerializer, visitorInfo) {
  var _a;
  const result = handleSerializer(value);
  if ("fallThrough" in result)
    value = result.fallThrough;
  else
    return result;
  if (typeof value === "symbol")
    return { v: "undefined" };
  if (Object.is(value, void 0))
    return { v: "undefined" };
  if (Object.is(value, null))
    return { v: "null" };
  if (Object.is(value, NaN))
    return { v: "NaN" };
  if (Object.is(value, Infinity))
    return { v: "Infinity" };
  if (Object.is(value, -Infinity))
    return { v: "-Infinity" };
  if (Object.is(value, -0))
    return { v: "-0" };
  if (typeof value === "boolean")
    return value;
  if (typeof value === "number")
    return value;
  if (typeof value === "string")
    return value;
  if (typeof value === "bigint")
    return { bi: value.toString() };
  if (isError(value)) {
    let stack;
    if ((_a = value.stack) == null ? void 0 : _a.startsWith(value.name + ": " + value.message)) {
      stack = value.stack;
    } else {
      stack = `${value.name}: ${value.message}
${value.stack}`;
    }
    return { e: { n: value.name, m: value.message, s: stack } };
  }
  if (isDate(value))
    return { d: value.toJSON() };
  if (isURL(value))
    return { u: value.toJSON() };
  if (isRegExp(value))
    return { r: { p: value.source, f: value.flags } };
  for (const [k, ctor] of Object.entries(typedArrayConstructors)) {
    if (isTypedArray(value, ctor))
      return { ta: { b: typedArrayToBase64(value), k } };
  }
  if (isArrayBuffer(value))
    return { ab: { b: typedArrayToBase64(new Uint8Array(value)) } };
  const id = visitorInfo.visited.get(value);
  if (id)
    return { ref: id };
  if (visitorInfo.extendedSerialization) {
    if (isMap(value)) {
      const m = [];
      const id2 = ++visitorInfo.lastId;
      visitorInfo.visited.set(value, id2);
      const iterator = value.entries();
      const next = (/* @__PURE__ */ new Map()).entries().next;
      while (true) {
        const entry = next.call(iterator);
        if (entry.done)
          break;
        m.push({ k: serialize(entry.value[0], handleSerializer, visitorInfo), v: serialize(entry.value[1], handleSerializer, visitorInfo) });
      }
      return { m, id: id2 };
    }
    if (isSet(value)) {
      const s = [];
      const id2 = ++visitorInfo.lastId;
      visitorInfo.visited.set(value, id2);
      const iterator = value.values();
      const next = (/* @__PURE__ */ new Set()).values().next;
      while (true) {
        const entry = next.call(iterator);
        if (entry.done)
          break;
        s.push(serialize(entry.value, handleSerializer, visitorInfo));
      }
      return { s, id: id2 };
    }
  }
  if (Array.isArray(value)) {
    const a = [];
    const id2 = ++visitorInfo.lastId;
    visitorInfo.visited.set(value, id2);
    for (let i = 0; i < value.length; ++i)
      a.push(serialize(value[i], handleSerializer, visitorInfo));
    return { a, id: id2 };
  }
  if (typeof value === "object") {
    const o = [];
    const id2 = ++visitorInfo.lastId;
    visitorInfo.visited.set(value, id2);
    for (const name of Object.keys(value)) {
      let item;
      try {
        item = value[name];
      } catch (e) {
        continue;
      }
      if (name === "toJSON" && typeof item === "function")
        o.push({ k: name, v: { o: [], id: 0 } });
      else
        o.push({ k: name, v: serialize(item, handleSerializer, visitorInfo) });
    }
    let jsonWrapper;
    try {
      if (o.length === 0 && value.toJSON && typeof value.toJSON === "function")
        jsonWrapper = { value: value.toJSON() };
    } catch (e) {
    }
    if (jsonWrapper)
      return innerSerialize(jsonWrapper.value, handleSerializer, visitorInfo);
    return { o, id: id2 };
  }
  if (typeof value === "function" && value.name.startsWith(kFunctionBindingPrefix))
    return { fn: value.name };
}

// packages/injected/src/utilityScript.ts
var UtilityScript = class {
  constructor(global, isUnderTest) {
    var _a, _b, _c, _d, _e, _f, _g, _h;
    this.global = global;
    this.isUnderTest = isUnderTest;
    if (global.__pwClock) {
      this.builtins = global.__pwClock.builtins;
    } else {
      this.builtins = {
        setTimeout: (_a = global.setTimeout) == null ? void 0 : _a.bind(global),
        clearTimeout: (_b = global.clearTimeout) == null ? void 0 : _b.bind(global),
        setInterval: (_c = global.setInterval) == null ? void 0 : _c.bind(global),
        clearInterval: (_d = global.clearInterval) == null ? void 0 : _d.bind(global),
        requestAnimationFrame: (_e = global.requestAnimationFrame) == null ? void 0 : _e.bind(global),
        cancelAnimationFrame: (_f = global.cancelAnimationFrame) == null ? void 0 : _f.bind(global),
        requestIdleCallback: (_g = global.requestIdleCallback) == null ? void 0 : _g.bind(global),
        cancelIdleCallback: (_h = global.cancelIdleCallback) == null ? void 0 : _h.bind(global),
        performance: global.performance,
        Intl: global.Intl,
        Date: global.Date,
        AbortSignal: global.AbortSignal,
        Temporal: global.Temporal
      };
    }
    if (this.isUnderTest)
      global.builtins = this.builtins;
  }
  evaluate(isFunction, returnByValue, expression, argCount, ...argsAndHandles) {
    const args = argsAndHandles.slice(0, argCount);
    const handles = argsAndHandles.slice(argCount);
    const parameters = [];
    for (let i = 0; i < args.length; i++)
      parameters[i] = parseEvaluationResultValue(args[i], handles);
    let result = this.global.eval(expression);
    if (isFunction === true) {
      result = result(...parameters);
    } else if (isFunction === false) {
      result = result;
    } else {
      if (typeof result === "function")
        result = result(...parameters);
    }
    return returnByValue ? this._promiseAwareJsonValueNoThrow(result) : result;
  }
  jsonValue(returnByValue, value) {
    if (value === void 0)
      return void 0;
    return serializeAsCallArgument(value, (value2) => ({ fallThrough: value2 }));
  }
  _promiseAwareJsonValueNoThrow(value) {
    const safeJson = (value2) => {
      try {
        return this.jsonValue(true, value2);
      } catch (e) {
        return void 0;
      }
    };
    if (value && typeof value === "object" && typeof value.then === "function") {
      return (async () => {
        const promiseValue = await value;
        return safeJson(promiseValue);
      })();
    }
    return safeJson(value);
  }
};
