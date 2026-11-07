// htmlparser2: the streaming tokenizer and parser (callbacks), the DOM handler, domutils queries and dom-serializer,
// in HTML and XML modes
import { Parser, parseDocument, DomUtils } from "htmlparser2";
import render from "dom-serializer";

const html = `<!DOCTYPE html><html><head><title>Test &amp; check</title><script>if (a < b && c > d) { x = "</p>"; }</script></head>
<body><div id="main" class="a b"><p>Para <b>bold<i>nested</b> text</i><br><img src=x.png alt='a "quoted" alt'>
<ul><li>One<li>Two</ul><!-- comment --><table><tr><td>1<td>2</table>
<a href="/link?a=1&b=2">link &copy; &#x263A; &unknown;</a></div><svg viewBox="0 0 1 1"><circle cx="1"/></svg></body></html>`;

export default () => {
  const events = [];
  const parser = new Parser({
    onopentag: (name, attributes) => events.push("open " + name + " " + JSON.stringify(attributes)),
    ontext: text => text.trim() && events.push("text " + JSON.stringify(text)),
    onclosetag: (name, implied) => events.push("close " + name + (implied ? " (implied)" : "")),
    oncomment: data => events.push("comment " + data),
    onprocessinginstruction: (name, data) => events.push("pi " + name + " " + data),
  }, { decodeEntities: true, recognizeSelfClosing: true });
  for (let i = 0; i < html.length; i += 37)
    parser.write(html.slice(i, i + 37));
  parser.end();

  const document = parseDocument(html);
  const xml = parseDocument("<root xmlns:a='urn:a'><a:item id='1'>x<![CDATA[<y>]]></a:item><Item/></root>", { xmlMode: true });
  return [
    events.join("\n"),
    DomUtils.findAll(element => element.name === "li" || element.name === "td", document.children).map(element => DomUtils.textContent(element)).join(","),
    DomUtils.getAttributeValue(DomUtils.getElementById("main", document.children), "class"),
    DomUtils.getElementsByTagName("a", document.children).map(element => element.attribs.href).join(","),
    render(document),
    render(document, { decodeEntities: false, xmlMode: true }),
    render(xml, { xmlMode: true }),
    DomUtils.getOuterHTML(DomUtils.findOne(element => element.name === "a:item", xml.children)),
  ].join("\n----\n");
};
