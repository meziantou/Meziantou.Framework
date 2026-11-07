// Lightning CSS (lightningcss-wasm): the Rust CSS parser, transformer and minifier compiled to WebAssembly (16 MB),
// called through napi-wasm (an implementation of Node-API in JavaScript). It fetches its module, compiles it, and calls
// back into JavaScript for the visitors and the resolver of the bundler.
import init, { transform, transformStyleAttribute, bundleAsync, composeVisitors, Features } from "lightningcss-wasm";

const encoder = new TextEncoder();
const decoder = new TextDecoder();
const css = `
@custom-media --small (width < 640px);
:root { --brand: #3366cc; }
.card {
  color: oklch(60% 0.15 250);
  background: color-mix(in srgb, red 30%, blue);
  inset: 0 auto auto 0;
  transition: transform .2s ease-in-out;
  user-select: none;
  &:hover { color: lab(50% 40 -20); }
  & .title { font: bold 1.5rem/1.2 system-ui, sans-serif; }
  @media (--small) { padding: 4px 8px 4px 8px; }
}
.grid { display: grid; grid-template-areas: "a b" "c d"; margin: 0px 0px 0px 0px; }
.unused-empty { }
@media (min-width: 640px) and (max-width: 1024px) { .grid { gap: calc(2px * 4 + 1rem - 1rem); } }
.button { composes: card; width: clamp(10px, 50%, 200px); }
`;

// Chrome 80 and Safari 13: the targets decide what is lowered
const targets = { chrome: 80 << 16, safari: 13 << 16 };

export default async () => {
  await init();
  const results = [];
  const transformed = transform({ filename: "style.css", code: encoder.encode(css), minify: true, targets, drafts: { customMedia: true }, sourceMap: true });
  results.push(decoder.decode(transformed.code), transformed.warnings.map(warning => warning.message).join(" | "), JSON.parse(decoder.decode(transformed.map)).mappings.length > 0);

  const pretty = transform({ filename: "style.css", code: encoder.encode(css), minify: false, targets: { chrome: 120 << 16 }, drafts: { customMedia: true }, exclude: Features.Nesting });
  results.push(decoder.decode(pretty.code));

  const modules = transform({ filename: "card.module.css", code: encoder.encode(".card { color: red } .button { composes: card; color: blue } #id { color: green }"), cssModules: { pattern: "[local]-[hash]" } });
  results.push(decoder.decode(modules.code), JSON.stringify(Object.fromEntries(Object.entries(modules.exports).sort(([a], [b]) => a < b ? -1 : 1))));

  // The visitors are JavaScript functions that the WebAssembly code calls for each matching node
  const visited = [];
  const withVisitors = transform({
    filename: "visitors.css",
    code: encoder.encode(".a { width: 12px; color: red; margin: 2px } .b { width: 1px }"),
    visitor: composeVisitors([
      { Length: length => length.unit === "px" ? { unit: "rem", value: length.value / 16 } : length },
      { Rule: { style(rule) { visited.push(rule.value.selectors.map(selector => selector.map(component => component.name ?? component.type).join("")).join(",")); return rule; } } },
      { Declaration: { color: declaration => ({ property: "color", value: { type: "rgb", r: 0, g: 128, b: 0, alpha: 1 } }) } },
    ]),
  });
  results.push(decoder.decode(withVisitors.code), visited.join(" "));
  results.push(decoder.decode(transformStyleAttribute({ filename: "style", code: encoder.encode("color: #ff0000; margin: 1px 1px 1px 1px; background: url(a.png)"), minify: true, targets }).code));

  // The bundler resolves and reads the imported files with asynchronous JavaScript callbacks
  const files = {
    "/main.css": "@import 'theme.css' layer(theme); @import 'print.css' print; .main { color: var(--text) }",
    "/theme.css": "@import 'base.css'; :root { --text: #222 }",
    "/base.css": "html { margin: 0 }",
    "/print.css": "body { color: black }",
  };
  const bundled = await bundleAsync({
    filename: "/main.css",
    minify: true,
    resolver: {
      read: async file => { await Promise.resolve(); return files[file]; },
      resolve: async (specifier, from) => "/" + specifier,
    },
  });
  results.push(decoder.decode(bundled.code));

  for (const invalid of [".a { color: ; }", ".a { width: 1px", "@media (min-width: ) { }", ".a:unknown-pseudo { }"]) {
    try {
      const result = transform({ filename: "bad.css", code: encoder.encode(invalid), errorRecovery: true });
      results.push(decoder.decode(result.code) + " | " + result.warnings.map(warning => warning.message + " at " + warning.loc.line + ":" + warning.loc.column).join(" | "));
    }
    catch (error) {
      results.push(error.message);
    }
  }

  try {
    transform({ filename: "bad.css", code: encoder.encode(".a { color: red") , minify: true });
    transform({ filename: "bad.css", code: encoder.encode("@import"), minify: true });
    results.push("no error");
  }
  catch (error) {
    results.push(error.message + " at " + error.loc?.line + ":" + error.loc?.column);
  }

  return results.join("\n----\n");
};
