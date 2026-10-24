// jsdiff: the Myers diff of characters, words, lines, sentences, JSON and arrays, unified patches, applying and
// reversing patches, and structured patches
import { diffChars, diffWords, diffWordsWithSpace, diffLines, diffSentences, diffJson, diffArrays, createTwoFilesPatch, createPatch, applyPatch, parsePatch, structuredPatch, reversePatch, formatPatch } from "diff";

const before = `function hello(name) {
  console.log("Hello, " + name);
  return true;
}

const unchanged = 1;
const removed = 2;
`;

const after = `function hello(name, greeting = "Hello") {
  console.log(greeting + ", " + name + "!");
  return true;
}

const unchanged = 1;
const added = 3;
// end
`;

const format = changes => changes.map(change => (change.added ? "+" : change.removed ? "-" : "=") + JSON.stringify(change.value)).join(" ");

export default () => {
  const patch = createTwoFilesPatch("a/file.js", "b/file.js", before, after, "old header", "new header", { context: 2 });
  const parsed = parsePatch(patch);
  return [
    format(diffChars("kitten sitting", "sitting kitten")),
    format(diffWords("The quick brown fox jumps", "The quick red fox leaps high")),
    format(diffWordsWithSpace("a  b c", "a b  c")),
    format(diffLines(before, after)),
    format(diffSentences("One. Two! Three?", "One. Deux! Three?")),
    format(diffJson({ a: 1, b: [1, 2], c: { d: "x" } }, { a: 1, b: [1, 3], c: { d: "x", e: null } })),
    format(diffArrays([1, 2, 3, 4], [1, 3, 4, 5])),
    patch,
    createPatch("file.txt", "a\nb\nc\n", "a\nB\nc\nd\n"),
    String(applyPatch(before, patch) === after),
    String(applyPatch(after, reversePatch(parsed)[0]) === before),
    JSON.stringify(structuredPatch("x", "y", "1\n2\n3\n", "1\n3\n4\n").hunks),
    formatPatch(parsed),
    String(applyPatch("completely different\n", patch)),
  ].join("\n----\n");
};
