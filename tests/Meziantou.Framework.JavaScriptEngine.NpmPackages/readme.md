# npm packages

Runs popular npm packages in the engine and compares their results with the results of Node.js. The
[Web Tooling Benchmark](../Meziantou.Framework.JavaScriptEngine.WebToolingBenchmark/readme.md) covers the tools of 2017
(mostly ES5 code in CommonJS modules); these workloads use the current versions of about 80 packages, most of them
written with modern syntax (classes, private fields, async functions and generators, optional chaining) and published as
ES modules: the module graphs (hundreds of modules for date-fns, d3 or unified), the interoperability of the ES modules
and CommonJS modules, and the parts of the engine that real code relies on (regular expressions, `Intl`, `BigInt`, typed
arrays, `Proxy`, `eval` and `Function`, promises and timers) are tested together.

The workloads are grouped by kind of package:

- CSS and markup: sass, less, csso, css-tree, tailwindcss, svgo, marked, markdown-it, unified (remark and rehype),
  katex, highlight.js, prismjs, shiki, js-beautify, prettier, sql-formatter.
- Data formats and validation: js-yaml, yaml, smol-toml, json5, fast-xml-parser, papaparse, ajv, zod, valibot,
  graphql, protobufjs, @msgpack/msgpack, qs, semver, validator, libphonenumber-js, ua-parser-js, whatwg-url.
- Dates, `Intl` and numbers: luxon, date-fns, dayjs, moment and moment-timezone, temporal-polyfill,
  intl-messageformat, i18next, chrono-node, decimal.js, mathjs, culori, d3, three.
- Frameworks, compilers and templates: react (react-dom/server), preact (preact-render-to-string), svelte, vue
  (@vue/compiler-sfc and vue/server-renderer), typescript, eslint, terser, sucrase, peggy, handlebars, mustache,
  liquidjs, htmlparser2, parse5, cheerio, linkedom, diff.
- Libraries: lodash, immer, immutable, rxjs, mobx, @reduxjs/toolkit, xstate, pako, fflate, crypto-js, @noble/hashes,
  @noble/curves, pdf-lib, fuse.js, lunr, compromise.

lightningcss is not tested: the `lightningcss` package is a native Node.js addon, and `lightningcss-wasm` needs
WebAssembly, which only Meziantou.Framework.WebPlatform provides (with Meziantou.Framework.WebPlatform.Wasmtime).

## Files

- `package.json` and `package-lock.json`: the versions of the packages. The packages are not committed:
  `dotnet run eng/javascriptengine/fetch-tests.cs` downloads them in
  `tests/Meziantou.Framework.JavaScriptEngine.External/npm-packages/node_modules`, from the tarballs and with the
  integrity hashes of the lockfile (`npm ci --ignore-scripts --omit=optional` without npm). The optional dependencies
  are native binaries (the file watcher of sass), which the workloads do not use.
- `workloads/<name>.mjs`: an ES module whose default export returns the result of the workload, a string (or a promise
  of a string). The inputs are part of the workload; the result must not depend on the time, the speed of the engine or
  `Math.random()`.
- `harness.js`: the runtime of the packages, which runs the same way in Node.js and in the engine. It resolves the
  modules as Node.js does (the `exports` and `imports` of the packages with the `import` or `require` and `default`
  conditions, `main`, the extensions, the `type` field and the detection of the syntax of the ES modules), loads the
  CommonJS modules (with `require()` of the ES modules), and defines the globals that the packages expect from a
  runtime that is neither Node.js nor a browser: `process` (`process.browser` is true), `console` (what the packages
  write is part of the result), timers in virtual time, `TextEncoder` and `TextDecoder`, `atob` and `btoa`,
  `structuredClone`, `EventTarget` and `AbortController`, `MessageChannel`, and `URL` and `URLSearchParams` (the
  `whatwg-url` package). The Node.js modules are not available, except `module` (`createRequire`), `util` (`inspect`)
  and `path` (the `path-browserify` package, as the bundlers do).
