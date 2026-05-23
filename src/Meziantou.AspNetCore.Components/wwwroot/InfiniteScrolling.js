/// <reference path="../ts-types/blazor.d.ts" />

/**
 *
 * @param {HTMLElement} lastIndicator
 * @param {DotNet.DotNetObject} instance
 */
export function initialize(lastIndicator, instance) {
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

/**
 * @param {HTMLElement | null} element
 * @returns {HTMLElement | null}
 */
function findClosestScrollContainer(element) {
    while (element) {
        const style = getComputedStyle(element);
        if (style.overflowY !== 'visible') {
            return element;
        }
        element = element.parentElement;
    }
    return null;
}

/**
 * @param {IntersectionObserver} observer
 */
function infiniteScollingDispose(observer) {
    observer.disconnect();
}

/**
 * @param {HTMLElement | null} element
 * @returns {boolean}
 */
function isValidTableElement(element) {
    if (element === null) {
        return false;
    }
    return ((element instanceof HTMLTableElement && element.style.display === '') || element.style.display === 'table')
        || ((element instanceof HTMLTableSectionElement && element.style.display === '') || element.style.display === 'table-row-group');
}
