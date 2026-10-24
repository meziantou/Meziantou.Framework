// Fuse.js: fuzzy search (Bitap) over nested keys with weights, extended search syntax, logical queries, match indices
// and an index built ahead
import Fuse from "fuse.js";

const books = [
  { title: "Old Man's War", author: { firstName: "John", lastName: "Scalzi" }, tags: ["fiction", "war"] },
  { title: "The Lock Artist", author: { firstName: "Steve", lastName: "Hamilton" }, tags: ["thriller"] },
  { title: "HTML5", author: { firstName: "Remy", lastName: "Sharp" }, tags: ["web"] },
  { title: "Right Ho Jeeves", author: { firstName: "P.D", lastName: "Woodhouse" }, tags: ["comedy"] },
  { title: "The Code of the Wooster", author: { firstName: "P.D", lastName: "Woodhouse" }, tags: ["comedy"] },
  { title: "Thank You Jeeves", author: { firstName: "P.D", lastName: "Woodhouse" }, tags: ["comedy"] },
  { title: "The DaVinci Code", author: { firstName: "Dan", lastName: "Brown" }, tags: ["thriller", "mystery"] },
  { title: "Angels & Demons", author: { firstName: "Dan", lastName: "Brown" }, tags: ["thriller"] },
  { title: "Frankenstein", author: { firstName: "Mary", lastName: "Shelley" }, tags: ["fiction", "horror"] },
  { title: "Mérida's Café", author: { firstName: "José", lastName: "Núñez" }, tags: ["food"] },
];

const format = results => results.map(result => [result.item.title, Math.round(result.score * 1e6) / 1e6, result.matches?.map(match => [match.key, match.indices])]);

export default () => {
  const options = { includeScore: true, includeMatches: true, threshold: 0.4, keys: [{ name: "title", weight: 2 }, "author.lastName", "tags"] };
  const fuse = new Fuse(books, options);
  const indexed = new Fuse(books, options, Fuse.createIndex(options.keys, books));
  const extended = new Fuse(books, { ...options, useExtendedSearch: true });
  return JSON.stringify({
    fuzzy: ["jeves", "wodhouse", "the code", "thrller", "cafe"].map(query => [query, format(fuse.search(query, { limit: 3 }))]),
    indexed: format(indexed.search("brwn")),
    extended: ["'Jeeves", "^The", "Code$", "!fiction", "=HTML5", "jeeves | angels"].map(query => [query, extended.search(query).map(result => result.item.title)]),
    logical: format(fuse.search({ $and: [{ "author.lastName": "woodhouse" }, { title: "jeeves" }] })),
    ignoreLocation: new Fuse(books, { keys: ["title"], ignoreLocation: true, ignoreDiacritics: true, threshold: 0.2 }).search("cafe").map(result => result.item.title),
    strings: new Fuse(["apple", "pineapple", "applesauce", "grape"], { includeScore: true }).search("aple").map(result => [result.item, Math.round(result.score * 1e6) / 1e6]),
  });
};
