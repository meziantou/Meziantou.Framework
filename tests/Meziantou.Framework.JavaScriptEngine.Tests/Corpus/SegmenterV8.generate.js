// Generates random texts, segments them into words with the Intl.Segmenter of V8 (ICU), and writes the results as JSON lines.
// Usage: node SegmenterV8.generate.js <directory of the ICU word lists> <seed> <count> <output>
'use strict';
const fs = require('fs');
const path = require('path');

const dictionaryDirectory = process.argv[2];
let seed = Number(process.argv[3] || 1) >>> 0;
const count = Number(process.argv[4] || 1000);
const output = process.argv[5] || 'corpus.jsonl';

function random() {
  // mulberry32
  seed = (seed + 0x6D2B79F5) >>> 0;
  let t = seed;
  t = Math.imul(t ^ (t >>> 15), t | 1);
  t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
  return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
}

function pick(items) { return items[Math.floor(random() * items.length)]; }

// The words of a list (gendict format: a word, then optionally white space and a value; '#' starts a comment)
function readWords(file) {
  return fs.readFileSync(path.join(dictionaryDirectory, file), 'utf8').split('\n').map(line => line.split('#')[0].trim().split(/\s/)[0]).filter(Boolean);
}

// The characters of a range that the Unicode version of node has (the engine may have a newer version)
const assigned = /\p{Assigned}/u;
function range(first, last) {
  const result = [];
  for (let c = first; c <= last; c++) {
    const s = String.fromCodePoint(c);
    if (assigned.test(s)) result.push(s);
  }

  return result;
}

const words = {
  cj: readWords('cjdict.txt'),
  th: readWords('thaidict.txt'),
  lo: readWords('laodict.txt'),
  km: readWords('khmerdict.txt'),
  my: readWords('burmesedict.txt'),
};
const scripts = Object.keys(words);
const scriptCharacters = {
  cj: [...range(0x4E00, 0x4E80), ...range(0x3005, 0x3007), ...range(0xF900, 0xF910), ...range(0x2F00, 0x2F10), '\u{20000}', '\u{20001}', '\u{2A6B2}',
    ...range(0x3041, 0x309F), ...range(0x30A0, 0x30FF), ...range(0xFF66, 0xFF9F), ...range(0x31F0, 0x31FF), ...range(0x3300, 0x3310), ...range(0x32D0, 0x32D5), '゙', '゚'],
  th: [...range(0x0E00, 0x0E7F), 'ฯ', 'ๆ', 'ั', '็', '่', '์', 'เ', 'ไ'],
  lo: range(0x0E80, 0x0EFF),
  km: [...range(0x1780, 0x17FF), ...range(0x19E0, 0x19FF)],
  my: [...range(0x1000, 0x109F), ...range(0xAA60, 0xAA7F), ...range(0xA9E0, 0xA9FF)],
};
const hangul = [...range(0xAC00, 0xAC20), ...range(0x1100, 0x1105), ...range(0x3131, 0x3135)];

// The other scripts of the line breaking class SA, which have no word list
const otherComplexContext = [...range(0x1A20, 0x1A30), ...range(0xAA80, 0xAA90), ...range(0x1950, 0x1958)];
const others = ['a', 'b', 'Z', '1', '9', ' ', '  ', '.', ',', "'", '_', '-', '̀', '‍', '‌', '­', '\u{1F600}', '\u{1F44D}\u{1F3FD}', '\u{1F1EB}\u{1F1F7}',
  '\n', '\r\n', '　', '。', '、', '「', '」', '（', '）', '０', '１', 'Ａ', '%', '$', 'א', '"', 'é', 'ß', '\t',
  '๐', '๑', '၀', '០'];

// Any character, with more of the characters the word rules treat specially
const allCharacters = [];
for (let c = 0; c < 0x40000; c++) {
  if (c >= 0xD800 && c <= 0xDFFF) continue;
  const s = String.fromCodePoint(c);
  if (assigned.test(s)) allCharacters.push(s);
}

const special = /[\p{Script=Han}\p{Script=Hiragana}\p{Script=Katakana}\p{Script=Thai}\p{Script=Lao}\p{Script=Khmer}\p{Script=Myanmar}\p{Script=Hangul}\p{Script=Hebrew}\p{Extended_Pictographic}\p{M}\p{Cf}\p{Zs}\p{Nd}\p{Pc}\p{Po}\p{Regional_Indicator}]/u;
const specialCharacters = allCharacters.filter(c => special.test(c));
const ascii = [..."aZ09_'.,:;\"-$% \t\n\r"];

function generateWords() {
  const script = pick(scripts);
  const onlyCharacters = random() < 0.15;
  let s = '';
  const n = 1 + Math.floor(random() * 12);
  for (let i = 0; i < n; i++) {
    const r = random();
    if (onlyCharacters) s += pick([...scriptCharacters[script], ...pick([others, hangul, otherComplexContext, scriptCharacters[pick(scripts)]])]);
    else if (r < 0.65) s += pick(words[script]);
    else if (r < 0.80) s += pick(scriptCharacters[script]);
    else if (r < 0.90) s += pick(others);
    else if (r < 0.94) s += pick(words[pick(scripts)]);
    else if (r < 0.97) s += pick(hangul);
    else s += pick(otherComplexContext);
  }

  return s;
}

function generateCharacters() {
  let s = '';
  const n = 1 + Math.floor(random() * 14);
  for (let i = 0; i < n; i++) {
    const r = random();
    s += r < 0.3 ? pick(allCharacters) : r < 0.8 ? pick(specialCharacters) : pick(ascii);
  }

  if (random() < 0.05) s += '\uD800';
  return s;
}

// '%XXXX' is a UTF-16 code unit: the lone surrogates (the JSON readers reject them) and '%'
function encode(s) {
  return s.replace(/[\uD800-\uDBFF](?![\uDC00-\uDFFF])|(?<![\uD800-\uDBFF])[\uDC00-\uDFFF]|%/g, c => '%' + c.charCodeAt(0).toString(16).toUpperCase().padStart(4, '0'));
}

const segmenter = new Intl.Segmenter('en', { granularity: 'word' });
const lines = [];
for (let i = 0; i < count; i++) {
  const text = random() < 0.75 ? generateWords() : generateCharacters();

  // The length of each segment, and 1 when it is word-like
  const result = [];
  for (const segment of segmenter.segment(text)) {
    result.push(segment.segment.length, segment.isWordLike ? 1 : 0);
  }

  lines.push(JSON.stringify({ s: encode(text), r: result }));
}

fs.writeFileSync(output, lines.join('\n') + '\n');
