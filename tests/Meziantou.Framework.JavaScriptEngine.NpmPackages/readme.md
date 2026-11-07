# npm packages

Runs popular npm packages in the engine, in a page of Meziantou.Framework.WebPlatform, and compares their results with
the results of Chromium. The packages are loaded as a browser loads them without a bundler: ES modules, with an import
map built from the installed packages. The page has the platform of a browser (the DOM, `URL`, `TextEncoder`,
`fetch`, `structuredClone`, `AbortController`, the timers, `WebAssembly` with Wasmtime...), so the packages run as they
were published, without polyfills.

The [Web Tooling Benchmark](../Meziantou.Framework.JavaScriptEngine.WebToolingBenchmark/readme.md) covers the tools of
2017 (mostly ES5 code in CommonJS modules); these workloads use the current versions of about 60 packages, written with
modern syntax (classes, private fields, async functions and generators, optional chaining) and published as ES modules:
the module graphs (hundreds of modules for date-fns, d3, lodash-es or micromark), the import maps, and the parts of the
engine that real code relies on (regular expressions, `Intl`, `BigInt`, typed arrays, `Proxy`, `eval` and `Function`,
promises and timers) are tested together, with the bindings of WebPlatform.

The workloads are grouped by kind of package:

- CSS and markup: lightningcss (lightningcss-wasm: the Rust library compiled to WebAssembly), sass, tailwindcss,
  css-tree, csso, svgo, marked, markdown-it, micromark and the mdast and hast utilities, katex, highlight.js, prismjs,
  shiki, prettier.
- Data formats and validation: js-yaml, yaml, smol-toml, json5, fast-xml-parser, zod, valibot, graphql,
  @msgpack/msgpack, libphonenumber-js.
- Dates, `Intl` and numbers: luxon, date-fns, temporal-polyfill, intl-messageformat, i18next, chrono-node, decimal.js,
  culori, d3, three.
- Frameworks, compilers and templates: preact (preact-render-to-string), vue (@vue/compiler-sfc, and an application
  mounted in the document), acorn with acorn-walk and astring, liquidjs, mustache, htmlparser2, parse5, diff.
- Libraries: lodash-es, immer, immutable, mobx, xstate, pako, fflate, @noble/hashes, @noble/curves, fuse.js,
  compromise.

The packages that a browser cannot load without a bundler are not tested: the CommonJS modules (React, TypeScript,
ESLint, terser, ajv, handlebars...), the ES modules that import CommonJS modules (unified, Svelte, less, sql-formatter,
peggy...) or that omit the extensions of their imports (rxjs, pdf-lib, cheerio, Day.js, Moment.js, validator). Some
packages have a browser build in addition to their default ES module, which reads `process.env.NODE_ENV` (a bundler
replaces it): the workloads of immer, mobx and vue import that build by its URL, as a page would from a CDN.

## Files

- `package.json` and `package-lock.json`: the versions of the packages. The packages are not committed:
  `dotnet run eng/javascriptengine/fetch-tests.cs` downloads them in
  `tests/Meziantou.Framework.JavaScriptEngine.External/npm-packages/node_modules`, from the tarballs and with the
  integrity hashes of the lockfile (`npm ci --ignore-scripts --omit=optional` without npm), and writes the import map of
  the installed packages in `npm-packages/importmap.json`: the `exports` of each package with the `browser`, `import`,
  `module`, `production` and `default` conditions (else its `module`, `browser` or `main` field), its `imports` (`#name`
  specifiers), the object form of its `browser` field, and the scopes of the packages installed in the `node_modules`
  of another package. The optional dependencies are native binaries (the file watcher of sass), which the workloads do
  not use.
- `workloads/<name>.mjs`: an ES module whose default export returns the result of the workload, a string (or a promise
  of a string). The inputs are part of the workload; the result must not depend on the current time, the speed of the
  engine or `Math.random()`.
- `page.html` and `runner.mjs`: the page of the site `https://npm.test` (with the import map), and the module that runs
  the workload named by the `workload` parameter of its URL and writes the result in its `#result` element. What the
  packages write to the console, and the exceptions of the callbacks, are part of the result.
- `NpmPackagesTests.cs`: runs each workload in a new `BrowsingContext` of WebPlatform, with a new engine (in the UTC
  time zone, with the en-US locale), Wasmtime, and a document loader that serves the files of the site. The timers run
  in virtual time: when the event loop is idle, the clock advances to the next timer. When the result differs from
  `expected/<name>.txt`, it is written to `<temp>/npm-packages/<name>.txt`, and the failure lists the missing files and
  the errors of the page.
- `generate.js`: writes `expected/<name>.txt` with Chromium (Playwright, in `generator/`), which serves the same files
  with the same site. Chromium runs in real time (the fake clock of Playwright replaces `Date` by a function that cannot
  be subclassed), so the timers of the workloads must be far enough apart. When a workload fails, it prints the missing
  files and the errors of the page.

## Regenerate the expected results

```
dotnet run eng/javascriptengine/fetch-tests.cs npm-packages
npm ci --prefix tests/Meziantou.Framework.JavaScriptEngine.NpmPackages/generator
npx --prefix tests/Meziantou.Framework.JavaScriptEngine.NpmPackages/generator playwright install chromium
node tests/Meziantou.Framework.JavaScriptEngine.NpmPackages/generate.js
```

`generate.js <workload>...` only runs the given workloads, and `--check` compares the results with the files instead of
writing them. The current results come from Chromium 156 (Playwright 1.64.1).

## Update the packages

Change the versions in `package.json`, then update the lockfile and the expected results:

```
npm install --package-lock-only --ignore-scripts --prefix tests/Meziantou.Framework.JavaScriptEngine.NpmPackages
dotnet run eng/javascriptengine/fetch-tests.cs npm-packages
node tests/Meziantou.Framework.JavaScriptEngine.NpmPackages/generate.js
```

Renovate does not update `package.json` and `generator/package.json`: a new version of a package or of Chromium changes
the expected results, which only Chromium can regenerate. Check the licenses of the new dependencies (`license` in
`package-lock.json`).

## Differences with Chromium

The results of the engine and of Chromium are the same, except where the specifications let them differ. The workloads
avoid these cases:

- The data of `Intl`: the engine embeds its own copy of CLDR, whose version can differ from the one of the ICU of
  Chromium. Prefer the locales and the time zones whose data is stable; a difference that appears when one of the
  versions changes is not a bug of the engine.
- The implementation-defined results: the name of the time zone of `Date.prototype.toString()`, and the order of
  `Array.prototype.sort` with an inconsistent comparator (the `topics()` of compromise).
- Timing: shiki stops tokenizing a line after 500 ms (`tokenizeTimeLimit`), which the workload disables.

## Duration

On an Apple M-series Mac (2026-10-24), the 53 workloads run in about 55 seconds per target framework in Debug: the
tests run in parallel, and the slowest ones set the duration. The slowest are shiki, lightningcss (the compilation of
its WebAssembly module), @noble/hashes, compromise, @noble/curves and sass (15 to 50 seconds each in Debug). The engine
is about a hundred times slower than a JIT on the loops of the hash functions and the compressors: keep their inputs
small.

## Licenses

Nothing is committed: the packages are downloaded by the tests. Most of them are under the MIT license; the others are
under ISC, BSD-2-Clause, BSD-3-Clause, Apache-2.0, MPL-2.0 (lightningcss-wasm), BlueOak-1.0.0, Unlicense, CC0-1.0
(mdn-data), MIT AND Zlib (pako) and PSF-2.0 (argparse).
