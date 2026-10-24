// Runs the workloads of the Web Tooling Benchmark (https://github.com/v8/web-tooling-benchmark). The benchmark is built
// with webpack; this file replaces the bundle: a CommonJS module loader that gives the modules the browser versions of
// the Node.js modules, as webpack does. It runs the same way in Node.js (generate.js, in a vm context) and in the engine
// (WebToolingBenchmarkTests.cs).
//
// The script evaluates to a function that takes readFile(path): the content of a file, or undefined when there is no file
// at that path. Paths are absolute and use '/': '/' is the root of the benchmark (src, third_party, node_modules). The
// function returns the names of the workloads, and run(name), which runs a workload and returns its result as a string.
(function (readFile) {
    "use strict";

    // The Node.js modules, replaced by the browser versions webpack uses (node-libs-browser) or by the mocks of the
    // benchmark (webpack.config.js maps 'fs' to a virtual file system and 'module' to an empty object).
    const builtins = {
        assert: "assert/",
        buffer: "buffer/",
        events: "events/",
        os: "os-browserify/browser.js",
        path: "path-browserify",
        process: "process/browser.js",
        punycode: "punycode/",
        stream: "stream-browserify",
        string_decoder: "string_decoder/",
        tty: "tty-browserify",
        vm: "vm-browserify",
        sys: "util/util.js",
        util: "util/util.js",
        url: "url/",
    };
    const builtinsDirectory = "/node_modules/node-libs-browser";

    // 'npm install' builds these two files with 'uglifyjs --self' (package.json): the library files wrapped in a function
    const bundles = {
        "/build/terser-bundled.js": { name: "Terser", directory: "/node_modules/terser" },
        "/build/uglify-js-bundled.js": { name: "UglifyJS", directory: "/node_modules/uglify-js" },
    };

    const cache = new Map();
    const global = globalThis;
    let processModule;
    let bufferModule;

    // What the libraries write to the console is part of the result
    const messages = [];
    const log = level => (...args) => messages.push(level + ": " + args.map(String).join(" "));
    globalThis.console = { log: log("log"), info: log("info"), warn: log("warn"), error: log("error"), debug: log("debug"), trace: log("trace") };

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
    const hostReadFile = readFile;
    readFile = path => {
        let content = files.get(path);
        if (content === undefined && !files.has(path)) {
            content = hostReadFile(path);
            files.set(path, content);
        }

        return content;
    };

    function isFile(path) {
        return path in bundles || readFile(path) !== undefined;
    }

    const packages = new Map();

    // The package.json of the package that contains the path, as { directory, json }
    function findPackage(path) {
        for (let directory = path; ; directory = dirname(directory)) {
            if (directory.endsWith("/node_modules"))
                return undefined;
            let result = packages.get(directory);
            if (result === undefined) {
                const content = readFile(directory + "/package.json");
                result = content === undefined ? null : { directory, json: JSON.parse(content) };
                packages.set(directory, result);
            }

            if (result !== null)
                return result;
            if (directory === "/")
                return undefined;
        }
    }

    // The 'browser' field of package.json (https://github.com/defunctzombie/package-browser-field-spec), which webpack
    // uses: a module or a file of the package is replaced by another file, or by an empty object (false)
    function browserReplacement(path) {
        const pkg = findPackage(dirname(path));
        const browser = pkg?.json.browser;
        if (browser === null || typeof browser !== "object")
            return path;

        for (const [key, value] of Object.entries(browser)) {
            if (key.startsWith(".")) {
                const target = normalize(pkg.directory + "/" + key);
                if (target === path || target + ".js" === path)
                    return value === false ? false : normalize(pkg.directory + "/" + value);
            }
        }

        return path;
    }

    function loadAsFile(path) {
        for (const candidate of [path, path + ".js", path + ".json"]) {
            if (isFile(candidate))
                return candidate;
        }

        return undefined;
    }

    function loadAsDirectory(path) {
        const packageJson = readFile(path + "/package.json");
        if (packageJson !== undefined) {
            const json = JSON.parse(packageJson);
            const main = typeof json.browser === "string" ? json.browser : json.main;
            if (typeof main === "string") {
                const target = normalize(path + "/" + main);
                const resolved = loadAsFile(target) ?? loadIndex(target);
                if (resolved !== undefined)
                    return resolved;
            }
        }

        return loadIndex(path);
    }

    function loadIndex(path) {
        return loadAsFile(path + "/index");
    }

    // Returns the path of the module, false for an empty module, or a special module name ('fs', 'module')
    function resolve(request, directory) {
        if (!request.startsWith(".") && !request.startsWith("/")) {
            const browser = findPackage(directory)?.json.browser;
            if (browser !== null && typeof browser === "object" && Object.hasOwn(browser, request)) {
                const value = browser[request];
                if (value === false)
                    return false;
                request = value;
            }
        }

        if (request === "fs" || request === "module")
            return request;

        if (Object.hasOwn(builtins, request))
            return resolve(builtins[request], builtinsDirectory);

        let resolved;
        if (request.startsWith("./") || request.startsWith("../") || request.startsWith("/") || request === "." || request === "..") {
            const path = normalize(request.startsWith("/") ? request : directory + "/" + request);
            resolved = loadAsFile(path) ?? loadAsDirectory(path);
        }
        else {
            for (let current = directory; resolved === undefined; current = dirname(current)) {
                if (!current.endsWith("/node_modules")) {
                    const path = normalize(current + "/node_modules/" + request);
                    resolved = loadAsFile(path) ?? loadAsDirectory(path);
                }

                if (current === "/")
                    break;
            }
        }

        if (resolved === undefined)
            throw new Error("Cannot find module '" + request + "' from '" + directory + "'");

        const replacement = browserReplacement(resolved);
        if (replacement === false || replacement === resolved)
            return replacement;
        return loadAsFile(replacement) ?? loadAsDirectory(replacement);
    }

    function readSource(path) {
        const bundle = bundles[path];
        if (bundle === undefined)
            return readFile(path);

        // tools/node.js lists the files of the library
        const files = [];
        const toolsSource = readFile(bundle.directory + "/tools/node.js");
        const listStart = toolsSource.indexOf("[");
        const listEnd = toolsSource.indexOf("]", listStart);
        for (const match of toolsSource.substring(listStart, listEnd).matchAll(/"([^"]+)"/g))
            files.push(readFile(normalize(bundle.directory + "/tools/" + match[1])));

        return "const " + bundle.name + " = module.exports = {};\n(function(exports){\n" + files.join("\n\n") + "\n})(typeof " + bundle.name + "=='undefined'?(" + bundle.name + "={}):" + bundle.name + ");\n";
    }

    function evaluate(source, path) {
        return (0, eval)(source + "\n//# sourceURL=" + path);
    }

    // The virtual file system of the benchmark (src/vfs.js) only has the files of third_party
    function readThirdParty(path) {
        path = normalize(path);
        return path.startsWith("/third_party/") ? readFile(path) : undefined;
    }

    function createFs() {
        return {
            existsSync(path) {
                return readThirdParty(path) !== undefined;
            },
            readFileSync(path) {
                const content = readThirdParty(path);
                if (content === undefined)
                    throw new Error("ENOENT: no such file or directory, open '" + path + "'");
                return content;
            },
            readFile(path, options, callback) {
                callback ??= options;
                let content;
                try {
                    content = this.readFileSync(path);
                }
                catch (error) {
                    Promise.resolve().then(() => callback(error));
                    return;
                }

                Promise.resolve().then(() => callback(null, content));
            },
        };
    }

    function load(path) {
        if (path === false || path === "module")
            return {};
        if (path === "fs")
            return createFs();

        const cached = cache.get(path);
        if (cached !== undefined)
            return cached.exports;

        const module = { id: path, filename: path, loaded: false, exports: {} };
        cache.set(path, module);
        const source = readSource(path);
        if (path.endsWith(".json")) {
            module.exports = JSON.parse(source);
        }
        else {
            const directory = dirname(path);
            const require = request => load(resolve(request, directory));
            require.resolve = request => resolve(request, directory);
            require.cache = {};

            // webpack gives these free variables to the modules that use them, in a function around the module
            const body = source.startsWith("#!") ? "//" + source : source;
            const wrapper = evaluate("(function (process, Buffer, global, setImmediate, clearImmediate) { return function (exports, require, module, __filename, __dirname) {" + body + "\n}; })", path);
            const process = /\bprocess\b/.test(source) ? getProcess() : undefined;
            const buffer = /\bBuffer\b/.test(source) ? getBuffer() : undefined;
            wrapper(process, buffer, global, setImmediate, clearImmediate).call(module.exports, module.exports, require, module, path, directory);
        }

        module.loaded = true;
        return module.exports;
    }

    function getProcess() {
        processModule ??= load(resolve("process", "/"));
        return processModule;
    }

    function getBuffer() {
        bufferModule ??= load(resolve("buffer", "/")).Buffer;
        return bufferModule;
    }

    function setImmediate(callback, ...args) {
        Promise.resolve().then(() => callback(...args));
        return 0;
    }

    function clearImmediate() {
    }

    function require(request) {
        return load(resolve(request, "/"));
    }

    // Runs the 'fn' of src/<name>-benchmark.js once
    const run = name => require("/src/" + name + "-benchmark.js").fn();
    const json = value => JSON.stringify(value, null, 1);
    const read = name => require("fs").readFileSync("third_party/" + name, "utf8");

    // Each workload returns the result of the benchmark as a string. The benchmarks whose 'fn' returns nothing are
    // rewritten to return what they compute.
    const workloads = {
        "acorn": () => json(run("acorn")),
        "babel": () => json(run("babel").map(result => result.code)),
        "babel-minify": () => json(run("babel-minify").map(result => result.code)),
        "babylon": () => json(run("babylon")),
        "buble": () => json(run("buble").map(result => [result.code, result.map.toString()])),
        "chai": () => {
            // The assertions throw when they fail
            run("chai");
            return "passed";
        },
        "coffeescript": () => run("coffeescript"),
        "espree": () => json(run("espree")),
        "esprima": () => json(run("esprima")),
        "jshint": () => {
            const jshint = require("jshint");
            return json(["lodash.core-4.17.4.js", "preact-8.2.5.js", "underscore-1.8.3.js"].map(name => {
                jshint.JSHINT(read(name));
                return jshint.JSHINT.data();
            }));
        },
        "lebab": () => json(run("lebab")),
        "postcss": () => json(run("postcss")),
        "prepack": () => json(run("prepack")),
        "prettier": () => json(run("prettier")),
        "source-map": () => {
            const sourceMap = require("source-map");
            return json(["lodash.min-4.17.4.js.map", "preact-8.2.5.js.map", "source-map.min-0.5.7.js.map", "underscore.min-1.8.3.js.map"].map(name => {
                const payload = read(name);
                const smc = new sourceMap.SourceMapConsumer(payload);
                const smg = sourceMap.SourceMapGenerator.fromSourceMap(smc);
                const fswsm = sourceMap.SourceNode.fromStringWithSourceMap(payload, smc);
                return [smg.toString(), fswsm.toString()];
            }));
        },
        "terser": () => json(run("terser")),
        "typescript": () => json(run("typescript")),
        "uglify-js": () => json(run("uglify-js")),
    };

    return {
        names: Object.keys(workloads),
        run(name) {
            let result = workloads[name]();
            if (messages.length > 0)
                result += "\n" + messages.join("\n");
            return result;
        },
    };
})
