# Performance

`benchmarks/Meziantou.Framework.WebPlatform.Benchmarks` measures the library with [BenchmarkDotNet](https://benchmarkdotnet.org/).
The documents are a small page, the [HTML article of Wikipedia](https://en.wikipedia.org/wiki/HTML) and the [HTML
Standard](https://html.spec.whatwg.org/) (13 MB, 724,000 nodes), and the style sheets of the Wikipedia article (230 KB)
and of [Bootstrap](https://getbootstrap.com/) 5.3 (280 KB), downloaded once in `artifacts/benchmark-data`.

```bash
dotnet run --project benchmarks/Meziantou.Framework.WebPlatform.Benchmarks -c Release --framework net10.0 -- --filter '*'
dotnet run --project benchmarks/Meziantou.Framework.WebPlatform.Benchmarks -c Release --framework net10.0 -- memory
```

## Results

.NET 10, Apple M-series, `ShortRun` job (2026-10-04). The AngleSharp columns were measured with AngleSharp 1.8.3 and
AngleSharp.Css 1.1.2, when the benchmarks still compared the library with it; the ratio is the time of AngleSharp divided
by the time of the library.

| Benchmark | Library | AngleSharp | Ratio | Allocated (library / AngleSharp) |
|---|---:|---:|---:|---:|
| Parse small page | 8.4 µs | 19.7 µs | 2.3× | 18 KB / 52 KB |
| Parse Wikipedia | 4.7 ms | 8.8 ms | 1.9× | 4.9 MB / 9.4 MB |
| Parse HTML Standard | 221 ms | 488 ms | 2.2× | 144 MB / 263 MB |
| `QuerySelectorAll("a")` | 64 µs | 120 µs | 1.9× | 64 KB / 65 KB |
| `QuerySelectorAll("#content")` | 48 ns | 114 µs | 2,400× | 176 B / 352 B |
| `QuerySelectorAll("div.mw-parser-output p a[href^='/wiki/']")` | 87 µs | 128 µs | 1.5× | 32 B / 296 B |
| `QuerySelectorAll("li:nth-child(2n+1) > a")` | 101 µs | 200 µs | 2.0× | 16 KB / 16 KB |
| `QuerySelectorAll("div:has(> table)")` | 62 µs | 2.7 ms | 44× | 176 B / 100 KB |
| `OuterHtml` Wikipedia | 1.5 ms | 3.1 ms | 2.0× | 3.5 MB / 7.6 MB |
| `OuterHtml` HTML Standard | 47 ms | 179 ms | 3.8× | 61 MB / 148 MB |
| `TextContent` Wikipedia | 186 µs | 250 µs | 1.3× | 453 KB / 453 KB |
| `TextContent` HTML Standard | 8.1 ms | 11.8 ms | 1.5× | 18 MB / 18 MB |
| Build and update a tree with the DOM API | 178 µs | 562 µs | 3.2× | 663 KB / 1,023 KB |
| Parse the Wikipedia style sheets | 6.0 ms | 7.0 ms | 1.2× | 10.1 MB / 9.6 MB |
| Parse Bootstrap | 10.2 ms | 10.3 ms | 1.0× | 13.3 MB / 15.7 MB |
| Computed style of all the elements of Wikipedia (7,864) | 106 ms | 17.1 s | 160× | 50 MB / 24.9 GB |
| Computed style of all the elements of the small page with Bootstrap | 7.5 ms | 44 ms | 5.9× | 15 MB / 50 MB |

The computed style benchmarks (`StyleBenchmarks`) load a new document in each iteration (not measured), with the style
sheets in a `style` element, then read `display`, `color`, `font-size` and `margin-top` of the computed style of each
element. AngleSharp.Css 1.1.2 has no cache (each call cascades all the rules for the element and its ancestors), and it
threw a `NullReferenceException` for 123 elements of the Wikipedia article (some `box-shadow` values), which were ignored.

The layout benchmark (`LayoutBenchmarks`) loads the Wikipedia article with its style sheets and computes the styles of its
elements in each iteration (not measured), then measures its first layout (the box tree, the text shaping with the
OpenType lookups of the fonts, the line breaking): about 280 ms and 200 MB allocated. The text benchmarks
(`TextMeasurementBenchmarks`) are described in [layout.md](layout.md#text-shaping).

Memory of the parsed documents (`memory`): the bytes retained by a document and the bytes allocated by the parser,
per node (elements, text, comments, doctype), with the AngleSharp values measured on the same date:

| Document | Nodes | Retained bytes/node (library / AngleSharp) | Allocated bytes/node (library / AngleSharp) |
|---|---:|---:|---:|
| Small page | 65 | 364 / 1,536 | 297 / 1,269 |
| Wikipedia | 15,468 | 558 / 1,045 | 333 / 760 |
| HTML Standard | 724,173 | 251 / 435 | 208 / 386 |

## Invalidation

`InvalidationBenchmarks` changes a laid out document of about 5,000 elements (500 cards and a table of 100 rows, with class,
descendant, child, sibling and structural selectors) and updates its layout: `ClassChange` toggles a class of one card,
`AppendTableRow` appends a row to the table (and removes it), `OffsetTopAfterEachWrite` changes the width of 10 cards and
reads their `offsetTop` after each write (10 forced layouts), `AnimationFrame` runs one frame of a document with one running
CSS animation. `invalidation <scenario> <count>` runs a scenario in a loop, to profile it:

```bash
dotnet run --project benchmarks/Meziantou.Framework.WebPlatform.Benchmarks -c Release --framework net10.0 -- --filter '*InvalidationBenchmarks*'
dotnet-trace collect --format Speedscope -- dotnet artifacts/bin/Meziantou.Framework.WebPlatform.Benchmarks/release_net10.0/Meziantou.Framework.WebPlatform.Benchmarks.dll invalidation class 30
```

Before the invalidation of the styles by element ([computed-style.md](computed-style.md#invalidation)), each change computed
the styles of all the elements again: the computed values were about half of the time of each scenario (48 to 52% of the
samples), the cascade 5 to 6%, the box tree 5 to 6% and the layout 38 to 40% (the allocations and the collections of the
discarded styles are counted where they happen). The styles of the elements that the change does not affect are now kept:

| Scenario | Allocated before | Allocated after | Process CPU time before | after |
|---|---:|---:|---:|---:|
| `ClassChange` | 189.2 MB | 43.4 MB | 390–450 ms | 100–107 ms |
| `AppendTableRow` | 189.3 MB | 43.8 MB | 390–440 ms | 100–110 ms |
| `OffsetTopAfterEachWrite` (10 layouts) | 1,892 MB | 434 MB | 3.7–4.1 s | 0.82–0.91 s |
| `AnimationFrame` | 188.9 MB | 43.4 MB | 380–385 ms | 95–110 ms |

(.NET 10, Apple M-series, 2026-10-14, a machine under heavy load: the process CPU time of `invalidation <scenario>` with
`DOTNET_gcConcurrent=0`, the two variants run alternately from the same directory.) The box tree and the layout were then
the main costs (about three quarters of the samples). The styles that the box tree derives from the computed styles (the
anonymous boxes, the symbols of the 1,500 markers...) are now kept with their styles instead of being created by each build
(their values computed again by the layout), and the box tree is patched with the boxes of the elements that changed instead
of being built again (its counters are kept), see [layout.md](layout.md#incremental-update):

| Scenario | Allocated: styles by element | derived styles kept | box tree patched | Process CPU time: styles by element | derived styles kept | box tree patched |
|---|---:|---:|---:|---:|---:|---:|
| `ClassChange` | 43.5 MB | 22.0 MB | 11.9 MB | 86–99 ms | 60–63 ms | 41–43 ms |
| `AppendTableRow` | 43.8 MB | 22.4 MB | 12.2 MB | 85–92 ms | 64–75 ms | 45–53 ms |
| `OffsetTopAfterEachWrite` (10 layouts) | 434 MB | 219 MB | 120 MB | 574–615 ms | 424–457 ms | 226–271 ms |
| `AnimationFrame` | 43.5 MB | 22.1 MB | 12.0 MB | 87–93 ms | 60–64 ms | 36–41 ms |

(Same machine and method, 2026-10-14, three rounds.) The layout of the whole document was then about 70% of each change. The
layout result cache now keeps the fragments of the boxes that did not change, and lays out again only the boxes that contain
the changed boxes (see [layout.md](layout.md#layout-result-cache)); the kept boxes of a rebuilt subtree of the box tree keep
their results (e.g. the rows of a table whose row group is built again):

| Scenario | Allocated: box tree patched | layout result cache | Process CPU time: box tree patched | layout result cache |
|---|---:|---:|---:|---:|
| `ClassChange` | 11.9 MB | 0.6 MB | 40–49 ms | 3.3–11 ms |
| `AppendTableRow` | 12.2 MB | 0.4 MB | 39–51 ms | 13–23 ms |
| `OffsetTopAfterEachWrite` (10 layouts) | 120 MB | 5.9 MB | 256–286 ms | 47–69 ms |
| `AnimationFrame` | 12.0 MB | 0.6 MB | 41–48 ms | 3–10 ms |

(2026-10-16, three rounds, a machine under heavy load: the third round ran with twice the load of the first.) Before the
invalidation of the styles by element, each of these changes took 380–450 ms of CPU and allocated 189 MB. The remaining costs
of a change are the indexing of the fragment tree after each pass (`LayoutEngine.IndexFragments`, a walk of all the
fragments), the layout of the ancestors of the changed boxes, the recomputed styles and the patch of the box tree.

## Regressions

The `Benchmarks` workflow runs the benchmarks of the library for the base and the head of each pull request that changes
the code, on the same machine, and fails when a benchmark is more than 30% slower or allocates more than 5% more (the
allocations are deterministic, the time is noisy on shared runners). `compare` prints the comparison of two
BenchmarkDotNet JSON reports:

```bash
dotnet run --project benchmarks/Meziantou.Framework.WebPlatform.Benchmarks -c Release --framework net10.0 -- --filter '*ParseBenchmarks*' --exporters json --artifacts artifacts/base
dotnet run --project benchmarks/Meziantou.Framework.WebPlatform.Benchmarks -c Release --framework net10.0 -- compare artifacts/base artifacts/head
```

## Pathological input

Deep and wide documents do not have quadratic costs: getting the root of a connected node is O(1), appending options to a
select does not run the selectedness algorithm for each option, and the pretty formatters limit the indentation. The
CSS parser limits the nesting of blocks to 256 levels, so deeply nested style sheets and selectors cannot exhaust the
stack. See [fuzzing.md](fuzzing.md).

The box-tree generation, layout, fragment-overflow computation, paint-order traversal and SVG element painting recurse on
the stack. When the stack is nearly exhausted (`RuntimeHelpers.TryEnsureSufficientExecutionStack()`), these paths
continue on a new thread with a 16 MB stack while the caller waits (`StackGuard`), so deeply nested documents do not
exhaust the caller's stack. The guards do not impose a fixed nesting-depth limit. Very deep documents still require memory
and processing time proportional to their size, and remain subject to other resource limits.
