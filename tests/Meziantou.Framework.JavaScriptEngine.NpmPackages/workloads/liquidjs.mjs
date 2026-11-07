// LiquidJS (its browser build): tags, filters, loops with forloop, assignments, captures, whitespace control, custom
// filters and tags, partials from an in-memory file system, and errors
import { Liquid } from "liquidjs/dist/liquid.browser.mjs";

const templates = {
  "header.liquid": "<header>{{ title | upcase }}</header>",
  "product.liquid": "{{ product.name }} ({{ product.price | money }})",
};

export default async () => {
  const engine = new Liquid({
    templates,
    strictFilters: true,
    relativeReference: false,
    timezoneOffset: 0,
  });
  engine.registerFilter("money", value => "$" + Number(value).toFixed(2));
  engine.registerTag("upper", {
    parse(token) { this.value = token.args; },
    *render(context) { return String(yield this.liquid.evalValue(this.value, context)).toUpperCase(); },
  });

  const template = `{% render 'header.liquid', title: shop %}
{%- assign sorted = products | sort: "price" | reverse -%}
{% for product in sorted limit: 3 %}
  {{ forloop.index }}/{{ forloop.length }} {% render 'product.liquid', product: product %}{% if forloop.last %} (last){% endif %}
{%- endfor %}
{% capture greeting %}Hello {{ user.name | default: "guest" | capitalize }}{% endcapture %}{{ greeting | append: "!" }}
{% case user.role %}{% when "admin" %}Admin{% when "user" %}User{% else %}Other{% endcase %}
{{ "2024-02-29T13:45:00Z" | date: "%A %d %B %Y %H:%M" }} {{ 10 | divided_by: 3.0 | round: 2 }} {{ "a,b,c" | split: "," | join: " - " }}
{{ "<p>x</p>" | escape }} {{ "hello world" | truncatewords: 1 }} {{ products | map: "name" | join: ", " }} {{ products | where: "available", true | size }}
{% unless user.admin %}not admin{% endunless %} {% upper user.name %} {% raw %}{{ raw }}{% endraw %}
{% tablerow p in products cols: 2 %}{{ p.name }}{% endtablerow %}`;

  const context = {
    shop: "My Shop",
    user: { name: "alice", role: "user" },
    products: [{ name: "Pen", price: 1.5, available: true }, { name: "Book", price: 12, available: false }, { name: "Bag", price: 30, available: true }, { name: "Cup", price: 4, available: true }],
  };

  const results = [await engine.parseAndRender(template, context), engine.parseAndRenderSync("{{ a | plus: 1 }}{% for i in (1..3) %}{{ i }}{% endfor %}", { a: 41 })];
  for (const invalid of ["{% if %}", "{{ a | unknown_filter }}", "{% for %}{% endfor %}", "{% render 'missing.liquid' %}"]) {
    try {
      results.push(await engine.parseAndRender(invalid, {}));
    }
    catch (error) {
      results.push(error.name + ": " + error.message.split("\n")[0]);
    }
  }

  return results.join("\n----\n");
};
