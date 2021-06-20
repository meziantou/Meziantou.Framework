export function blazorScrollToId(id: string) {
  const element = document.getElementById(id);
  if (element instanceof HTMLElement) {
    element.scrollIntoView({
      behavior: "smooth",
      block: "start",
      inline: "nearest"
    });
  }
}
