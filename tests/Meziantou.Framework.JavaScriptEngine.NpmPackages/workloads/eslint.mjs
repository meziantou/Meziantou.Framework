// ESLint 10 (the Linter of eslint/universal): parsing with espree, scope analysis with eslint-scope, the core rules,
// autofixing, inline configuration comments and custom rules
import { Linter } from "eslint/universal";

const code = `/* eslint no-console: "warn" */
var unused = 1;
let reassigned = 2;
const a = 1; a = 2;
function f(x, x2) { if (x == null) { return } else { return x2 } }
console.log("hello" + 'world');
for (var i = 0; i < 10; i++) { setTimeout(function () { console.log(i); }); }
const obj = { a: 1, a: 2 };
if (typeof x === "strnig") {}
async function g() { await Promise.all([1].map(async v => await v)); }
while (true) { break; }
const re = /a  b/;
label: for (;;) { continue label; }
debugger;
export default class { constructor() { super.x; } }
`;

const customRule = {
  meta: { type: "suggestion", fixable: "code", messages: { noFoo: "Do not use '{{ name }}'." }, schema: [] },
  create(context) {
    return {
      Identifier(node) {
        if (node.name.startsWith("unused"))
          context.report({ node, messageId: "noFoo", data: { name: node.name }, fix: fixer => fixer.replaceText(node, "used" + node.name.slice(6)) });
      },
    };
  },
};

export default () => {
  const linter = new Linter({ configType: "flat" });
  const config = [{
    languageOptions: { ecmaVersion: "latest", sourceType: "module", globals: { console: "readonly", setTimeout: "readonly", Promise: "readonly" } },
    plugins: { local: { rules: { "no-foo": customRule } } },
    rules: {
      "no-unused-vars": "error", "prefer-const": "error", "no-const-assign": "error", "no-dupe-keys": "error", "valid-typeof": "error", "eqeqeq": ["error", "always"],
      "no-else-return": "error", "no-var": "error", "quotes": ["error", "double"], "no-loop-func": "error", "no-constant-condition": "error", "no-regex-spaces": "error",
      "no-debugger": "error", "no-labels": "error", "no-useless-return": "error", "prefer-template": "error", "no-return-await": "error", "semi": "error", "constructor-super": "error",
      "local/no-foo": "error",
    },
  }];
  const messages = linter.verify(code, config, { filename: "file.js" });
  const fixed = linter.verifyAndFix(code, config, { filename: "file.js" });
  return [
    messages.map(message => `${message.line}:${message.column} ${message.severity} ${message.ruleId} ${message.message}${message.fix ? " [fix]" : ""}${message.suggestions ? " [" + message.suggestions.length + " suggestions]" : ""}`).join("\n"),
    fixed.output,
    String(fixed.fixed) + " " + fixed.messages.length,
    JSON.stringify(linter.verify("let = ;", config, "x.js")),
  ].join("\n----\n");
};
