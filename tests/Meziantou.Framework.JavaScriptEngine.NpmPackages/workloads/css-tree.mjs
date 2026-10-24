// CSSTree: parsing to an AST, walking, the lexer (validation of values against the CSS grammar) and generation
import * as csstree from "css-tree";

const css = `
@import url("theme.css") screen;
:root { --gap: 4px; }
.grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(120px, 1fr)); gap: var(--gap); }
a[href^="https"]:not(.internal)::after { content: "↗"; color: hsl(210 50% 40% / .8); }
@media (min-width: 640px) and (prefers-color-scheme: dark) { .grid { gap: calc(var(--gap) * 2); } }
@supports (display: grid) { .x { width: clamp(1rem, 2.5vw, 2rem); } }
.bad { color: 12px; width: red; }
`;

export default () => {
  const ast = csstree.parse(css, { positions: true });
  const selectors = [];
  const declarations = [];
  csstree.walk(ast, {
    visit: "Rule",
    enter(node) {
      selectors.push(csstree.generate(node.prelude));
    },
  });
  csstree.walk(ast, {
    visit: "Declaration",
    enter(node) {
      const match = csstree.lexer.matchDeclaration(node);
      declarations.push(node.property + ": " + csstree.generate(node.value) + (match.error ? " -> " + match.error.message.split("\n")[0] : " -> valid") + " @" + node.loc.start.line + ":" + node.loc.start.column);
    },
  });

  const clone = csstree.clone(ast);
  csstree.walk(clone, {
    visit: "Dimension",
    enter(node) {
      if (node.unit === "px")
        node.value = String(Number(node.value) * 2);
    },
  });

  return JSON.stringify({
    selectors,
    declarations,
    doubled: csstree.generate(clone),
    list: csstree.toPlainObject(csstree.parse("a, b > c", { context: "selectorList" })).children.length,
    property: csstree.property("-webkit-border-radius"),
    keyword: csstree.keyword("-moz-max-content"),
  }, null, 2);
};
