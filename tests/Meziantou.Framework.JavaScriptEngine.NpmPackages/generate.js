// Records the results of the workloads with Node.js in expected/<workload>.txt: see readme.md.
//
// node generate.js [--check] [workload...]
//
// --check compares the results with the files instead of writing them.
"use strict";

const childProcess = require("child_process");
const fs = require("fs");
const path = require("path");
const vm = require("vm");

// The modules of a vm context need --experimental-vm-modules. The engine runs the workloads in the UTC time zone and with
// the en-US locale.
if (process.env.NPM_PACKAGES_GENERATE !== "1") {
  const result = childProcess.spawnSync(process.execPath, ["--experimental-vm-modules", "--no-warnings", __filename, ...process.argv.slice(2)], {
    stdio: "inherit",
    env: { ...process.env, NPM_PACKAGES_GENERATE: "1", TZ: "UTC", LANG: "en_US.UTF-8", LC_ALL: "en_US.UTF-8" },
  });
  process.exit(result.status ?? 1);
}

// The engine does not track the promises that are rejected without a handler: Node.js must not exit for them
process.on("unhandledRejection", () => { });

const root = path.join(__dirname, "..", "Meziantou.Framework.JavaScriptEngine.External", "npm-packages");
const harness = fs.readFileSync(path.join(__dirname, "harness.js"), "utf8");
const expectedDirectory = path.join(__dirname, "expected");

const args = process.argv.slice(2);
const check = args[0] === "--check";
if (check)
  args.shift();

if (!fs.existsSync(path.join(root, "node_modules"))) {
  console.error(`${root}/node_modules does not exist: run 'dotnet run eng/javascriptengine/fetch-tests.cs'`);
  process.exit(1);
}

// '/workloads' is the directory of the workloads, the other paths are in the installed packages
function readFile(file) {
  const fullPath = file.startsWith("/workloads/") ? path.join(__dirname, file) : path.join(root, file);
  if (!fs.statSync(fullPath, { throwIfNoEntry: false })?.isFile())
    return undefined;
  return fs.readFileSync(fullPath, "utf8");
}

// Each workload runs in a new context, which only has the built-ins of ECMAScript, as in a realm of the engine
async function runWorkload(name) {
  const context = vm.createContext({});
  const modules = new Map();
  let api;

  function getModule(specifier, referrer) {
    const { path: modulePath, format } = api.resolve(specifier, referrer);
    let module = modules.get(modulePath);
    if (module === undefined) {
      if (format === "module") {
        module = new vm.SourceTextModule(readFile(modulePath), {
          context,
          identifier: modulePath,
          initializeImportMeta(meta) {
            meta.url = "file://" + modulePath;
          },
          importModuleDynamically: (specifier, referrer) => importModule(specifier, referrer.identifier),
        });
      }
      else {
        // A CommonJS module, a JSON file or a Node.js module
        const exportNames = api.getExportNames(modulePath);
        module = new vm.SyntheticModule(exportNames, function () {
          const exports = api.require(modulePath);
          for (const name of exportNames)
            this.setExport(name, name === "default" ? exports : exports[name]);
        }, { context, identifier: modulePath });
      }

      modules.set(modulePath, module);
    }

    return module;
  }

  // Links the module and the modules it imports, synchronously
  function link(module) {
    const visited = new Set();
    const linkRequests = current => {
      if (visited.has(current) || current.status !== "unlinked")
        return;

      visited.add(current);
      const dependencies = current.moduleRequests.map(request => getModule(request.specifier, current.identifier));
      if (current instanceof vm.SourceTextModule)
        current.linkRequests(dependencies);
      for (const dependency of dependencies)
        linkRequests(dependency);
    };
    linkRequests(module);
    if (module.status === "unlinked")
      module.instantiate();
    return module;
  }

  async function importModule(specifier, referrer) {
    const module = link(getModule(specifier, referrer));
    await module.evaluate();
    return module;
  }

  // require() of an ES module: the module must be evaluated synchronously (no top-level await)
  function requireModule(modulePath) {
    const module = link(getModule(modulePath, undefined));
    module.evaluate();
    if (module.status === "errored")
      throw module.error;
    if (module.status !== "evaluated")
      throw new Error("require() of the ES module '" + modulePath + "' that uses top-level await is not supported");
    return module.namespace;
  }

  const script = new vm.Script(harness, {
    filename: "/harness.js",
    importModuleDynamically: specifier => importModule(specifier, undefined),
  });
  api = script.runInContext(context)({ readFile, requireModule });
  const state = api.run(name);
  for (; ;) {
    // Runs the promise jobs
    await new Promise(resolve => setImmediate(resolve));
    if (state.done)
      return state.result;
    if (!api.runTimer())
      return "Error: the workload did not complete";
  }
}

const names = args.length > 0 ? args : fs.readdirSync(path.join(__dirname, "workloads")).filter(name => name.endsWith(".mjs")).map(name => name.slice(0, -4)).sort();
let failed = false;
fs.mkdirSync(expectedDirectory, { recursive: true });
(async () => {
  for (const name of names) {
    const start = process.hrtime.bigint();
    const result = await runWorkload(name);
    const duration = Number((process.hrtime.bigint() - start) / 1000000n);
    const expectedPath = path.join(expectedDirectory, name + ".txt");
    if (check) {
      const same = fs.existsSync(expectedPath) && fs.readFileSync(expectedPath, "utf8") === result;
      failed ||= !same;
      console.log(`${name}: ${same ? "same" : "different"} (${duration} ms)`);
    }
    else {
      fs.writeFileSync(expectedPath, result);
      console.log(`${name}: ${result.length} characters (${duration} ms)${result.startsWith("Error: ") ? " " + result.split("\n")[0] : ""}`);
    }
  }

  process.exit(failed ? 1 : 0);
})();
