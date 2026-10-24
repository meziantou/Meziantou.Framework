// React 19 server rendering (react-dom/server.browser): components, hooks, context, fragments, keys, Suspense with
// a resolved promise (use), escaping, renderToString and the streaming renderer (ReadableStream is not required)
import React, { createContext, useContext, useState, useMemo, useId, Fragment, Suspense, memo, forwardRef } from "react";
import { renderToString, renderToStaticMarkup } from "react-dom/server.browser";

const h = React.createElement;
const Theme = createContext("light");

function Item({ item }) {
  const theme = useContext(Theme);
  const id = useId();
  return h("li", { id, className: theme, "data-done": item.done, style: { textDecoration: item.done ? "line-through" : undefined, fontSize: 14 } }, item.title, item.done && h("span", null, " ✓"));
}

const MemoItem = memo(Item);
const Input = forwardRef((props, ref) => h("input", { ...props, ref, defaultValue: props.value, readOnly: true }));

function TodoList({ items }) {
  const [filter] = useState("all");
  const visible = useMemo(() => items.filter(item => filter === "all" || !item.done), [items, filter]);
  return h(Fragment, null,
    h("h1", { title: "Todo <list> & \"quotes\"" }, "Todos (", visible.length, ")"),
    h(Input, { value: "search <x>", "aria-label": "Search" }),
    h("ul", null, visible.map(item => h(MemoItem, { key: item.id, item }))),
    h("p", { dangerouslySetInnerHTML: { __html: "<em>raw</em>" } }),
    h("svg", { viewBox: "0 0 10 10", xmlnsXlink: "http://www.w3.org/1999/xlink" }, h("circle", { cx: 5, cy: 5, r: 4, strokeWidth: 2 })),
    h("select", { value: "b", onChange: () => { } }, h("option", { value: "a" }, "A"), h("option", { value: "b" }, "B")),
    h("textarea", { value: "text", readOnly: true }));
}

function App({ items }) {
  return h(Theme.Provider, { value: "dark" }, h("main", null, h(TodoList, { items })));
}

const items = [{ id: 1, title: "Write <tests>", done: true }, { id: 2, title: "Run them", done: false }, { id: 3, title: "Ship & celebrate", done: false }];

export default () => [
  renderToString(h(App, { items })),
  renderToStaticMarkup(h(App, { items: items.slice(0, 1) })),
  renderToString(h(Suspense, { fallback: h("p", null, "Loading") }, h("div", null, "ready"))),
  renderToStaticMarkup(h("div", { hidden: true, tabIndex: -1, draggable: false, contentEditable: true, suppressContentEditableWarning: true, "data-x": null, className: undefined }, 0, null, false, "", [1, [2, [3]]])),
].join("\n----\n");
