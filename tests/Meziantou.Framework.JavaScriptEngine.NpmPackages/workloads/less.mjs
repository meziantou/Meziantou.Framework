// The core of Less (lib/less), without the Node.js file system: variables, mixins, guards, operations, functions,
// extend, detached rulesets, loops and errors

// The engine implements the setter of Error.prototype.stack of the Error Stack Accessor proposal
// (https://tc39.es/proposal-error-stack-accessor/), which throws a TypeError when the value is not a string. LessError
// copies the stack of the object it wraps (this.stack = e.stack), which is undefined for the errors of the parser: Less
// creates them while it parses a detached ruleset, and for each error it reports. V8 has no such setter: the assignment
// creates a property of the error, as here.
const stack = Object.getOwnPropertyDescriptor(Error.prototype, "stack");
if (stack?.set !== undefined) {
  Object.defineProperty(Error.prototype, "stack", {
    configurable: true,
    get: stack.get,
    set(value) {
      Object.defineProperty(this, "stack", { value, writable: true, configurable: true });
    },
  });
}

const { default: createLess } = await import("less/lib/less/index.js");

const less = createLess({
  encodeBase64: text => btoa(text),
  mimeLookup: () => "application/octet-stream",
  charsetLookup: () => undefined,
  getSourceMapGenerator: () => undefined,
}, []);
less.PluginLoader = class extends less.AbstractPluginLoader { };

const source = `
@primary: #3366cc;
@spacing: 8px;
@sizes: small 10px, medium 16px, large 24px;

.bordered(@width: 1px; @style: solid) { border: @width @style darken(@primary, 10%); }
.mixin(@a) when (lightness(@a) >= 50%) { background-color: black; }
.mixin(@a) when (lightness(@a) < 50%) { background-color: white; }
.loop(@i) when (@i > 0) {
    .w-@{i} { width: (@i * 10%); }
    .loop(@i - 1);
}

@detached: { background: red; };

.box {
    .bordered(2px; dashed);
    .mixin(#ddd);
    padding: @spacing (@spacing * 2);
    margin: -@spacing;
    color: fade(@primary, 50%);
    width: ~"calc(100% - @{spacing})";
    &:extend(.base all);
    &-title { font-size: percentage(0.5); }
    .inner & { color: spin(@primary, 30); }
    @media (min-width: 768px) { padding: (@spacing * 3); }
    @detached();
}

.base { display: block; }
.loop(3);

each(@sizes, {
    .text-@{value} { font-size: extract(@value, 2); }
});

@plugin-free: e("raw");
.functions {
    a: round(3.14159, 2);
    b: mod(11, 3);
    c: unit(5, px);
    d: rgb(10, 20, 30);
    e: mix(#ff0000, #0000ff, 50%);
    f: replace("Hello", "e", "a");
    g: length(1px 2px 3px);
    h: if((iscolor(#fff)), yes, no);
}
`;

export default async () => {
  const results = [];
  results.push((await less.render(source)).css);
  results.push((await less.render(source, { compress: true })).css);
  for (const invalid of [".a { color: @missing; }", ".a { .undefined-mixin(); }", ".a { color: red"]) {
    try {
      await less.render(invalid);
      results.push("no error");
    }
    catch (error) {
      results.push(error.type + ": " + error.message + " (line " + error.line + ")");
    }
  }

  return results.join("\n----\n");
};
