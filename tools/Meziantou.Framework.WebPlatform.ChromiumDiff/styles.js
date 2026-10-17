// Returns the computed display, position and float of the elements of the given keys (see layout.js), and the display of
// their parent, to describe the differences of the layout comparison
keys => keys.map(key => {
  let element = document.documentElement;
  for (const index of key.split('/').slice(1)) {
    element = element.children[Number(index)];
  }

  const style = getComputedStyle(element);
  const parent = element.parentElement ? getComputedStyle(element.parentElement).display : '';
  return [style.display, style.position, style.cssFloat, parent];
})
