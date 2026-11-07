# Web Tooling Benchmark

Runs the workloads of V8's [Web Tooling Benchmark](https://github.com/v8/web-tooling-benchmark) in the engine and
compares their results with the results of Node.js. test262 checks the edge cases of the specification one by one; these
workloads are large programs where many features interact: acorn, babel, babel-minify, babylon, buble, chai,
coffeescript, espree, esprima, jshint, lebab, postcss (with autoprefixer and postcss-nested), prepack, prettier,
source-map, terser, typescript and uglify-js, on the fixed inputs of the benchmark (jQuery, lodash, Vue, Bootstrap...).

## Files

- `web-tooling-benchmark.json`: the commit of the benchmark, and the npm packages to install. They are not committed:
  `dotnet run eng/javascriptengine/fetch-tests.cs` downloads them in
  `tests/Meziantou.Framework.JavaScriptEngine.External/web-tooling-benchmark`. The packages come from the
  `package-lock.json` of the benchmark, with its integrity hashes: the dependencies of the benchmarks, and the browser
  versions of the Node.js modules that webpack uses (`node-libs-browser>buffer` is the `buffer` package as
  `node-libs-browser` resolves it).
- `harness.js`: the benchmark is a webpack bundle, which needs `npm install` and a build. This script replaces it: a
  CommonJS module loader that resolves the modules as webpack does (the `browser` field of `package.json`, the browser
  versions of the Node.js modules, the free variables `process`, `Buffer` and `global`), the virtual file system of the
  benchmark (`third_party`), and the two files that `npm install` builds with `uglifyjs --self`. Each workload runs the
  `fn` of `src/<name>-benchmark.js` once and returns its result as a string (JSON for the objects). jshint and
  source-map are rewritten: their `fn` returns nothing. What the libraries write to the console is part of the result.
- `expected.json`: the length and the SHA-256 (of the UTF-16 code units) of the result of each workload in Node.js.
- `WebToolingBenchmarkTests.cs`: runs each workload in a new engine and compares its result with `expected.json`. When
  they differ, the result of the engine is written to `<temp>/web-tooling-benchmark/<workload>.txt`.

## Regenerate the expected results

`harness.js` runs in a `vm` context of Node.js, which only has the built-ins of ECMAScript, as a realm of the engine:

```
dotnet run eng/javascriptengine/fetch-tests.cs
node tests/Meziantou.Framework.JavaScriptEngine.WebToolingBenchmark/generate.js
```

`generate.js <workload>...` only runs the given workloads, and `--dump <directory>` writes their results to
`<directory>/<workload>.txt`, to compare them with the result of the engine. The current results come from Node.js
24.15.0.

## Duration

The CPU time of each workload on net10.0, in a process of its own (the start of the runtime included), on an Apple M-series
Mac (2026-10-23). The tests run in parallel: `dotnet test -c Release` runs the 18 workloads on both target frameworks in
about 25 seconds.

| Workload | Release | Debug |
|---|---:|---:|
| acorn | 2.4 s | 7.6 s |
| babel | 8.6 s | 31.5 s |
| babel-minify | 2.8 s | 3.8 s |
| babylon | 2.7 s | 6.0 s |
| buble | 2.5 s | 3.5 s |
| chai | 1.5 s | 2.0 s |
| coffeescript | 2.6 s | 3.3 s |
| espree | 3.0 s | 8.5 s |
| esprima | 2.4 s | 6.2 s |
| jshint | 2.9 s | 3.8 s |
| lebab | 2.8 s | 4.8 s |
| postcss | 4.1 s | 10.9 s |
| prepack | 3.6 s | 5.4 s |
| prettier | 3.1 s | 6.8 s |
| source-map | 2.4 s | 5.2 s |
| terser | 1.4 s | 1.9 s |
| typescript | 3.3 s | 5.8 s |
| uglify-js | 2.2 s | 5.0 s |
| Total | 54 s | 122 s |

## Licenses

Nothing is committed: the files are downloaded by the tests. The benchmark is under the BSD-3-Clause license, and its
`third_party` files under their own licenses (MIT for most of them: see the `LICENSE` file of the benchmark). Most
npm packages are under the MIT license; the others are under ISC, BSD-2-Clause, BSD-3-Clause, Apache-2.0
(typescript), CC-BY-4.0 (the data of caniuse-lite) and the JSON license (a file of jshint).
