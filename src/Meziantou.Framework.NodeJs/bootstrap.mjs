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

const socket = net.connect(endpoint);
socket.setEncoding("utf8");
socket.on("error", () => process.exit(1));
socket.on("close", () => process.exit(0));

function send(message) {
    socket.write(JSON.stringify(message) + "\n");
}

function sendResult(id, result) {
    let json;
    try {
        json = JSON.stringify({ id, result: result === undefined ? null : result });
    } catch (error) {
        sendError(id, error);
        return;
    }

    socket.write(json + "\n");
}

function sendError(id, error) {
    const isError = error instanceof Error;
    send({
        id,
        error: {
            name: isError ? error.name : typeof error,
            message: isError ? error.message : String(error),
            stack: isError && typeof error.stack === "string" ? error.stack : null,
        },
    });
}

function toSpecifier(module) {
    return path.isAbsolute(module) ? pathToFileURL(module).href : module;
}

async function invoke(message) {
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
    } else if (hasObjectDefault && message.export in module.default) {
        container = module.default;
        target = module.default[message.export];
    } else {
        throw new Error(`The module '${message.module}' does not export '${message.export}'`);
    }

    if (typeof target === "function") {
        return await target.apply(container, message.args ?? []);
    }

    return target;
}

async function evaluate(message) {
    const fn = new AsyncFunction("require", message.code);
    return await fn(require);
}

async function handle(message) {
    try {
        let result;
        switch (message.type) {
            case "invoke":
                result = await invoke(message);
                break;
            case "eval":
                result = await evaluate(message);
                break;
            default:
                throw new Error(`Unknown message type '${message.type}'`);
        }

        sendResult(message.id, result);
    } catch (error) {
        sendError(message.id, error);
    }
}

// Only the new chunk is searched for line breaks, and the parts of an incomplete line are joined once complete,
// so a large message split into many chunks is processed in linear time
let incompleteLine = [];
socket.on("data", chunk => {
    let start = 0;
    let index;
    while ((index = chunk.indexOf("\n", start)) >= 0) {
        incompleteLine.push(chunk.slice(start, index));
        const line = incompleteLine.join("");
        incompleteLine = [];
        start = index + 1;
        if (line.length > 0) {
            handle(JSON.parse(line));
        }
    }

    if (start < chunk.length) {
        incompleteLine.push(chunk.slice(start));
    }
});

send({ type: "hello", token, version: process.version });
