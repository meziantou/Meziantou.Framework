// The functions shared by document.js and fragments.js (TreeComparison.CreateScript prepends this file to them)

// Serializes the children of a node in the html5lib tree format (see TreeDumper.cs, which must produce the same output)
const dumpTree = root => {
  const html = 'http://www.w3.org/1999/xhtml';
  const elementPrefixes = { 'http://www.w3.org/2000/svg': 'svg ', 'http://www.w3.org/1998/Math/MathML': 'math ' };
  const attributePrefixes = { 'http://www.w3.org/1999/xlink': 'xlink ', 'http://www.w3.org/XML/1998/namespace': 'xml ', 'http://www.w3.org/2000/xmlns/': 'xmlns ' };
  const lines = [];
  const indent = depth => '| ' + ' '.repeat(depth * 2);
  const dumpChildren = (parent, depth) => {
    for (let child = parent.firstChild; child; child = child.nextSibling) {
      dump(child, depth);
    }
  };
  const dump = (node, depth) => {
    switch (node.nodeType) {
      case Node.DOCUMENT_TYPE_NODE:
        lines.push(indent(depth) + '<!DOCTYPE ' + node.name + (node.publicId || node.systemId ? ' "' + node.publicId + '" "' + node.systemId + '"' : '') + '>');
        break;
      case Node.ELEMENT_NODE: {
        lines.push(indent(depth) + '<' + (elementPrefixes[node.namespaceURI] || '') + node.localName + '>');
        const attributes = Array.from(node.attributes, a => [(attributePrefixes[a.namespaceURI] || '') + a.localName, a.value]);
        attributes.sort((a, b) => a[0] < b[0] ? -1 : a[0] > b[0] ? 1 : 0);
        for (const [name, value] of attributes) {
          lines.push(indent(depth + 1) + name + '="' + value + '"');
        }

        if (node.shadowRoot) {
          lines.push(indent(depth + 1) + '#shadow-root (' + node.shadowRoot.mode + ')');
          dumpChildren(node.shadowRoot, depth + 2);
        }

        if (node.namespaceURI === html && node.localName === 'template') {
          lines.push(indent(depth + 1) + 'content');
          dumpChildren(node.content, depth + 2);
        }

        dumpChildren(node, depth + 1);
        break;
      }
      case Node.TEXT_NODE:
        lines.push(indent(depth) + '"' + node.data + '"');
        break;
      case Node.COMMENT_NODE:
        lines.push(indent(depth) + '<!-- ' + node.data + ' -->');
        break;
      case Node.PROCESSING_INSTRUCTION_NODE:
        lines.push(indent(depth) + '<?' + node.target + ' ' + node.data + '?>');
        break;
    }
  };
  dumpChildren(root, 0);
  return lines.join('\n');
};

// Serializes a document as the HTML fragment serialization algorithm serializes its children, with the serializable
// shadow roots (see TreeComparison.SerializeDocument, which must produce the same output)
const serializeDocument = document => Array.from(document.childNodes, node => {
  switch (node.nodeType) {
    case Node.DOCUMENT_TYPE_NODE:
      return '<!DOCTYPE ' + node.name + '>';
    case Node.COMMENT_NODE:
      return '<!--' + node.data + '-->';
    case Node.ELEMENT_NODE: {
      const endTag = '</' + node.localName + '>';
      const tags = node.cloneNode(false).outerHTML;
      return tags.slice(0, tags.length - endTag.length) + node.getHTML({ serializableShadowRoots: true }) + endTag;
    }
    default:
      return '';
  }
}).join('');
