// The default export
export default async function greet(name) {
  await new Promise(resolve => setTimeout(resolve, 10));
  return `Hello, ${name}!`;
}
