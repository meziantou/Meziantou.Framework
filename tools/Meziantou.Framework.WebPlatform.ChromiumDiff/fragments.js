// Parses each input with the innerHTML setter of each context element (arg: { inputs, contexts }), and returns for each
// pair the tree of the fragment, its serialization (getHTML), and the tree of the fragment parsed from the serialization.
// The context elements are not connected; their node document is this document (no-quirks, with the scripting of the
// page) or a quirks-mode document created by DOMParser (without scripting).
const quirksDocument = new DOMParser().parseFromString('', 'text/html');
const createContext = context => {
  const element = (context.quirks ? quirksDocument : document).createElementNS(context.namespace, context.name);
  for (const [name, value] of context.attributes) {
    element.setAttribute(name, value);
  }

  return element;
};
const getRoot = element => element.namespaceURI === 'http://www.w3.org/1999/xhtml' && element.localName === 'template' ? element.content : element;
const results = [];
for (const input of arg.inputs) {
  for (const context of arg.contexts) {
    const element = createContext(context);
    element.innerHTML = input;
    const serialization = element.getHTML({ serializableShadowRoots: true });
    const roundTrip = createContext(context);
    roundTrip.innerHTML = serialization;
    results.push([dumpTree(getRoot(element)), serialization, dumpTree(getRoot(roundTrip))]);
  }
}

return results;
