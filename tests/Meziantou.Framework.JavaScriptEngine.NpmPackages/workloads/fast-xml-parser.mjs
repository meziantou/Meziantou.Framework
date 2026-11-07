// fast-xml-parser: parsing XML to objects (attributes, CDATA, namespaces, entities, arrays, value parsing), building
// XML back and validation
import { XMLParser, XMLBuilder, XMLValidator } from "fast-xml-parser";

const xml = `<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE note [<!ENTITY writer "Writer: Donald Duck.">]>
<catalog xmlns:dc="http://purl.org/dc/elements/1.1/">
  <!-- a comment -->
  <book id="bk101" available="true" price="44.95">
    <dc:author>Gambardella, Matthew</dc:author>
    <title lang="en">XML Developer's Guide</title>
    <genre>Computer</genre>
    <publish_date>2000-10-01</publish_date>
    <description><![CDATA[An in-depth look at <XML> & more.]]></description>
    <tags><tag>a</tag><tag>b</tag></tags>
    <note>&writer; &amp; &lt;escaped&gt; &#169; &#x263A;</note>
  </book>
  <book id="bk102" available="false" price="5">
    <dc:author>Ralls, Kim</dc:author>
    <title lang="fr">Midnight Rain</title>
    <empty/>
    <mixed>text <b>bold</b> tail</mixed>
  </book>
</catalog>`;

export default () => {
  const results = [];
  const parser = new XMLParser({
    ignoreAttributes: false,
    attributeNamePrefix: "@",
    parseAttributeValue: true,
    processEntities: true,
    htmlEntities: true,
    cdataPropName: "#cdata",
    commentPropName: "#comment",
    removeNSPrefix: false,
    isArray: name => name === "tag",
  });
  const value = parser.parse(xml);
  results.push(JSON.stringify(value, null, 2));
  results.push(JSON.stringify(new XMLParser({ preserveOrder: true, ignoreAttributes: false }).parse("<a x=\"1\"><b>1</b><c/><b>2</b></a>")));
  results.push(JSON.stringify(new XMLParser({ parseTagValue: false, trimValues: false }).parse("<a> 0012 </a>")));
  results.push(new XMLBuilder({ ignoreAttributes: false, attributeNamePrefix: "@", format: true, cdataPropName: "#cdata", commentPropName: "#comment", suppressEmptyNode: true }).build(value));
  for (const document of [xml, "<a><b></a>", "<a x='1' x='2'/>", "<a>text", "<?xml version=\"1.0\"?><a/><b/>"]) {
    const validation = XMLValidator.validate(document);
    results.push(validation === true ? "valid" : JSON.stringify(validation.err));
  }

  return results.join("\n----\n");
};
