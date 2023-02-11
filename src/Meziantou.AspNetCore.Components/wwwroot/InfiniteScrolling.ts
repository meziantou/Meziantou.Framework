export function initialize(lastIndicator : HTMLElement, instance : DotNet.DotNetObject) {
  const options = {
    root: findClosestScrollContainer(lastIndicator),
    rootMargin: '0px',
    threshold: 0,
  };

  if (isValidTableElement(lastIndicator.parentElement)) {
    lastIndicator.style.display = 'table-row';
  }

  const observer = new IntersectionObserver(async (entries) => {
    for (const entry of entries) {
      if (entry.isIntersecting) {
        observer.unobserve(lastIndicator);
        await instance.invokeMethodAsync("LoadMoreItems");
      }
    }
  }, options);

  observer.observe(lastIndicator);

  return {
    dispose: () => infiniteScollingDispose(observer),
    onNewItems: () => {
      observer.unobserve(lastIndicator);
      observer.observe(lastIndicator);
    },
  };
}

function findClosestScrollContainer(element : HTMLElement | null) : HTMLElement | null {
  while (element) {
    const style = getComputedStyle(element);
    if (style.overflowY !== 'visible') {
      return element;
    }

    element = element.parentElement;
  }

  return null;
}

function infiniteScollingDispose(observer : IntersectionObserver) {
  observer.disconnect();
}

function isValidTableElement(element: HTMLElement | null): boolean {
  if (element === null) {
    return false;
  }

  return ((element instanceof HTMLTableElement && element.style.display === '') || element.style.display === 'table')
    || ((element instanceof HTMLTableSectionElement && element.style.display === '') || element.style.display === 'table-row-group');
}
