// Runs the workloads of workloads/*.mjs: ES modules that use npm packages as an application would, without a bundler.
// It runs the same way in Node.js (generate.js, in a vm context) and in the engine (NpmPackagesTests.cs).
//
// The packages see a runtime that is neither Node.js nor a browser: the built-ins of ECMAScript, and the globals that the
// harness defines below (process, console, the timers, TextEncoder and TextDecoder, atob and btoa, structuredClone,
// EventTarget and AbortController, MessageChannel, URL and URLSearchParams). The conditions of the 'exports' of the
// packages are 'import' or 'require', and 'default'.
//
// The script evaluates to a function that takes the host, { readFile(path), requireModule(path) }. readFile returns the
// content of a file, or undefined when there is no file at that path. Paths are absolute and use '/': '/workloads' is
// the directory of the workloads, and '/node_modules' the installed packages. requireModule loads, links and evaluates
// the ES module at the path synchronously and returns its namespace (require() of an ES module). The function returns:
// - resolve(specifier, referrer): the module that an 'import' of the module at the path 'referrer' (or of the harness)
//   loads, as { path, format }: 'module' for an ES module, else 'commonjs', 'json' or 'builtin' (a Node.js module,
//   'node:<name>'). The host creates the ES modules, and creates the others as modules whose exports are the names that
//   getExportNames(path) returns, and whose values are require(path) ('default') and its properties.
// - require(path): the exports of the CommonJS module, the JSON file or the Node.js module at the path.
// - getExportNames(path): the names of the exports of a module that is not an ES module.
// - run(name): imports workloads/<name>.mjs and calls its default export. It returns an object whose 'done' property
//   becomes true once the result is known, and whose 'result' property is then the result: a string, followed by what
//   the packages wrote to the console. The host runs the promise jobs, and calls runTimer() while 'done' is false.
// - runTimer(): runs the next callback of setTimeout, setInterval or setImmediate. It returns false when there is none.
(function (host) {
  "use strict";

  // Node.js gives these globals to the modules. The process object only has what the packages commonly read: the
  // packages must not believe they run in Node.js (process.versions.node), as the Node.js modules ('fs', 'path'...)
  // do not exist here.
  globalThis.global = globalThis;
  globalThis.process = {
    env: { NODE_ENV: "production" },
    argv: [],
    platform: "",
    version: "",
    versions: {},
    cwd: () => "/",
    // The convention of the bundlers (the 'process' package): the packages that look for Node.js check it
    browser: true,
    nextTick: (callback, ...args) => Promise.resolve().then(() => callback(...args)),
    emitWarning() { },
  };

  // What the packages write to the console is part of the result
  const messages = [];
  const log = level => (...args) => messages.push(level + ": " + args.map(String).join(" "));
  globalThis.console = { log: log("log"), info: log("info"), warn: log("warn"), error: log("error"), debug: log("debug"), trace: log("trace") };

  // The timers run when the host asks for them, once the promise jobs are done, in the order of their virtual time
  const timers = [];
  let timerId = 0;
  let currentTime = 0;
  function addTimer(callback, delay, args, repeat) {
    const timer = { id: ++timerId, time: currentTime + Math.max(0, Number(delay) || 0), callback, args, repeat: repeat ? Math.max(1, Number(delay) || 0) : undefined };
    timers.push(timer);
    return timer.id;
  }

  function clearTimer(id) {
    const index = timers.findIndex(timer => timer.id === id);
    if (index >= 0)
      timers.splice(index, 1);
  }

  globalThis.setTimeout = (callback, delay, ...args) => addTimer(callback, delay, args, false);
  globalThis.setInterval = (callback, delay, ...args) => addTimer(callback, delay, args, true);
  globalThis.setImmediate = (callback, ...args) => addTimer(callback, 0, args, false);
  globalThis.clearTimeout = clearTimer;
  globalThis.clearInterval = clearTimer;
  globalThis.clearImmediate = clearTimer;
  globalThis.queueMicrotask = callback => { Promise.resolve().then(callback); };

  // TextEncoder and TextDecoder of the Encoding Standard, for UTF-8 only
  class TextEncoder {
    get encoding() {
      return "utf-8";
    }

    encode(input = "") {
      const bytes = [];
      const text = String(input);
      for (let i = 0; i < text.length; i++) {
        let code = text.charCodeAt(i);
        if (code >= 0xD800 && code <= 0xDBFF && i + 1 < text.length) {
          const next = text.charCodeAt(i + 1);
          if (next >= 0xDC00 && next <= 0xDFFF) {
            code = 0x10000 + ((code - 0xD800) << 10) + (next - 0xDC00);
            i++;
          }
        }

        if (code >= 0xD800 && code <= 0xDFFF)
          code = 0xFFFD;

        if (code < 0x80) {
          bytes.push(code);
        }
        else if (code < 0x800) {
          bytes.push(0xC0 | (code >> 6), 0x80 | (code & 0x3F));
        }
        else if (code < 0x10000) {
          bytes.push(0xE0 | (code >> 12), 0x80 | ((code >> 6) & 0x3F), 0x80 | (code & 0x3F));
        }
        else {
          bytes.push(0xF0 | (code >> 18), 0x80 | ((code >> 12) & 0x3F), 0x80 | ((code >> 6) & 0x3F), 0x80 | (code & 0x3F));
        }
      }

      return new Uint8Array(bytes);
    }

    encodeInto(input, destination) {
      const bytes = this.encode(input);
      const written = Math.min(bytes.length, destination.length);
      destination.set(bytes.subarray(0, written));
      return { read: String(input).length, written };
    }
  }

  class TextDecoder {
    get encoding() {
      return "utf-8";
    }

    decode(input) {
      if (input === undefined)
        return "";

      const bytes = ArrayBuffer.isView(input) ? new Uint8Array(input.buffer, input.byteOffset, input.byteLength) : new Uint8Array(input);
      let result = "";
      for (let i = 0; i < bytes.length;) {
        const byte = bytes[i];
        const length = byte < 0x80 ? 1 : byte >= 0xC2 && byte < 0xE0 ? 2 : byte >= 0xE0 && byte < 0xF0 ? 3 : byte >= 0xF0 && byte < 0xF5 ? 4 : 0;
        let code = length === 1 ? byte : length === 2 ? byte & 0x1F : length === 3 ? byte & 0x0F : byte & 0x07;
        let valid = length > 0 && i + length <= bytes.length;
        for (let j = 1; valid && j < length; j++) {
          const next = bytes[i + j];
          valid = (next & 0xC0) === 0x80;
          code = (code << 6) | (next & 0x3F);
        }

        if (valid && ((length === 3 && (code < 0x800 || (code >= 0xD800 && code <= 0xDFFF))) || (length === 4 && (code < 0x10000 || code > 0x10FFFF))))
          valid = false;

        if (!valid) {
          result += "\uFFFD";
          i++;
          continue;
        }

        result += String.fromCodePoint(code);
        i += length;
      }

      return result;
    }
  }

  globalThis.TextEncoder = TextEncoder;
  globalThis.TextDecoder = TextDecoder;

  // atob and btoa of the HTML Standard (forgiving-base64)
  const base64 = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
  globalThis.btoa = data => {
    const text = String(data);
    let result = "";
    for (let i = 0; i < text.length; i += 3) {
      const codes = [text.charCodeAt(i), text.charCodeAt(i + 1), text.charCodeAt(i + 2)];
      if (codes.some(code => code > 0xFF))
        throw new Error("The string to be encoded contains characters outside of the Latin1 range.");

      const value = (codes[0] << 16) | ((codes[1] || 0) << 8) | (codes[2] || 0);
      result += base64[value >> 18] + base64[(value >> 12) & 0x3F] + (i + 1 < text.length ? base64[(value >> 6) & 0x3F] : "=") + (i + 2 < text.length ? base64[value & 0x3F] : "=");
    }

    return result;
  };
  globalThis.atob = data => {
    let text = String(data).replace(/[\t\n\f\r ]/g, "");
    if (text.length % 4 === 0)
      text = text.replace(/==?$/, "");
    if (text.length % 4 === 1 || /[^A-Za-z0-9+/]/.test(text))
      throw new Error("The string to be decoded is not correctly encoded.");

    let result = "";
    let buffer = 0;
    let bits = 0;
    for (const char of text) {
      buffer = (buffer << 6) | base64.indexOf(char);
      bits += 6;
      if (bits >= 8) {
        bits -= 8;
        result += String.fromCharCode((buffer >> bits) & 0xFF);
      }
    }

    return result;
  };

  // structuredClone of the HTML Standard, for the values of ECMAScript
  globalThis.structuredClone = value => {
    const memory = new Map();
    const clone = input => {
      if ((typeof input !== "object" && typeof input !== "function") || input === null) {
        if (typeof input === "symbol")
          throw new Error("Symbol could not be cloned.");
        return input;
      }

      if (memory.has(input))
        return memory.get(input);
      if (typeof input === "function")
        throw new Error("The function could not be cloned.");

      const remember = output => {
        memory.set(input, output);
        return output;
      };
      const tag = Object.prototype.toString.call(input);
      switch (tag) {
        case "[object Boolean]":
        case "[object Number]":
        case "[object String]":
        case "[object BigInt]":
          return remember(Object(input.valueOf()));
        case "[object Date]":
          return remember(new Date(input.getTime()));
        case "[object RegExp]":
          return remember(new RegExp(input.source, input.flags));
        case "[object ArrayBuffer]":
          return remember(input.slice(0));
        case "[object DataView]":
          return remember(new DataView(clone(input.buffer), input.byteOffset, input.byteLength));
        case "[object Map]": {
          const output = remember(new Map());
          for (const [key, item] of input)
            output.set(clone(key), clone(item));
          return output;
        }
        case "[object Set]": {
          const output = remember(new Set());
          for (const item of input)
            output.add(clone(item));
          return output;
        }
        case "[object Error]": {
          const constructors = { EvalError, RangeError, ReferenceError, SyntaxError, TypeError, URIError };
          const output = remember(new (constructors[input.name] ?? Error)(input.message));
          if ("cause" in input)
            output.cause = clone(input.cause);
          return output;
        }
      }

      if (ArrayBuffer.isView(input))
        return remember(new input.constructor(clone(input.buffer), input.byteOffset, input.length));

      const output = remember(Array.isArray(input) ? new Array(input.length) : {});
      for (const key of Object.keys(input))
        output[key] = clone(input[key]);
      return output;
    };

    return clone(value);
  };

  // EventTarget, Event, AbortSignal and AbortController of the DOM Standard, for the packages that cancel their work
  class Event {
    constructor(type, options = {}) {
      this.type = String(type);
      this.bubbles = Boolean(options.bubbles);
      this.cancelable = Boolean(options.cancelable);
      this.defaultPrevented = false;
      this.target = null;
      this.currentTarget = null;
    }

    preventDefault() {
      if (this.cancelable)
        this.defaultPrevented = true;
    }

    stopPropagation() { }

    stopImmediatePropagation() {
      this.immediatePropagationStopped = true;
    }
  }

  class EventTarget {
    #listeners = new Map();

    addEventListener(type, callback, options) {
      if (callback === null || callback === undefined)
        return;

      const listeners = this.#listeners.get(type) ?? [];
      const capture = typeof options === "boolean" ? options : Boolean(options?.capture);
      if (listeners.some(listener => listener.callback === callback && listener.capture === capture))
        return;

      const listener = { callback, capture, once: Boolean(options?.once) };
      listeners.push(listener);
      this.#listeners.set(type, listeners);
      options?.signal?.addEventListener("abort", () => this.removeEventListener(type, callback, options));
    }

    removeEventListener(type, callback, options) {
      const capture = typeof options === "boolean" ? options : Boolean(options?.capture);
      const listeners = this.#listeners.get(type);
      const index = listeners?.findIndex(listener => listener.callback === callback && listener.capture === capture) ?? -1;
      if (index >= 0)
        listeners.splice(index, 1);
    }

    dispatchEvent(event) {
      event.target = this;
      event.currentTarget = this;
      for (const listener of [...(this.#listeners.get(event.type) ?? [])]) {
        if (listener.once)
          this.removeEventListener(event.type, listener.callback, listener);
        try {
          if (typeof listener.callback === "function")
            listener.callback.call(this, event);
          else
            listener.callback.handleEvent(event);
        }
        catch (error) {
          messages.push("uncaught: " + format(error));
        }

        if (event.immediatePropagationStopped)
          break;
      }

      event.currentTarget = null;
      return !event.defaultPrevented;
    }
  }

  class AbortSignal extends EventTarget {
    constructor() {
      super();
      this.aborted = false;
      this.reason = undefined;
      this.onabort = null;
    }

    throwIfAborted() {
      if (this.aborted)
        throw this.reason;
    }

    static abort(reason) {
      const controller = new AbortController();
      controller.abort(reason);
      return controller.signal;
    }

    static timeout(milliseconds) {
      const controller = new AbortController();
      setTimeout(() => controller.abort(Object.assign(new Error("The operation timed out."), { name: "TimeoutError" })), milliseconds);
      return controller.signal;
    }
  }

  class AbortController {
    constructor() {
      this.signal = new AbortSignal();
    }

    abort(reason) {
      const signal = this.signal;
      if (signal.aborted)
        return;

      signal.aborted = true;
      signal.reason = reason === undefined ? Object.assign(new Error("This operation was aborted"), { name: "AbortError" }) : reason;
      const event = new Event("abort");
      signal.onabort?.call(signal, event);
      signal.dispatchEvent(event);
    }
  }

  Object.assign(globalThis, { Event, EventTarget, AbortSignal, AbortController });

  // MessageChannel of the HTML Standard: a message is delivered by a task, as a timer without delay
  class MessagePort {
    constructor() {
      this.onmessage = null;
      this.other = undefined;
    }

    postMessage(data) {
      const target = this.other;
      setImmediate(() => target.onmessage?.({ data, target }));
    }

    start() { }

    close() { }
  }

  globalThis.MessageChannel = class MessageChannel {
    constructor() {
      this.port1 = new MessagePort();
      this.port2 = new MessagePort();
      this.port1.other = this.port2;
      this.port2.other = this.port1;
    }
  };

  function runTimer() {
    if (timers.length === 0)
      return false;

    let next = 0;
    for (let i = 1; i < timers.length; i++) {
      if (timers[i].time < timers[next].time || (timers[i].time === timers[next].time && timers[i].id < timers[next].id))
        next = i;
    }

    const timer = timers[next];
    currentTime = timer.time;
    if (timer.repeat === undefined) {
      timers.splice(next, 1);
    }
    else {
      timer.time += timer.repeat;
    }

    // An exception of a callback is reported, as a browser reports it to the console
    try {
      timer.callback(...timer.args);
    }
    catch (error) {
      messages.push("uncaught: " + format(error));
    }

    return true;
  }

  function normalize(path) {
    const parts = [];
    for (const part of path.split("/")) {
      if (part === "" || part === ".")
        continue;
      if (part === "..")
        parts.pop();
      else
        parts.push(part);
    }

    return "/" + parts.join("/");
  }

  function dirname(path) {
    const index = path.lastIndexOf("/");
    return index <= 0 ? "/" : path.substring(0, index);
  }

  // The resolution of a module reads the files it tries: the host is asked once per path
  const files = new Map();
  function readFile(path) {
    let content = files.get(path);
    if (content === undefined && !files.has(path)) {
      content = host.readFile(path);
      files.set(path, content);
    }

    return content;
  }

  const isFile = path => readFile(path) !== undefined;

  function notFound(specifier, referrer) {
    const error = new Error("Cannot find module '" + specifier + "' imported from '" + referrer + "'");
    error.code = "MODULE_NOT_FOUND";
    return error;
  }

  // The package.json of the package that contains the path, as { directory, json }
  const packages = new Map();
  function findPackage(path) {
    for (let directory = path; ; directory = dirname(directory)) {
      if (!directory.endsWith("/node_modules")) {
        let result = packages.get(directory);
        if (result === undefined) {
          const content = readFile(directory + "/package.json");
          result = content === undefined ? null : { directory, json: JSON.parse(content) };
          packages.set(directory, result);
        }

        if (result !== null)
          return result;
      }

      if (directory === "/")
        return undefined;
    }
  }

  // The conditions of the 'exports' and 'imports' fields (https://nodejs.org/api/packages.html#conditional-exports):
  // the packages are loaded as by a runtime that is neither Node.js nor a browser
  const conditions = {
    module: ["import", "default"],
    commonjs: ["require", "default"],
  };

  function resolveTarget(directory, target, match, kind) {
    if (typeof target === "string") {
      if (!target.startsWith("./"))
        return target.startsWith("../") || target.startsWith("/") ? undefined : { bare: target.replaceAll("*", match) };
      return { path: normalize(directory + "/" + target.replaceAll("*", match)) };
    }

    if (Array.isArray(target)) {
      for (const item of target) {
        const result = resolveTarget(directory, item, match, kind);
        if (result !== undefined)
          return result;
      }

      return undefined;
    }

    if (target !== null && typeof target === "object") {
      for (const [key, value] of Object.entries(target)) {
        if (conditions[kind].includes(key)) {
          const result = resolveTarget(directory, value, match, kind);
          if (result !== undefined)
            return result;
        }
      }
    }

    return undefined;
  }

  // PACKAGE_IMPORTS_EXPORTS_RESOLVE: an exact key, or the pattern with the longest prefix
  function resolveMap(directory, map, key, kind) {
    if (Object.hasOwn(map, key) && !key.includes("*"))
      return resolveTarget(directory, map[key], "", kind);

    let bestKey;
    let bestMatch;
    for (const candidate of Object.keys(map)) {
      const star = candidate.indexOf("*");
      if (star < 0)
        continue;

      const prefix = candidate.substring(0, star);
      const suffix = candidate.substring(star + 1);
      if (key.startsWith(prefix) && key !== prefix && key.endsWith(suffix) && key.length >= candidate.length - 1) {
        if (bestKey === undefined || prefix.length > bestKey.indexOf("*")) {
          bestKey = candidate;
          bestMatch = key.substring(prefix.length, key.length - suffix.length);
        }
      }
    }

    return bestKey === undefined ? undefined : resolveTarget(directory, map[bestKey], bestMatch, kind);
  }

  function resolveExports(directory, exports, subpath, kind) {
    const isConditions = exports !== null && typeof exports === "object" && !Array.isArray(exports) && !Object.keys(exports).some(key => key.startsWith("."));
    const map = typeof exports === "string" || Array.isArray(exports) || isConditions ? { ".": exports } : exports;
    return resolveMap(directory, map, subpath, kind)?.path;
  }

  // LOAD_AS_FILE and LOAD_AS_DIRECTORY of require (https://nodejs.org/api/modules.html#all-together). The ES modules
  // of the packages always give the full path of the files, so the same rules work for them.
  function loadAsFile(path) {
    for (const candidate of [path, path + ".js", path + ".json"]) {
      if (isFile(candidate))
        return candidate;
    }

    return undefined;
  }

  function loadIndex(path) {
    return loadAsFile(path + "/index");
  }

  function loadAsDirectory(path) {
    const content = readFile(path + "/package.json");
    if (content !== undefined) {
      const main = JSON.parse(content).main;
      if (typeof main === "string") {
        const target = normalize(path + "/" + main);
        const resolved = loadAsFile(target) ?? loadIndex(target);
        if (resolved !== undefined)
          return resolved;
      }
    }

    return loadIndex(path);
  }

  function resolvePath(specifier, referrer, kind) {
    const directory = dirname(referrer);
    if (specifier.startsWith("./") || specifier.startsWith("../") || specifier.startsWith("/") || specifier === "." || specifier === "..") {
      const path = normalize(specifier.startsWith("/") ? specifier : directory + "/" + specifier);
      return loadAsFile(path) ?? loadAsDirectory(path);
    }

    // The 'imports' field of the package of the referrer
    if (specifier.startsWith("#")) {
      const pkg = findPackage(directory);
      const imports = pkg?.json.imports;
      if (imports === null || typeof imports !== "object")
        return undefined;

      const target = resolveMap(pkg.directory, imports, specifier, kind);
      if (target === undefined)
        return undefined;
      return target.bare !== undefined ? resolvePath(target.bare, pkg.directory + "/package.json", kind) : target.path;
    }

    // A package: its name and the path in the package
    const parts = specifier.split("/");
    const nameLength = specifier.startsWith("@") ? 2 : 1;
    const name = parts.slice(0, nameLength).join("/");
    const subpath = parts.length > nameLength ? "./" + parts.slice(nameLength).join("/") : ".";
    for (let current = directory; ; current = dirname(current)) {
      if (!current.endsWith("/node_modules")) {
        const packageDirectory = normalize(current + "/node_modules/" + name);
        const content = readFile(packageDirectory + "/package.json");
        if (content !== undefined) {
          const json = JSON.parse(content);
          if (json.exports !== undefined && json.exports !== null)
            return resolveExports(packageDirectory, json.exports, subpath, kind);

          const path = normalize(packageDirectory + "/" + subpath);
          return loadAsFile(path) ?? loadAsDirectory(path);
        }
      }

      if (current === "/")
        return undefined;
    }
  }

  // The CommonJS modules are compiled once: the function that wraps their code, or the SyntaxError of the code
  const wrappers = new Map();
  function compile(path) {
    let wrapper = wrappers.get(path);
    if (wrapper === undefined) {
      const source = readFile(path);
      const body = source.startsWith("#!") ? "//" + source : source;
      try {
        wrapper = (0, eval)("(function (exports, require, module, __filename, __dirname) {" + body + "\n})\n//# sourceURL=" + path);
      }
      catch (error) {
        wrapper = { error };
      }

      wrappers.set(path, wrapper);
    }

    return wrapper;
  }

  // ESM_FILE_FORMAT: the extension, then the 'type' field of the package.json of the file. When there is no 'type'
  // field, a file that is not a valid CommonJS module and uses the syntax of the ES modules is an ES module, as Node.js
  // detects it (https://nodejs.org/api/packages.html#syntax-detection).
  const formats = new Map();
  function getFormat(path) {
    let format = formats.get(path);
    if (format === undefined) {
      if (path.startsWith("node:")) {
        format = "builtin";
      }
      else if (path.endsWith(".mjs")) {
        format = "module";
      }
      else if (path.endsWith(".cjs")) {
        format = "commonjs";
      }
      else if (path.endsWith(".json")) {
        format = "json";
      }
      else {
        const type = findPackage(dirname(path))?.json.type;
        if (type === "module") {
          format = "module";
        }
        else if (type === undefined && compile(path).error instanceof SyntaxError && /\b(import|export)\b/.test(readFile(path))) {
          format = "module";
        }
        else {
          format = "commonjs";
        }
      }

      formats.set(path, format);
    }

    return format;
  }

  // The Node.js modules that the packages use without needing Node.js
  const builtins = {
    module: () => ({
      createRequire: url => {
        const path = String(url).startsWith("file://") ? String(url).substring(7) : String(url);
        return createRequire(path);
      },
    }),
    util: () => ({
      inspect: value => String(value),
    }),
  };
  const builtinExports = new Map();

  // URL and URLSearchParams of the URL Standard come from the npm package that implements them in JavaScript. They are
  // created when a script first uses them.
  for (const name of ["URL", "URLSearchParams"]) {
    Object.defineProperty(globalThis, name, {
      configurable: true,
      enumerable: false,
      get() {
        const value = requireModule(resolve("whatwg-url", "/harness.js", "commonjs").path)[name];
        Object.defineProperty(globalThis, name, { configurable: true, enumerable: false, writable: true, value });
        return value;
      },
      set(value) {
        Object.defineProperty(globalThis, name, { configurable: true, enumerable: false, writable: true, value });
      },
    });
  }

  // The Node.js modules replaced by the npm packages that the bundlers use for them
  const builtinPackages = {
    path: "path-browserify",
  };

  function resolve(specifier, referrer, kind) {
    referrer ??= "/harness.js";
    const builtin = specifier.startsWith("node:") ? specifier.substring(5) : specifier;
    if (Object.hasOwn(builtins, builtin))
      return { path: "node:" + builtin, format: "builtin" };
    if (Object.hasOwn(builtinPackages, builtin))
      return resolve(builtinPackages[builtin], "/harness.js", kind);

    const path = resolvePath(specifier, referrer, kind);
    if (path === undefined || !isFile(path))
      throw notFound(specifier, referrer);
    return { path, format: getFormat(path) };
  }

  function createRequire(path) {
    const require = specifier => requireModule(resolve(specifier, path, "commonjs").path);
    require.resolve = specifier => resolve(specifier, path, "commonjs").path;
    require.cache = Object.create(null);
    return require;
  }

  // The CommonJS modules, the JSON files and the Node.js modules
  const cache = new Map();
  function requireModule(path) {
    const cached = cache.get(path);
    if (cached !== undefined)
      return cached.exports;

    const format = getFormat(path);
    if (format === "builtin") {
      let exports = builtinExports.get(path);
      if (exports === undefined) {
        exports = builtins[path.substring(5)]();
        builtinExports.set(path, exports);
      }

      return exports;
    }

    // require() of an ES module (https://nodejs.org/api/modules.html#loading-ecmascript-modules-using-require): the
    // host loads, links and evaluates it synchronously, and returns its namespace
    if (format === "module") {
      const namespace = host.requireModule(path);
      const exports = Object.hasOwn(namespace, "module.exports") ? namespace["module.exports"] : namespace;
      cache.set(path, { exports });
      return exports;
    }

    const module = { id: path, filename: path, path: dirname(path), loaded: false, exports: {}, children: [], paths: [] };
    cache.set(path, module);
    if (format === "json") {
      module.exports = JSON.parse(readFile(path));
    }
    else {
      const wrapper = compile(path);
      if (typeof wrapper !== "function")
        throw wrapper.error;

      const require = createRequire(path);
      module.require = require;
      wrapper.call(module.exports, module.exports, require, module, path, dirname(path));
    }

    module.loaded = true;
    return module.exports;
  }

  // The names of the exports of the module that the host creates for a module that is not an ES module: 'default' (the
  // value of require(path)), and its properties. Node.js finds the names of the exports of a CommonJS module in its code
  // (cjs-module-lexer); here, the module runs when it is linked, and the names are the properties of its exports.
  function getExportNames(path) {
    const exports = requireModule(path);
    const names = (typeof exports === "object" && exports !== null) || typeof exports === "function" ? Object.keys(exports).filter(name => name !== "default") : [];
    return ["default", ...names];
  }

  function format(error) {
    if (error instanceof Error)
      return "Error: " + String(error) + (error.cause === undefined ? "" : " (cause: " + String(error.cause) + ")");
    return "Error: " + String(error);
  }

  return {
    resolve: (specifier, referrer) => resolve(specifier, referrer, "module"),
    require: requireModule,
    getExportNames,
    runTimer,
    run(name) {
      const state = { done: false, result: undefined };
      const complete = text => {
        if (messages.length > 0)
          text += "\n--- console ---\n" + messages.join("\n");

        // The engine writes the narrow no-break spaces of the CLDR patterns (e.g. "h:mm\u202Fa") as spaces, as the
        // browsers do; Node.js keeps them
        text = text.replaceAll("\u202F", " ");
        state.result = text;
        state.done = true;
      };

      import("/workloads/" + name + ".mjs")
        .then(namespace => namespace.default())
        .then(value => complete(String(value)), error => complete(format(error)));
      return state;
    },
  };
})
