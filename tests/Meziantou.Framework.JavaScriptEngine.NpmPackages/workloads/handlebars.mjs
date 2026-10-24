// Handlebars: compilation of templates to JavaScript (new Function), helpers, partials, block helpers, each/with/if,
// escaping, whitespace control, precompilation and errors
import Handlebars from "handlebars";

const template = `<h1>{{title}}</h1>
{{#if author}}<p class="author">{{author.firstName}} {{author.lastName}}</p>{{else}}<p>Anonymous</p>{{/if}}
<ul>
{{#each items as |item index|}}
  <li{{#if @first}} class="first"{{/if}}>{{index}}: {{item.name}} - {{currency item.price}}{{#unless @last}},{{/unless}}</li>
{{~/each}}
</ul>
{{> footer year=2024}}
{{#with stats}}{{count}} items, {{{html}}} vs {{html}}{{/with}}
{{#list tags}}{{this}}{{/list}}
{{lookup labels kind}} {{../title}} {{!-- comment --}}
{{#each (sort items "price")}}{{name}} {{/each}}
{{raw-helper}}
`;

export default () => {
  const handlebars = Handlebars.create();
  handlebars.registerHelper("currency", value => new handlebars.SafeString("<b>$" + value.toFixed(2) + "</b>"));
  handlebars.registerHelper("list", function (items, options) {
    return "<ol>" + items.map(item => "<li>" + options.fn(item) + "</li>").join("") + "</ol>";
  });
  handlebars.registerHelper("sort", (items, key) => [...items].sort((a, b) => a[key] - b[key]));
  handlebars.registerHelper("raw-helper", () => "<i>escaped</i>");
  handlebars.registerPartial("footer", "<footer>&copy; {{year}} {{title}}</footer>");
  const context = {
    title: "Products & <Prices>",
    author: { firstName: "Ada", lastName: "Lovelace" },
    items: [{ name: "Pen", price: 1.5 }, { name: "Book", price: 12 }, { name: "Bag", price: 30.25 }],
    stats: { count: 3, html: "<em>emphasis</em>" },
    tags: ["a", "b"],
    labels: { new: "New!" },
    kind: "new",
  };

  const results = [handlebars.compile(template)(context), handlebars.compile(template, { noEscape: true, strict: false })(context)];
  results.push(handlebars.precompile("{{#each list}}{{this}}{{/each}}").toString().length > 100 ? "precompiled" : "too short");
  results.push(JSON.stringify(handlebars.parse("{{a}} {{#b}}c{{/b}}").body.map(node => node.type)));
  for (const invalid of ["{{#if}}{{/each}}", "{{> missing}}", "{{foo"]) {
    try {
      results.push(handlebars.compile(invalid, { strict: true })({}));
    }
    catch (error) {
      results.push(error.message.split("\n")[0]);
    }
  }

  return results.join("\n----\n");
};
