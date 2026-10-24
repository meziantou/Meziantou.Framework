// Preact with preact-render-to-string: components, hooks, context, fragments, JSX-like h() calls and pretty printing
import { h, Fragment, createContext, Component } from "preact";
import { useState, useContext, useMemo, useReducer } from "preact/hooks";
import { render, renderToStaticMarkup } from "preact-render-to-string";

const Locale = createContext("en");

function Price({ amount }) {
  const locale = useContext(Locale);
  return h("span", { class: "price" }, new Intl.NumberFormat(locale, { style: "currency", currency: "EUR" }).format(amount));
}

class Counter extends Component {
  constructor(props) {
    super(props);
    this.state = { count: props.start };
  }

  render(props, state) {
    return h("button", { disabled: state.count > 5, "aria-pressed": "false" }, props.label, ": ", state.count);
  }
}

function Cart({ products }) {
  const [open] = useState(true);
  const [state] = useReducer((current, action) => current, { coupon: "SAVE10" });
  const total = useMemo(() => products.reduce((sum, product) => sum + product.price, 0), [products]);
  return h(Fragment, null,
    h("h2", { style: { color: "red", marginTop: 10 } }, "Cart <", products.length, ">"),
    open && h("ul", null, products.map(product => h("li", { key: product.name }, product.name, " ", h(Price, { amount: product.price })))),
    h("p", null, "Total: ", h(Price, { amount: total }), " ", state.coupon),
    h(Counter, { label: "Clicks", start: 3 }),
    h("input", { type: "checkbox", checked: true, value: "x & y" }));
}

const products = [{ name: "Book", price: 12.5 }, { name: "Pen & Paper", price: 3 }];

export default () => [
  render(h(Locale.Provider, { value: "fr-FR" }, h(Cart, { products }))),
  render(h(Cart, { products }), {}, { pretty: true }),
  renderToStaticMarkup(h("div", { dangerouslySetInnerHTML: { __html: "<b>raw</b>" } })),
].join("\n----\n");
