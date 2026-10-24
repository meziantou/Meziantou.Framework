// Lunr: a full-text search index (tokenizer, stemmer, stop words, inverted index, TF-IDF / BM25 scoring), queries with
// fields, wildcards, fuzziness, boosts and presence, and serialization of the index
import lunr from "lunr";

const documents = [
  { id: "1", title: "Searching with Lunr", body: "Lunr is a small, full-text search library for use in the browser. It indexes JSON documents and provides a simple search interface.", tags: "search javascript" },
  { id: "2", title: "Stemming words", body: "Stemming reduces words such as running, runs and ran to their root form, which improves the recall of searches.", tags: "nlp" },
  { id: "3", title: "Inverted indexes", body: "An inverted index maps each term to the documents that contain it, with the positions and frequencies of the term.", tags: "search data-structures" },
  { id: "4", title: "Scoring documents", body: "BM25 scores documents from the term frequencies and the inverse document frequencies, normalized by the length.", tags: "search ranking" },
  { id: "5", title: "Fuzzy matching", body: "Fuzzy matching finds terms within an edit distance, so typos such as serch still find search.", tags: "search" },
];

export default () => {
  const index = lunr(function () {
    this.ref("id");
    this.field("title", { boost: 10 });
    this.field("body");
    this.field("tags");
    this.metadataWhitelist = ["position"];
    documents.forEach(document => this.add(document));
  });

  const queries = ["search", "running", "title:index*", "serch~1", "+search -fuzzy", "documents^5 terms", "inver*", "tags:nlp", "the and of", "frequenc*"];
  const results = queries.map(query => [query, index.search(query).map(result => [result.ref, Math.round(result.score * 1e6) / 1e6, Object.keys(result.matchData.metadata).sort()])]);
  results.push(["query builder", index.query(query => { query.term("score", { fields: ["title"], wildcard: lunr.Query.wildcard.TRAILING, boost: 2 }); query.term("bm25", { presence: lunr.Query.presence.REQUIRED }); }).map(result => result.ref)]);
  const loaded = lunr.Index.load(JSON.parse(JSON.stringify(index)));
  results.push(["loaded", loaded.search("stem*").map(result => result.ref)]);
  results.push(["tokens", lunr.tokenizer("Hello, World! It's 2024-01-01.").map(token => token.toString())]);
  results.push(["stems", ["running", "generalization", "happiness", "flies", "relational"].map(word => lunr.stemmer(new lunr.Token(word)).toString())]);
  try {
    index.search("title:");
  }
  catch (error) {
    results.push([error.name, error.message]);
  }

  return JSON.stringify(results);
};
