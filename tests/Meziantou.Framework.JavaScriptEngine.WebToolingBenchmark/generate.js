// Records the results of the workloads of the Web Tooling Benchmark with Node.js in expected.json: see readme.md.
//
// node generate.js [--dump <directory>] [workload...]
//
// --dump writes the result of each workload to <directory>/<workload>.txt, to compare it with the result of the engine.
"use strict";

const crypto = require("crypto");
const fs = require("fs");
const path = require("path");
const vm = require("vm");

const root = path.join(__dirname, "..", "Meziantou.Framework.JavaScriptEngine.External", "web-tooling-benchmark");
const expectedPath = path.join(__dirname, "expected.json");
const harness = fs.readFileSync(path.join(__dirname, "harness.js"), "utf8");

const args = process.argv.slice(2);
let dump;
if (args[0] === "--dump") {
    dump = args[1];
    args.splice(0, 2);
    fs.mkdirSync(dump, { recursive: true });
}

if (!fs.existsSync(path.join(root, "node_modules"))) {
    console.error(`${root} does not exist: run 'dotnet run eng/javascriptengine/fetch-tests.cs'`);
    process.exit(1);
}

function readFile(file) {
    const fullPath = path.join(root, file);
    if (!fs.statSync(fullPath, { throwIfNoEntry: false })?.isFile())
        return undefined;
    return fs.readFileSync(fullPath, "utf8");
}

// Each workload runs in a new context, which only has the built-ins of ECMAScript, as in a realm of the engine
function createHarness() {
    return vm.runInContext(harness, vm.createContext({}), { filename: "harness.js" })(readFile);
}

const expected = fs.existsSync(expectedPath) ? JSON.parse(fs.readFileSync(expectedPath, "utf8")) : { workloads: {} };
const names = args.length > 0 ? args : createHarness().names;
for (const name of names) {
    const start = process.hrtime.bigint();
    const result = createHarness().run(name);
    const duration = Number((process.hrtime.bigint() - start) / 1000000n);

    // The hash of the UTF-16 code units, as the test computes it
    const sha256 = crypto.createHash("sha256").update(Buffer.from(result, "utf16le")).digest("hex");
    expected.workloads[name] = { length: result.length, sha256 };
    console.log(`${name}: ${result.length} characters (${duration} ms)`);
    if (dump !== undefined)
        fs.writeFileSync(path.join(dump, name + ".txt"), result);
}

const sorted = Object.fromEntries(Object.entries(expected.workloads).sort(([a], [b]) => a < b ? -1 : a > b ? 1 : 0));
fs.writeFileSync(expectedPath, JSON.stringify({ node: process.version, workloads: sorted }, null, 2) + "\n");
