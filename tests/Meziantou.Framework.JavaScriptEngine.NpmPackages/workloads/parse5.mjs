// parse5: the HTML Standard tokenizer and tree construction (implied tags, foster parenting, adoption agency, foreign
// content, templates), fragments, serialization and parse errors
import { parse, parseFragment, serialize, serializeOuter } from "parse5";

const inputs = [
  "<!DOCTYPE html><title>T</title><p>One<p>Two<div>Block</div>",
  "<table><tr><td>cell</td></tr>text<tr><td>2</table>",
  "<b>bold<i>both</b>italic</i>",
  "<p><b><i>a</p>b",
  "<svg><foreignObject><p>html in svg</p></foreignObject><circle/></svg><math><mi>x</mi></math>",
  "<template><td>in template</td></template>",
  "<select><option>a<option>b<optgroup><option>c</select>",
  "<ul><li>1<li>2</ul><dl><dt>t<dd>d</dl>",
  "<textarea>\n<b>not bold</b></textarea><pre>\n\nkept</pre>",
  "<a href=x>1<a href=y>2</a>",
  "&amp; &copy &notin; &unknown; &#x110000; &#0;",
  "<!-- unclosed comment",
  "<frameset><frame></frameset>",
];

export default () => {
  const results = [];
  for (const input of inputs) {
    const errors = [];
    const document = parse(input, { onParseError: error => errors.push(error.code), sourceCodeLocationInfo: true });
    results.push(serialize(document) + "\n  errors: " + errors.join(", "));
  }

  const fragment = parseFragment("<td>cell</td><tr><td>row</td></tr>", { onParseError: null });
  results.push(serialize(fragment));
  const located = parse("<div class=a>\n  <span>x</span>\n</div>", { sourceCodeLocationInfo: true });
  const div = located.childNodes[0].childNodes[1].childNodes[0];
  results.push(serializeOuter(div) + " " + JSON.stringify(div.sourceCodeLocation.startTag));
  return results.join("\n----\n");
};
