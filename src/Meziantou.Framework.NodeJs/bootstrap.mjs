// Bootstrap script executed by Meziantou.Framework.NodeJs.NodeJsHost.
// It is read from the standard input with "--input-type=module", so dynamic imports are resolved relative to the working directory.
// Messages are newline-delimited JSON exchanged over the local socket provided by the .NET host.
const endpoint = process.env.MEZIANTOU_NODEJS_ENDPOINT;
const token = process.env.MEZIANTOU_NODEJS_TOKEN;
delete process.env.MEZIANTOU_NODEJS_ENDPOINT;
delete process.env.MEZIANTOU_NODEJS_TOKEN;

await startRuntime({ endpoint, token });

// Runs the calls of the .NET host on the current thread: the main thread, or a worker thread when NodeJsHostOptions.WorkerThreads is set.
// Worker threads evaluate the source code of this function, so it must not use anything declared outside of it, and it only uses dynamic imports,
// which work whatever the module type of the code evaluated by a worker.
async function startRuntime(config) {
    const { default: net } = await import("node:net");
    const { default: path } = await import("node:path");
    const { AsyncResource, createHook, executionAsyncResource } = await import("node:async_hooks");
    const { createRequire } = await import("node:module");
    const { pathToFileURL } = await import("node:url");
    const { Worker, parentPort } = await import("node:worker_threads");

    const { endpoint, token } = config;
    const isWorker = config.worker !== undefined;

    // Errors that are not related to a call (e.g. thrown by a timer callback) must not stop the thread, as it would fail all the calls in progress
    process.on("uncaughtException", error => console.error("Uncaught exception:", error));
    process.on("unhandledRejection", reason => console.error("Unhandled promise rejection:", reason));

    const AsyncFunction = (async function () { }).constructor;
    const nodeRequire = createRequire(path.join(process.cwd(), "/"));

    // Functions that compile code. They cannot be called by name, so a module, export, or member name cannot be used to run arbitrary code.
    const codeCompilingFunctions = new Set([
        Function,
        AsyncFunction,
        (function* () { }).constructor,
        (async function* () { }).constructor,
        globalThis.eval,
    ]);

    const canWriteRawJson = typeof JSON.rawJSON === "function";

    // Identifier of the call whose code runs on the thread, shared with the thread that answers the host while the event loop is blocked:
    // 0 when the event loop is idle, -1 for code that does not belong to a call
    const IdleCall = 0n;
    const UnattributedCode = -1n;
    const callSymbol = Symbol("MeziantouNodeJsCall");
    const microtaskSymbol = Symbol("MeziantouNodeJsMicrotask");
    let tracking = false;

    // Connection with the host, created once everything is declared
    let socket;

    // Values referenced by the .NET host (JSReference), by identifier
    const references = new Map();
    let nextReferenceId = 0;

    // Worker threads started for NodeJsHostOptions.WorkerThreads, by index
    let workerSlots = [];

    // JSON.stringify escapes a lone surrogate (e.g. a string cut in the middle of an emoji) as \udXXX, which System.Text.Json cannot convert to a string.
    // It is replaced by U+FFFD, as String.prototype.toWellFormed does. Valid surrogate pairs are not escaped, and a backslash preceded by an odd number of backslashes is not an escape.
    const loneSurrogateEscapePattern = /(?<!\\)((?:\\\\)*)\\ud[89a-f][0-9a-f]{2}/g;

    // The socket of a worker thread is created when the host may not accept connections yet (e.g. a named pipe instance is being created), so connecting is retried until the startup timeout
    function connectWorker() {
        const retryableErrors = new Set(["ENOENT", "EBUSY", "EAGAIN", "ECONNREFUSED"]);
        return new Promise((resolve, reject) => {
            const attempt = () => {
                const connection = net.connect(endpoint);
                const onError = error => {
                    connection.destroy();
                    if (retryableErrors.has(error.code) && Date.now() < config.connectDeadline) {
                        setTimeout(attempt, 10);
                    } else {
                        reject(error);
                    }
                };

                connection.once("error", onError);
                connection.once("connect", () => {
                    connection.off("error", onError);
                    parentPort.postMessage("connected");
                    resolve(connection);
                });
            };

            attempt();
        });
    }

    function stringify(value, replacer) {
        const json = JSON.stringify(value, replacer);
        return json !== undefined && json.includes("\\ud") ? json.replace(loneSurrogateEscapePattern, "$1�") : json;
    }

    function send(message) {
        socket.write(stringify(message) + "\n");
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
        const json = stringify(result === undefined ? null : result, resultReplacer);
        if (json === undefined) {
            // JSON.stringify omits the value, so the response would look like the response of a void call
            const description = typeof result === "function" || typeof result === "symbol" ? `a ${typeof result}` : "a value whose JSON representation is undefined";
            throw new TypeError(`The result is ${description}, which cannot be serialized as JSON. Use a call that returns a JSReference to keep it in the Node.js process, or a void call to ignore it.`);
        }

        // Written in several parts, so a large result is not copied into a new string
        socket.cork();
        socket.write(`{"id":${id},"result":`);
        socket.write(json);
        socket.write("}\n");
        socket.uncork();
    }

    // Bounds the chain of causes, which can be cyclic
    const MaxErrorCauseDepth = 8;

    // Never throws, as an error thrown while reporting an error would leave the call without a response
    function describeError(error, depth = 0) {
        try {
            if (error instanceof Error) {
                const description = {
                    name: String(error.name),
                    message: String(error.message),
                    stack: typeof error.stack === "string" ? error.stack : null,
                };

                // e.g. "ENOENT" for the errors of node:fs
                const code = error.code;
                if (typeof code === "string" || typeof code === "number") {
                    description.code = String(code);
                }

                const cause = error.cause;
                if (cause !== undefined && depth < MaxErrorCauseDepth) {
                    description.cause = describeError(cause, depth + 1);
                }

                return description;
            }

            return { name: typeof error, message: describeThrownValue(error), stack: null };
        } catch {
            return { name: typeof error, message: "The error cannot be converted to a string", stack: null };
        }
    }

    // An object thrown as an error (e.g. { code: "E_INVALID" }) is described by its JSON representation, which is more useful than "[object Object]"
    function describeThrownValue(value) {
        if (typeof value === "object" && value !== null) {
            try {
                const json = JSON.stringify(value, resultReplacer);
                if (json !== undefined) {
                    return json;
                }
            } catch {
                // e.g. a cyclic object
            }
        }

        return String(value);
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

    // Checks whether a value can be used with "new" without calling it. Arrow functions and methods are functions that are not constructors.
    function isConstructor(value) {
        if (typeof value !== "function") {
            return false;
        }

        try {
            Reflect.construct(Object, [], value);
            return true;
        } catch {
            return false;
        }
    }

    function constructInstance(constructor, args, description) {
        if (!isConstructor(constructor)) {
            throw new TypeError(`${description} is not a constructor`);
        }

        if (codeCompilingFunctions.has(constructor)) {
            throw new TypeError("Functions that compile code, such as the Function constructor or eval, cannot be called");
        }

        return Reflect.construct(constructor, args);
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
        const module = await importModule(toSpecifier(message.module));

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

        if (message.construct === true) {
            return constructInstance(target, args, message.export === null || message.export === undefined ? `The default export of the module '${message.module}'` : `The export '${message.export}' of the module '${message.module}'`);
        }

        if (typeof target === "function") {
            return await callFunction(target, container, args);
        }

        return target;
    }

    async function invokeMember(target, member, args, construct) {
        if (construct && (member === null || member === undefined)) {
            return constructInstance(target, args, "The referenced value");
        }

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
        if (construct) {
            return constructInstance(value, args, `The member '${member}' of the referenced value`);
        }

        if (typeof value === "function") {
            return await callFunction(value, target, args);
        }

        return value;
    }

    async function evaluate(message, args) {
        // "args" is only declared when the caller provides arguments, so code declaring its own "args" variable still compiles
        const require = tracking ? requireOutsideOfCall : nodeRequire;
        if (args === undefined) {
            return await new AsyncFunction("require", message.code)(require);
        }

        return await new AsyncFunction("require", "args", message.code)(require, args);
    }

    // Resources that run their callback once (promises, timeouts, I/O requests...) belong to the call that creates them.
    // Long-lived resources (intervals, sockets, servers, workers...) can run code for other calls, e.g. a connection pool created by the first call that needs it,
    // so the code they run does not belong to a call: it would make the host kill the process for a call that is not canceled.
    const oneShotResourceTypePattern = /^(?:PROMISE|Microtask|TickObject|Immediate)$|REQ|CONNECTWRAP|WRITEWRAP|SHUTDOWNWRAP|SENDWRAP|QUERYWRAP/;
    const oneShotResourceTypes = new Map();

    function isOneShotResource(type, resource) {
        if (type === "Timeout") {
            // _repeat is set when the timeout is created by setInterval
            return !resource._repeat;
        }

        let result = oneShotResourceTypes.get(type);
        if (result === undefined) {
            result = oneShotResourceTypePattern.test(type);
            oneShotResourceTypes.set(type, result);
        }

        return result;
    }

    // Microtasks (promise reactions, queueMicrotask, process.nextTick) run between the callbacks of the event loop, so they are part of the same turn of the event loop
    function isMacrotask(resource) {
        return !(resource instanceof Promise) && resource?.[microtaskSymbol] !== true;
    }

    // Measures for how long the event loop of a thread has been running the same turn, from the state shared with the thread (see startTracking).
    // The turn is observed by the thread that answers the host, so the duration does not depend on the clock of the observed thread, nor on the time the host takes to ask.
    // It is the time since the turn was first observed, so it never exceeds the time the event loop has been running it.
    // The watchdog thread evaluates its source code, so it must not use anything declared outside of it.
    function createTurnObserver(state) {
        const runningCall = new BigInt64Array(state, 0, 1);
        const turn = new Int32Array(state, BigInt64Array.BYTES_PER_ELEMENT, 1);
        let observedTurn;
        let observedSince = 0;
        return () => {
            // The observed thread changes the turn before the call, so reading the call first never associates it with an older turn
            const call = Atomics.load(runningCall, 0);
            const currentTurn = Atomics.load(turn, 0);
            const now = performance.now();
            if (currentTurn !== observedTurn) {
                observedTurn = currentTurn;
                observedSince = now;
            }

            return { running: call, elapsed: Math.floor(now - observedSince) };
        };
    }

    // Runs on a worker thread, so it answers the host even when the event loop of the main thread is blocked.
    // The host asks it which call runs on the main thread, and for how long the event loop has been running the same turn,
    // so it can kill the process only when a canceled call blocks the event loop (NodeJsHostOptions.UnresponsiveTimeout).
    // Its source code is evaluated by the worker, with createTurnObserver, so it only uses dynamic imports, which work whatever the module type of the worker.
    async function watchdog() {
        const { workerData } = await import("node:worker_threads");
        const { default: net } = await import("node:net");
        const observe = createTurnObserver(workerData.state);
        const socket = net.connect(workerData.endpoint);
        socket.setEncoding("utf8");
        socket.on("error", () => socket.destroy());
        socket.write(JSON.stringify({ type: "hello", token: workerData.token }) + "\n");

        // Each line is a request for the running call
        let incompleteLine = "";
        socket.on("data", chunk => {
            incompleteLine += chunk;
            let index;
            while ((index = incompleteLine.indexOf("\n")) >= 0) {
                incompleteLine = incompleteLine.slice(index + 1);
                const { running, elapsed } = observe();
                socket.write(`{"running":${running},"elapsed":${elapsed}}\n`);
            }
        });
    }

    // Tracks the call whose code runs on the current thread, and the turns of its event loop, in the state shared with the thread that answers the host
    function startTracking(state) {
        const runningCall = new BigInt64Array(state, 0, 1);
        const turn = new Int32Array(state, BigInt64Array.BYTES_PER_ELEMENT, 1);
        const stack = [];
        let current = IdleCall;
        const setRunningCall = call => {
            current = call;
            Atomics.store(runningCall, 0, call);
        };

        // A new turn starts and ends with each callback run by the event loop, so the turn does not change while microtasks keep the event loop busy
        const changeTurnIfMacrotask = () => {
            if (stack.length === 0 && isMacrotask(executionAsyncResource())) {
                Atomics.add(turn, 0, 1);
            }
        };

        // Asynchronous operations (promises, timeouts, I/O...) belong to the call that started them, so the code they run is attributed to it.
        // An exception thrown by a hook stops the process, so the hooks never throw.
        createHook({
            init(asyncId, type, triggerAsyncId, resource) {
                try {
                    if (type === "TickObject" || type === "Microtask") {
                        resource[microtaskSymbol] = true;
                    }

                    const call = executionAsyncResource()?.[callSymbol];
                    if (call !== undefined && isOneShotResource(type, resource)) {
                        resource[callSymbol] = call;
                    }
                } catch {
                    // The resource is not extensible
                }
            },
            before() {
                changeTurnIfMacrotask();
                stack.push(current);
                setRunningCall(executionAsyncResource()?.[callSymbol] ?? UnattributedCode);
            },
            after() {
                const call = stack.pop() ?? IdleCall;
                changeTurnIfMacrotask();
                setRunningCall(call);
            },
        }).enable();
        tracking = true;
    }

    function startWatchdog() {
        const state = new SharedArrayBuffer(BigInt64Array.BYTES_PER_ELEMENT * 2);
        startTracking(state);

        const code = `const createTurnObserver = ${createTurnObserver};\n(${watchdog})();`;
        const worker = new Worker(code, { eval: true, workerData: { endpoint, token, state } });
        worker.unref();
        worker.on("error", error => {
            console.error("The watchdog failed:", error);
            socket.destroy();
        });
    }

    // Starts the worker threads that run the calls (NodeJsHostOptions.WorkerThreads). Each one connects to the host, which sends it calls directly.
    // A worker thread that exits (e.g. process.exit() or terminated by the host) is restarted, and the host is notified, so it fails the calls of the worker thread.
    function startWorkers(message) {
        // The endpoint and the token are removed from workerData before running the calls, so the code of the calls cannot read them
        const code = `(async () => {
            const workerThreads = await import("node:worker_threads");
            const config = { ...workerThreads.workerData };
            delete workerThreads.workerData.endpoint;
            delete workerThreads.workerData.token;
            await (${startRuntime})(config);
        })();`;

        const connectTimeout = typeof message.connectTimeout === "number" ? message.connectTimeout : Infinity;
        const spawn = (index, generation) => {
            const state = message.tracking ? new SharedArrayBuffer(BigInt64Array.BYTES_PER_ELEMENT * 2) : undefined;
            const slot = { generation, connected: false, observe: state === undefined ? undefined : createTurnObserver(state) };
            slot.worker = new Worker(code, { eval: true, workerData: { endpoint, token, worker: index, generation, state, connectDeadline: Date.now() + connectTimeout } });
            slot.worker.on("message", () => slot.connected = true);
            slot.worker.on("error", error => console.error(`The worker thread ${index} failed:`, error));
            slot.worker.on("exit", exitCode => {
                send({ type: "workerExit", worker: index, generation, exitCode });

                // Restarting a worker thread that cannot start would never end, so the process exits instead
                if (!slot.connected) {
                    console.error(`The worker thread ${index} exited before connecting to the host`);
                    socket.destroy();
                    return;
                }

                spawn(index, generation + 1);
            });
            workerSlots[index] = slot;
        };

        for (let i = 0; i < message.count; i++) {
            spawn(i, 1);
        }
    }

    function getRunningCalls() {
        const result = [];
        workerSlots.forEach((slot, worker) => {
            if (slot.observe !== undefined) {
                const { running, elapsed } = slot.observe();

                // Identifiers of calls are smaller than Number.MAX_SAFE_INTEGER, and a BigInt cannot be serialized before Node.js 21
                result.push({ worker, generation: slot.generation, running: Number(running), elapsed });
            }
        });

        return result;
    }

    // Module initialization (the top-level code of a module, and the timers and connections it creates) is shared by all the calls,
    // so it does not belong to the call that happens to load the module first
    function runOutsideOfCall(fn, ...args) {
        if (!tracking) {
            return fn(...args);
        }

        return new AsyncResource("MeziantouNodeJsModuleLoading").runInAsyncScope(fn, undefined, ...args);
    }

    function importModule(specifier) {
        return runOutsideOfCall(() => import(specifier));
    }

    // The require function passed to evaluated code. Its properties (resolve, cache...) are the ones of the actual require function.
    const requireOutsideOfCall = Object.assign(function require(id) { return runOutsideOfCall(nodeRequire, id); }, nodeRequire);

    // Runs the code of a call in its own asynchronous context once the tracking is started, so the code it runs is attributed to it
    function run(message) {
        if (!tracking || typeof message.id !== "number") {
            handle(message);
            return;
        }

        const resource = new AsyncResource("MeziantouNodeJsCall");
        resource[callSymbol] = BigInt(message.id);
        resource.runInAsyncScope(handle, undefined, message);
    }

    async function handle(message) {
        switch (message.type) {
            case "release":
                references.delete(message.reference);
                return;

            case "watchdog":
                try {
                    startWatchdog();
                } catch (error) {
                    // The host waits for the watchdog to connect, so stop the process instead
                    console.error("Cannot start the watchdog:", error);
                    socket.destroy();
                }

                return;

            case "workers":
                try {
                    startWorkers(message);
                } catch (error) {
                    // The host waits for the worker threads to connect, so stop the process instead
                    console.error("Cannot start the worker threads:", error);
                    socket.destroy();
                }

                return;

            case "terminateWorker": {
                // The worker thread is restarted once it exits
                const slot = workerSlots[message.worker];
                if (slot?.generation === message.generation) {
                    slot.worker.terminate();
                }

                return;
            }
        }

        try {
            // Arguments and targets are resolved before awaiting anything, so a reference released by a later message is still available to this call
            let result;
            switch (message.type) {
                case "invoke":
                    result = invoke(message, decodeArguments(message));
                    break;
                case "invokeReference":
                    result = invokeMember(getReference(message.reference), message.member, decodeArguments(message), message.construct === true);
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
                case "running":
                    result = getRunningCalls();
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

        run(message);
    }

    // Each call runs on a worker thread, so the asynchronous context of the calls is tracked by the worker threads
    if (isWorker && config.state !== undefined) {
        startTracking(config.state);
    }

    socket = isWorker ? await connectWorker() : net.connect(endpoint);
    socket.setEncoding("utf8");
    socket.on("error", () => process.exit(1));

    // On a worker thread, it only stops the worker thread
    socket.on("close", () => process.exit(0));

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

    if (isWorker) {
        send({ type: "hello", token, worker: config.worker, generation: config.generation });
    } else {
        send({ type: "hello", token, version: process.version });
    }
}
