// Terser 5: minification of modern JavaScript (classes, private fields, async generators, optional chaining, modules)
// with compression, mangling, property mangling, source maps and errors
import { minify } from "terser";

const code = `
export class Queue {
    #items = [];
    static #instances = 0;
    constructor(name = "queue") { this.name = name; Queue.#instances++; }
    push(...values) { this.#items.push(...values); return this; }
    get size() { return this.#items.length; }
    *[Symbol.iterator]() { yield* this.#items; }
    async *drain(delay) { while (this.#items.length) { await delay(); yield this.#items.shift(); } }
    static get count() { return Queue.#instances; }
}

function unusedHelper() { return "never called"; }
const DEBUG = false;
export function process(input) {
    if (DEBUG) console.log("debug", input);
    const { a = 1, b: { c } = {}, ...rest } = input ?? {};
    const result = [a, c ?? "none", Object.keys(rest).length].map(x => typeof x === "number" ? x * 2 : x);
    label: for (let i = 0; i < 3; i++) { if (i === 1) continue label; result.push(i ** 2); }
    return result.length > 3 ? result : [...result, \`count: \${result.length}\`];
}
export const config = { longPropertyName: 1, _privateProperty: 2, "quoted-key": 3 };
`;

export default async () => {
  const results = [];
  const basic = await minify(code, { module: true, sourceMap: { filename: "out.js", url: "out.js.map" } });
  results.push(basic.code, basic.map);
  results.push((await minify(code, { module: true, compress: { passes: 2, unsafe: true, pure_getters: true }, mangle: { properties: { regex: /^_/ } }, format: { comments: false, ecma: 2020 } })).code);
  results.push((await minify(code, { module: true, compress: false, mangle: false, format: { beautify: true, indent_level: 2 } })).code);
  results.push((await minify("var a = function () { return 1 + 2 * 3 }; console.log(a(), void 0 === undefined);", { toplevel: true, ecma: 5 })).code);
  try {
    await minify("function ( { }");
  }
  catch (error) {
    results.push(error.name + ": " + error.message + " " + error.line + ":" + error.col);
  }

  return results.join("\n----\n");
};