- `NpmPackagesTests.cs`: runs each workload in a new engine (in the UTC time zone, with the en-US locale) and compares
  its result with `expected/<name>.txt`. The module loader of the test creates the modules that the harness resolves.
  When the results differ, the result of the engine is written to `<temp>/npm-packages/<name>.txt`.
- `generate.js`: writes `expected/<name>.txt` with Node.js. The harness runs in a `vm` context, which only has the
  built-ins of ECMAScript, as a realm of the engine.

## Regenerate the expected results

```
dotnet run eng/javascriptengine/fetch-tests.cs npm-packages
node tests/Meziantou.Framework.JavaScriptEngine.NpmPackages/generate.js
```

`generate.js <workload>...` only runs the given workloads, and `--check` compares the results with the files instead of
writing them. The current results come from Node.js 24.15.0 (ICU 78.2, CLDR 48). generate.js runs itself again with
`--experimental-vm-modules`, `TZ=UTC` and the en-US locale.

## Update the packages

Change the versions in `package.json`, then update the lockfile and the expected results:

```
npm install --package-lock-only --ignore-scripts --prefix tests/Meziantou.Framework.JavaScriptEngine.NpmPackages
dotnet run eng/javascriptengine/fetch-tests.cs npm-packages
node tests/Meziantou.Framework.JavaScriptEngine.NpmPackages/generate.js
```

Renovate does not update `package.json`: a new version changes the expected results, which only Node.js can regenerate.
Check the licenses of the new dependencies (`license` in `package-lock.json`): ua-parser-js stays on version 1, as
version 2 is under the AGPL.

## Differences with Node.js

The results of the engine and of Node.js are the same, except where the specification lets them differ. The workloads
avoid these cases:

- The data of `Intl`: the engine embeds its own copy of CLDR, whose version can differ from the one of the ICU of
  Node.js (CLDR 49 and 48 at the time of writing). The formats of the dates in Hindi, the abbreviated day periods in
  Korean and the names of `Europe/London` and `Europe/Dublin` (the British and Irish metazones of CLDR 49) differ
  between these versions. Prefer the locales and the time zones whose data is stable; a difference that appears when
  one of the versions changes is not a bug of the engine.
- The engine writes the narrow no-break spaces (U+202F) of the CLDR patterns (`h:mm\u202Fa`) as spaces, as the
  browsers do: the harness replaces them in the results.
- The implementation-defined results: the name of the time zone of `Date.prototype.toString()`, and the order of
  `Array.prototype.sort` with an inconsistent comparator (the `topics()` of compromise).
- Timing: shiki stops tokenizing a line after 500 ms (`tokenizeTimeLimit`), which the workload disables.
- The engine implements the setter of `Error.prototype.stack` of the
  [Error Stack Accessor](https://tc39.es/proposal-error-stack-accessor/) proposal, which throws a TypeError for a value
  that is not a string. `LessError` of less copies the `stack` of the errors of its parser, which is undefined: the
  less workload replaces the setter with one that accepts any value, as V8, where `stack` is a property of the errors.
- The browser build of Dart, which sass is compiled with, computes `Uri.base` (part of the messages of sass) from
  `self.location`: the sass workload defines it.

## Duration

On an Apple M-series Mac (2026-10-23), the 80 workloads run in about 36 seconds per target framework, in Release and in
Debug: the tests run in parallel, and the slowest ones set the duration. The slowest are shiki, mathjs, typescript,
@noble/hashes, sass and compromise (10 to 30 seconds each in Debug). The engine is about a hundred times slower than
Node.js on the loops of the hash functions and the compressors: keep their inputs small.

## Licenses

Nothing is committed: the packages are downloaded by the tests. Most of them are under the MIT license; the others are
under ISC, BSD-2-Clause, BSD-3-Clause, Apache-2.0, 0BSD, BlueOak-1.0.0, Unlicense, CC0-1.0 (mdn-data), MIT AND Zlib
(pako) and Python-2.0 or PSF-2.0 (argparse).
