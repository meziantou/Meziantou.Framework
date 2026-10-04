// Bootstrap script executed by Meziantou.Framework.NodeJs.NodeJsHost.
// It is started with "--input-type=module --eval", so dynamic imports are resolved relative to the working directory.
// Messages are newline-delimited JSON exchanged over the local socket provided by the .NET host.
import net from "node:net";
import path from "node:path";
import { createRequire } from "node:module";
import { pathToFileURL } from "node:url";

const endpoint = process.env.MEZIANTOU_NODEJS_ENDPOINT;
const token = process.env.MEZIANTOU_NODEJS_TOKEN;
delete process.env.MEZIANTOU_NODEJS_ENDPOINT;
delete process.env.MEZIANTOU_NODEJS_TOKEN;

// Errors that are not related to a call (e.g. thrown by a timer callback) must not stop the process, as it would fail all the calls in progress
process.on("uncaughtException", error => console.error("Uncaught exception:", error));
process.on("unhandledRejection", reason => console.error("Unhandled promise rejection:", reason));

const AsyncFunction = (async function () { }).constructor;
const require = createRequire(path.join(process.cwd(), "/"));

// Functions that compile code. They cannot be called by name, so a module, export, or member name cannot be used to run arbitrary code.
const codeCompilingFunctions = new Set([
    Function,
    AsyncFunction,
    (function* () { }).constructor,
    (async function* () { }).constructor,
    globalThis.eval,
]);

const canWriteRawJson = typeof JSON.rawJSON === "function";

const socket = net.connect(endpoint);
socket.setEncoding("utf8");
socket.on("error", () => process.exit(1));
socket.on("close", () => process.exit(0));

// Values referenced by the .NET host (JSReference), by identifier
const references = new Map();
let nextReferenceId = 0;

function send(message) {
    socket.write(JSON.stringify(message) + "\n");
}

function toBase64(bytes) {
    return Buffer.from(bytes.buffer, bytes.byteOffset, bytes.byteLength).toString("base64");
}

function mapToObject(map) {
    // A null prototype, so a "__proto__" key is an own property
    const result = Object.create(null);
    for (const [key, value] of map) {
        let name;
        switch (typeof key) {
            case "string":
                name = key;
                break;
            case "number":
            case "bigint":
            case "boolean":
                name = String(key);
                break;
            default:
                throw new TypeError(`Cannot serialize a Map with a key of type '${key === null ? "null" : typeof key}'. Only string, number, bigint, and boolean keys are supported.`);
        }

        if (Object.hasOwn(result, name)) {
            throw new TypeError(`Cannot serialize a Map with several keys converted to '${name}'`);
        }

        result[name] = value;
    }

    return result;
}

// Converts values that JSON.stringify cannot represent, or represents in a lossy way
function resultReplacer(key, value) {
    switch (typeof value) {
        case "number":
            if (Number.isFinite(value)) {
                return value === 0 && canWriteRawJson && Object.is(value, -0) ? JSON.rawJSON("-0") : value;
            }

            // Same representation as JsonNumberHandling.AllowNamedFloatingPointLiterals
            return Number.isNaN(value) ? "NaN" : value > 0 ? "Infinity" : "-Infinity";

        case "bigint":
            // Without JSON.rawJSON (Node.js 20 or earlier), JSON.stringify throws as BigInt values are not serializable
            return canWriteRawJson ? JSON.rawJSON(value.toString()) : value;

        case "object":
            if (value === null || Array.isArray(value)) {
                return value;
            }

            if (value instanceof Map) {
                return mapToObject(value);
            }

            if (value instanceof Set) {
                return Array.from(value);
            }

            if (value instanceof ArrayBuffer || (typeof SharedArrayBuffer === "function" && value instanceof SharedArrayBuffer)) {
                return toBase64(new Uint8Array(value));
            }

            if (ArrayBuffer.isView(value)) {
                // Bytes are sent as base64, as System.Text.Json does for byte[]. Other typed arrays are sent as arrays of numbers.
                return value instanceof Uint8Array || value instanceof Uint8ClampedArray || value instanceof DataView ? toBase64(value) : Array.from(value);
            }

            // Buffer.prototype.toJSON converts the buffer to { type: "Buffer", data: [...] } before the replacer is called.
            // The property is read without invoking getters, and the original value is only read for this shape, as reading it again may have side effects.
            if (Object.getOwnPropertyDescriptor(value, "type")?.value === "Buffer") {
                const original = this[key];
                if (original instanceof Uint8Array) {
                    return toBase64(original);
                }
            }

            return value;
    }

    return value;
}

