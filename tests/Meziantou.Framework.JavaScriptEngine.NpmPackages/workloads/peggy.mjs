// Peggy: a parser generator. The grammar of a small language is compiled to JavaScript (eval), then the generated parser
// evaluates expressions and reports syntax errors
import peggy from "peggy";

const grammar = `
{{
  function fold(head, tail) { return tail.reduce((left, [, op, , right]) => ({ op, left, right }), head); }
}}
Program = _ statements:(Statement _)* { return statements.map(([statement]) => statement); }
Statement = "let" __ name:Identifier _ "=" _ value:Expression _ ";" { return { type: "let", name, value }; }
          / expression:Expression _ ";" { return { type: "expr", expression }; }
Expression = head:Term tail:(_ ("+" / "-") _ Term)* { return fold(head, tail); }
Term = head:Factor tail:(_ ("*" / "/" / "%") _ Factor)* { return fold(head, tail); }
Factor = "(" _ expression:Expression _ ")" { return expression; }
       / Call / Number / String / name:Identifier { return { name }; }
Call = callee:Identifier "(" _ args:(Expression (_ "," _ Expression)*)? _ ")" { return { callee, args: args ? [args[0], ...args[1].map(item => item[3])] : [] }; }
Number "number" = digits:$([0-9]+ ("." [0-9]+)?) { return parseFloat(digits); }
String "string" = '"' chars:$([^"\\\\] / "\\\\" .)* '"' { return chars; }
Identifier "identifier" = $([a-zA-Z_] [a-zA-Z0-9_]*)
_ "whitespace" = ([ \\t\\n\\r] / "//" [^\\n]*)*
__ = [ \\t\\n\\r]+
`;

function evaluate(node, scope) {
  if (typeof node === "number" || typeof node === "string")
    return node;
  if (node.name !== undefined)
    return scope[node.name];
  if (node.callee !== undefined)
    return scope[node.callee](...node.args.map(arg => evaluate(arg, scope)));

  const left = evaluate(node.left, scope);
  const right = evaluate(node.right, scope);
  return { "+": left + right, "-": left - right, "*": left * right, "/": left / right, "%": left % right }[node.op];
}

export default () => {
  const parser = peggy.generate(grammar, { cache: true });
  const program = parser.parse(`
        let x = 1 + 2 * 3;   // comment
        let y = (x - 1) / 2 % 4;
        let s = concat("a", "b\\"c", max(x, y, 10));
        x * y + s;
    `);
  const scope = { concat: (...values) => values.join(""), max: Math.max };
  const values = program.map(statement => statement.type === "let" ? (scope[statement.name] = evaluate(statement.value, scope)) : evaluate(statement.expression, scope));
  const errors = ["let = 1;", "1 + ;", 'let x = "unterminated;'].map(source => {
    try {
      parser.parse(source);
      return "no error";
    }
    catch (error) {
      return error.message + " at " + error.location.start.line + ":" + error.location.start.column;
    }
  });

  const source = peggy.generate(grammar, { output: "source", format: "commonjs" });
  return [JSON.stringify(program), JSON.stringify(values), errors.join("\n"), String(source.length > 1000)].join("\n----\n");
};
