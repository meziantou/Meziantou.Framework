// Mustache: sections, inverted sections, partials, lambdas, custom delimiters, escaping and the parse tree
import Mustache from "mustache";

const template = `{{greeting}}, {{name}}! {{{raw}}} {{&raw}}
{{#items}}
  - {{name}}: {{price}}{{#onSale}} (sale){{/onSale}}
{{/items}}
{{^empty}}No empty items.{{/empty}}
{{#wrapped}}{{name}} is awesome.{{/wrapped}}
{{> signature}}
{{=<% %>=}}<% name %> with custom delimiters<%={{ }}=%>
{{person.address.city}} {{missing}}.
`;

export default () => {
  const view = {
    greeting: "Hello",
    name: "Chris <&>",
    raw: "<b>bold</b>",
    items: [{ name: "Apple", price: 1, onSale: true }, { name: "Pear", price: 2 }],
    empty: [],
    wrapped: () => (text, render) => "<em>" + render(text) + "</em>",
    person: { address: { city: "Paris" } },
  };
  return [
    Mustache.render(template, view, { signature: "-- {{name}}" }),
    JSON.stringify(Mustache.parse("{{a}} {{#b}}{{c}}{{/b}}")),
    Mustache.render("[[a]]", { a: 1 }, {}, ["[[", "]]"]),
  ].join("\n----\n");
};
