// Turns a title into a URL segment: "Hello, World!" becomes "hello-world"
export function slugify(title) {
  return title.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');
}

// An export that is not a function
export const separator = '-';
