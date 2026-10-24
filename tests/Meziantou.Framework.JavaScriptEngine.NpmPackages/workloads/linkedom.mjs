// linkedom: a DOM implementation in JavaScript (Document, Element, events, selectors, attributes, classList, dataset,
// TreeWalker, custom elements, innerHTML and serialization) used without a browser
import { parseHTML, DOMParser } from "linkedom";

export default () => {
  const { document, window, customElements, HTMLElement, Event, CustomEvent } = parseHTML("<!DOCTYPE html><html lang=en><head><title>T</title></head><body><main id=app><p class='a b'>Hello <b>world</b></p><ul></ul></main></body></html>");
  const results = [];
  const list = document.querySelector("ul");
  for (const [index, name] of ["one", "two", "three"].entries()) {
    const item = document.createElement("li");
    item.textContent = name + " <&>";
    item.dataset.index = String(index);
    item.classList.add("item", index % 2 ? "odd" : "even");
    list.appendChild(item);
  }

  const log = [];
  list.addEventListener("click", event => log.push("list " + event.target.textContent + " " + event.eventPhase));
  list.children[1].addEventListener("click", event => { log.push("item"); event.stopPropagation(); }, { once: true });
  list.children[1].dispatchEvent(new Event("click", { bubbles: true }));
  list.children[1].dispatchEvent(new Event("click", { bubbles: true }));
  document.addEventListener("custom", event => log.push("custom " + JSON.stringify(event.detail)));
  document.dispatchEvent(new CustomEvent("custom", { detail: { x: 1 } }));
  results.push(log.join(" | "));

  class MyGreeting extends HTMLElement {
    connectedCallback() { this.innerHTML = `<span>Hi ${this.getAttribute("name")}</span>`; }
    static get observedAttributes() { return ["name"]; }
    attributeChangedCallback(name, oldValue, newValue) { log.push(`attr ${name} ${oldValue} -> ${newValue}`); }
  }
  customElements.define("my-greeting", MyGreeting);
  const greeting = document.createElement("my-greeting");
  greeting.setAttribute("name", "Ada");
  document.getElementById("app").append(greeting);
  results.push(greeting.outerHTML, log.slice(-1)[0]);

  results.push(document.querySelectorAll("li.odd, p > b").length, document.querySelector("li:last-child").dataset.index, document.querySelector("p").closest("main").id, [...document.querySelectorAll("[data-index]")].map(element => element.className).join("|"));
  const walker = document.createTreeWalker(document.body, 4 /* NodeFilter.SHOW_TEXT */);
  const texts = [];
  while (walker.nextNode())
    texts.push(walker.currentNode.data);
  results.push(texts.join("|"));

  const p = document.querySelector("p");
  p.insertAdjacentHTML("beforeend", "<i>!</i>");
  p.setAttribute("title", "a \"title\"");
  p.removeAttribute("class");
  p.replaceChild(document.createTextNode("Bye "), p.firstChild);
  results.push(p.outerHTML, document.title, document.documentElement.lang, String(window === document.defaultView));
  const xml = new DOMParser().parseFromString("<root><item id='1'>a</item><item>b</item></root>", "text/xml");
  results.push(xml.querySelectorAll("item").length + " " + xml.toString());
  results.push(document.toString());
  return results.join("\n----\n");
};