function sendResult(id, result) {
    socket.write(JSON.stringify({ id, result: result === undefined ? null : result }, resultReplacer) + "\n");
}

// A lone surrogate (e.g. a string cut in the middle of an emoji) is serialized as an escape sequence that System.Text.Json cannot convert to a string
function toWellFormed(value) {
    return typeof value.toWellFormed === "function" ? value.toWellFormed() : value.replace(/[\uD800-\uDBFF](?![\uDC00-\uDFFF])|(?<![\uD800-\uDBFF])[\uDC00-\uDFFF]/g, "�");
}

// Never throws, as an error thrown while reporting an error would leave the call without a response
function describeError(error) {
    try {
        if (error instanceof Error) {
            return {
                name: toWellFormed(String(error.name)),
                message: toWellFormed(String(error.message)),
                stack: typeof error.stack === "string" ? toWellFormed(error.stack) : null,
            };
        }

        return { name: typeof error, message: toWellFormed(String(error)), stack: null };
    } catch {
        return { name: typeof error, message: "The error cannot be converted to a string", stack: null };
    }
}

function sendError(id, error) {
    send({ id, error: describeError(error) });
}

function toSpecifier(module) {
    if (path.isAbsolute(module)) {
        return pathToFileURL(module).href;
    }

    // Only file and built-in module URLs are supported. Other schemes, such as data:, would run the code contained in the specifier.
    let url;
    try {
        url = new URL(module);
    } catch {
        // Not a URL: a relative path or a package name
        return module;
    }

    if (url.protocol !== "file:" && url.protocol !== "node:") {
        throw new Error(`The module '${module}' uses the unsupported URL scheme '${url.protocol}'. Only file: and node: URLs are supported.`);
    }

    return module;
}

// Inherited "constructor" and "__proto__" members give access to the Function constructor and to prototypes, so only own members with these names are accessible
function hasMember(target, member) {
    if (member === "constructor" || member === "__proto__") {
        return Object.hasOwn(target, member);
    }

    return member in target;
}

function callFunction(fn, thisArg, args) {
    if (codeCompilingFunctions.has(fn)) {
        throw new TypeError("Functions that compile code, such as the Function constructor or eval, cannot be called");
    }

    return fn.apply(thisArg, args);
}

function getReference(id) {
    if (!references.has(id)) {
        throw new Error(`The reference ${id} does not exist or was released`);
    }

    return references.get(id);
}

function decodeValue(entry) {
    switch (entry.type) {
        case "undefined":
            return undefined;
        case "bigint":
            return BigInt(entry.value);
        case "number":
            return Number(entry.value);
        case "date":
            return new Date(entry.value);
        case "uint8Array":
            // Copy the data, so the Uint8Array does not expose the shared memory pool of Buffer
            return new Uint8Array(Buffer.from(entry.value, "base64"));
        case "reference":
            return getReference(entry.value);
        default:
            throw new Error(`Unknown value type '${entry.type}'`);
    }
}

// Values that JSON cannot represent are sent as null, with their location in the arguments
function decodeArguments(message) {
    const args = message.args ?? [];
    for (const entry of message.values ?? []) {
        const path = entry.path;
        let container = args;
        for (let i = 0; i < path.length - 1; i++) {
            container = container[path[i]];
        }

        // Containers are created by JSON.parse, so even keys such as "__proto__" are own properties
        container[path[path.length - 1]] = decodeValue(entry);
    }

    return args;
}

