// The Content Security Policy of the pages with scripting hides the nonce attributes of the connected elements
// (https://html.spec.whatwg.org/multipage/urls-and-fetching.html#nonce-attributes): restore their value from the nonce
// IDL attribute, which keeps it, including in the shadow trees
const restoreNonces = root => {
  for (const element of root.querySelectorAll('*')) {
    if (element.hasAttribute('nonce') && element.nonce) {
      element.setAttribute('nonce', element.nonce);
    }

    if (element.shadowRoot) {
      restoreNonces(element.shadowRoot);
    }
  }
};
restoreNonces(document);

// Returns the tree of the document, its serialization, and the tree of the document parsed from the serialization by
// DOMParser (with the scripting disabled, and without declarative shadow roots)
const tree = dumpTree(document);
const serialization = serializeDocument(document);
return [tree, serialization, dumpTree(new DOMParser().parseFromString(serialization, 'text/html'))];
