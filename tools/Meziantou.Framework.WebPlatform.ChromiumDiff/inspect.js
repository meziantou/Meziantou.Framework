// Returns the computed values of the given properties of the elements of the given keys (see layout.js), and their client
// and scroll sizes, to triage the differences of the layout comparison (--inspect)
([keys, properties]) => keys.map(key => {
  let element = document.documentElement;
  for (const index of key.split('/').slice(1)) {
    element = element.children[Number(index)];
  }

  const style = getComputedStyle(element);
  return [
    ...properties.map(property => style.getPropertyValue(property)),
    `client=${element.clientWidth}x${element.clientHeight} scroll=${element.scrollWidth}x${element.scrollHeight}`,
  ];
})