async function invoke(message, args) {
    const module = await import(toSpecifier(message.module));

    // Functions are called with their container as "this", so methods of exported objects work (e.g. CommonJS "module.exports = { method() { return this... } }")
    const hasObjectDefault = module.default !== null && (typeof module.default === "object" || typeof module.default === "function");
    let container;
    let target;
    if (message.export === null || message.export === undefined) {
        target = module.default;
    } else if (message.export in module) {
        target = module[message.export];

        // Named exports detected in a CommonJS module are copies of the properties of module.exports, which is the expected "this"
        container = hasObjectDefault && module.default[message.export] === target ? module.default : module;
    } else if (hasObjectDefault && hasMember(module.default, message.export)) {
        container = module.default;
        target = module.default[message.export];
    } else {
        throw new Error(`The module '${message.module}' does not export '${message.export}'`);
    }

    if (typeof target === "function") {
        return await callFunction(target, container, args);
    }

    return target;
}

async function invokeMember(target, member, args) {
    if (member === null || member === undefined) {
        if (typeof target !== "function") {
            throw new TypeError("The referenced value is not a function");
        }

        return await callFunction(target, undefined, args);
    }

    if (target === null || target === undefined || !hasMember(Object(target), member)) {
        throw new Error(`The referenced value does not have a member '${member}'`);
    }

    const value = target[member];
    if (typeof value === "function") {
        return await callFunction(value, target, args);
    }

    return value;
}

async function evaluate(message, args) {
    // "args" is only declared when the caller provides arguments, so code declaring its own "args" variable still compiles
    if (args === undefined) {
        return await new AsyncFunction("require", message.code)(require);
    }

    return await new AsyncFunction("require", "args", message.code)(require, args);
}

async function handle(message) {
    if (message.type === "release") {
        references.delete(message.reference);
        return;
    }

    try {
        // Arguments and targets are resolved before awaiting anything, so a reference released by a later message is still available to this call
        let result;
        switch (message.type) {
            case "invoke":
                result = invoke(message, decodeArguments(message));
                break;
            case "invokeReference":
                result = invokeMember(getReference(message.reference), message.member, decodeArguments(message));
                break;
            case "getReference":
                result = getReference(message.reference);
                break;
            case "eval":
                result = evaluate(message, message.args === undefined ? undefined : decodeArguments(message));
                break;
            case "debug":
                result = { references: references.size };
                break;
            case "ping":
                // Answered as soon as the event loop is available, to detect a call blocking it
                result = undefined;
                break;
            default:
                throw new Error(`Unknown message type '${message.type}'`);
        }

        result = await result;
        switch (message.returns) {
            case "void":
                send({ id: message.id });
                break;
            case "reference": {
                const reference = ++nextReferenceId;
                references.set(reference, result);
                send({ id: message.id, reference });
                break;
            }
            default:
                sendResult(message.id, result);
                break;
        }
    } catch (error) {
        sendError(message.id, error);
    }
}

// Requests always start with their identifier, so a request that cannot be parsed can still be answered
const requestIdPattern = /^\{"id":(\d+)[,}]/;

function processLine(parts) {
    let message;
    try {
        const line = parts.length === 1 ? parts[0] : parts.join("");
        if (line.length === 0) {
            return;
        }

        message = JSON.parse(line);
    } catch (error) {
        // e.g. the message exceeds the maximum length of a string
        let prefix = "";
        for (const part of parts) {
            prefix += part.slice(0, 32);
            if (prefix.length >= 32) {
                break;
            }
        }

        const match = requestIdPattern.exec(prefix);
        if (match === null) {
            // Nothing can be answered, so stop the process instead of leaving calls pending forever
            console.error("Invalid message:", error);
            socket.destroy();
            return;
        }

        sendError(Number(match[1]), error instanceof RangeError ? new RangeError(`The message is too large to be processed by Node.js: ${error.message}`) : error);
        return;
    }

    handle(message);
}

// Only the new chunk is searched for line breaks, and the parts of an incomplete line are joined once complete,
// so a large message split into many chunks is processed in linear time
let incompleteLine = [];
socket.on("data", chunk => {
    let start = 0;
    let index;
    while ((index = chunk.indexOf("\n", start)) >= 0) {
        incompleteLine.push(chunk.slice(start, index));
        start = index + 1;

        // The parts are detached before being processed, so a line that cannot be processed does not corrupt the following ones
        const parts = incompleteLine;
        incompleteLine = [];
        processLine(parts);
    }

    if (start < chunk.length) {
        incompleteLine.push(chunk.slice(start));
    }
});

send({ type: "hello", token, version: process.version });
