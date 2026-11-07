// Measures the border boxes of the elements of the document, in document coordinates (see LayoutComparison.MeasureLibrary,
// which must measure the same elements). Each element is identified by its key: the indexes of the element children from
// the root element ("0/1/3": the 4th element child of the 2nd element child of the root). The contents of the head, svg,
// math and select elements are not measured.
() => {
  const keys = [];
  const rects = [];
  const scrollX = window.scrollX;
  const scrollY = window.scrollY;
  const visit = (element, key) => {
    const rect = element.getBoundingClientRect();
    keys.push(key);
    rects.push(rect.x + scrollX, rect.y + scrollY, rect.width, rect.height);
    if (element.localName === 'head' || element.localName === 'select' || element.localName === 'svg' || element.localName === 'math') {
      return;
    }

    let index = 0;
    for (let child = element.firstElementChild; child; child = child.nextElementSibling) {
      visit(child, key + '/' + index);
      index++;
    }
  };
  visit(document.documentElement, '0');
  return { keys, rects };
}
