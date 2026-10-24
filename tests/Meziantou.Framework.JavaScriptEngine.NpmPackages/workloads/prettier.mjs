// Prettier 3 (standalone): formatting of JavaScript, TypeScript, CSS, Markdown, YAML, HTML and GraphQL with its parsers
import * as prettier from "prettier/standalone";
import * as babel from "prettier/plugins/babel";
import * as estree from "prettier/plugins/estree";
import * as typescript from "prettier/plugins/typescript";
import * as postcss from "prettier/plugins/postcss";
import * as markdown from "prettier/plugins/markdown";
import * as yaml from "prettier/plugins/yaml";
import * as html from "prettier/plugins/html";
import * as graphql from "prettier/plugins/graphql";

const plugins = [babel, estree, typescript, postcss, markdown, yaml, html, graphql];

const inputs = [
  ["babel", "const {a,b:[c,...d]}=obj;async function f(x=1,...y){return await Promise.all([x,...y].map(async v=>v?.w??0))}label:for(const k in o)if(k)continue label;else break\nexport default class extends Base{static #p=1;get x(){return this.#p}}"],
  ["typescript", "type A<T extends object={}>={readonly [K in keyof T]?:T[K] extends Function?never:T[K]};enum E{A=1,B};function f<T,>(this:Window,a:T):asserts a is NonNullable<T>{}abstract class C implements I{private constructor(public x:number){super()}}"],
  ["css", ".a{color:red;background:URL(x.png)}@media (max-width:100px){.b>.c,.d~.e{margin:0 auto!important}}@supports (display:grid){:root{--x:calc( 1px+2px )}}"],
  ["markdown", "# Title\n* item one\n* item two\n\n1) first\n1) second\n\n|a|b|\n|-|-|\n|1|2|\n\n```js\nconst   a=1\n```\n\nA very long paragraph that should stay on its own line because markdown prose wrapping is preserved by default."],
  ["yaml", "key:   value\nlist:\n- a\n-   b: c\n    d: e\nanchors: &x {a: 1, b: 2}\nref: *x\nmulti: |\n   text\n"],
  ["html", "<!DOCTYPE html><html><head><title>T</title></head><body><div class=\"a\" id=b><p>Some <b>bold</b> text</p><ul><li>1</li><li>2</li></ul><button onclick=\"go( 1 )\" disabled>Go</button></div></body></html>"],
  ["graphql", "query Q($id:ID!,$n:Int=10){user(id:$id){...F friends(first:$n)@include(if:true){edges{node{name}}}}}fragment F on User{id name}"],
];

export default async () => {
  const results = [];
  for (const [parser, code] of inputs) {
    results.push(await prettier.format(code, { parser, plugins }));
  }

  results.push(await prettier.format(inputs[0][1], { parser: "babel", plugins, semi: false, singleQuote: true, trailingComma: "none", printWidth: 40, tabWidth: 4, arrowParens: "avoid" }));
  try {
    await prettier.format("const = ;", { parser: "babel", plugins });
  }
  catch (error) {
    results.push(error.name + ": " + error.message.split("\n")[0]);
  }

  return results.join("\n----\n");
};
