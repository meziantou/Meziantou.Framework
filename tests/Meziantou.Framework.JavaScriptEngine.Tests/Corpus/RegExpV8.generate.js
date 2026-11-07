// Generates random patterns and inputs, runs them in V8, and writes the results as JSON lines.
// Usage: node gen.js <seed> <count> <output>
'use strict';
const fs = require('fs');

let seed = Number(process.argv[2] || 1) >>> 0;
const count = Number(process.argv[3] || 1000);
const output = process.argv[4] || 'corpus.jsonl';

function random() {
  // mulberry32
  seed = (seed + 0x6D2B79F5) >>> 0;
  let t = seed;
  t = Math.imul(t ^ (t >>> 15), t | 1);
  t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
  return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
}

function pick(items) { return items[Math.floor(random() * items.length)]; }
function chance(p) { return random() < p; }

const literalChars = ['a', 'a', 'b', 'b', 'c', 'A', 'B', 'C', '0', '1', '_', ' ', '-', 'k', 's', 'S', 'K',
  '\u017F', '\u212A', '\u00DF', '\u1E9E', '\u03C3', '\u03C2', '\u03A3', '\u00E9', '\u00C9', '\uD83D\uDE00', '\u0130', 'i', 'I', '\u0131'];
const escapes = ['\\d', '\\D', '\\w', '\\W', '\\s', '\\S', '.', '\\n', '\\t', '\\x41', '\\u0062', '\\u{1F600}', '\\uD83D', '\\uDE00', '\\0', '\\cA', '\\-', '\\.', '\\*', '\\/', '\\8', '\\c', '\\k', '\\q', '\\a', '\\u{12}'];
const assertions = ['^', '$', '\\b', '\\B'];
const classItems = ['a', 'b', 'c', 'a-c', 'A-C', 'a-z', '0-9', '\\d', '\\w', '\\s', '\\W', '\\D', '-', '\\-', '\u017F', '\u212A', 'k', 'K', '\u00DF', '\u03C3', '\uD83D\uDE00', '\\u{1F600}', '\\uD83D', '\\b', '_', ' ', '.', '^', '\\c1', '\\d-z', 'x-\\d'];
const vItems = ['\\q{ab|c}', '\\q{abc|}', '\\q{a|bc}', '[a-c]', '[^a]', '[\\w--\\d]', '[a-z&&[aeiou]]', '\\p{Lu}', '\\p{ASCII}', '\\p{RGI_Emoji_Flag_Sequence}', '\\q{\\u{1F600}a}'];
const properties = ['\\p{L}', '\\p{Lu}', '\\P{Ll}', '\\p{Script=Latin}', '\\p{sc=Grek}', '\\p{scx=Grek}', '\\p{ASCII}', '\\p{Any}', '\\p{Alphabetic}', '\\P{White_Space}', '\\p{Emoji_Presentation}', '\\p{Nd}', '\\p{Lowercase}', '\\p{Changes_When_Casefolded}'];

let groupCount = 0;
let names = [];

function genAtom(depth, mode) {
  const r = random();
  if (r < 0.35) return pick(literalChars);
  if (r < 0.5) return pick(escapes);
  if (r < 0.58) return pick(assertions);
  if (r < 0.7) return genClass(mode);
  if (r < 0.75 && mode !== '') return pick(properties);
  if (depth > 3) return pick(literalChars);
  const g = random();
  if (g < 0.3) { groupCount++; return '(' + genDisjunction(depth + 1, mode) + ')'; }
  if (g < 0.45) {
    groupCount++;
    const name = 'n' + (names.length + 1);
    names.push(name);
    return '(?<' + name + '>' + genDisjunction(depth + 1, mode) + ')';
  }
  if (g < 0.6) return '(?:' + genDisjunction(depth + 1, mode) + ')';
  if (g < 0.7) return pick(['(?=', '(?!', '(?<=', '(?<!']) + genDisjunction(depth + 1, mode) + ')';
  if (g < 0.8) return '(?' + pick(['i', '-i', 'm', 's', 'i-s', '-m', 'is']) + ':' + genDisjunction(depth + 1, mode) + ')';
  if (g < 0.9 && groupCount > 0) return '\\' + (1 + Math.floor(random() * (groupCount + 1)));
  if (names.length > 0) return '\\k<' + pick(names) + '>';
  return pick(literalChars);
}

function genClass(mode) {
  let s = '[';
  if (chance(0.25)) s += '^';
  const n = Math.floor(random() * 4);
  for (let i = 0; i < n; i++) {
    if (mode === 'v' && chance(0.3)) s += pick(vItems);
    else s += pick(classItems);
  }
  if (mode === 'v' && chance(0.2)) {
    return '[' + pick(vItems) + pick(['--', '&&']) + pick(vItems) + ']';
  }
  return s + ']';
}

function genQuantifier() {
  const q = pick(['*', '+', '?', '{2}', '{0,2}', '{1,}', '{0}', '{1,3}', '{2,}']);
  return chance(0.3) ? q + '?' : q;
}

function genTerm(depth, mode) {
  let atom = genAtom(depth, mode);
  if (chance(0.3)) atom += genQuantifier();
  return atom;
}

function genAlternative(depth, mode) {
  let s = '';
  const n = Math.floor(random() * 4) + (depth === 0 ? 1 : 0);
  for (let i = 0; i < n; i++) s += genTerm(depth, mode);
  return s;
}

function genDisjunction(depth, mode) {
  let s = genAlternative(depth, mode);
  while (chance(0.25)) s += '|' + genAlternative(depth, mode);
  return s;
}

const inputChars = ['a', 'a', 'a', 'b', 'b', 'c', 'A', 'B', 'C', '0', '1', '_', ' ', '-', '\n', 'k', 'K', 's', 'S',
  '\u017F', '\u212A', '\u00DF', '\u1E9E', '\u03C3', '\u03C2', '\u03A3', '\u00E9', '\u00C9', '\uD83D\uDE00', '\uD83D', '\uDE00',
  '\uD83C\uDDEB\uD83C\uDDF7', '\u0130', 'i', 'I', '\u0131', '.', '*', '\u2028', '\u0001', '\u00A0'];

function genInput() {
  let s = '';
  const n = Math.floor(random() * 10);
  for (let i = 0; i < n; i++) s += pick(inputChars);
  return s;
}

// The printable ASCII characters but %, then %XXXX for the other UTF-16 code units (lone surrogates included)
function hex(s) {
  let r = '';
  for (let i = 0; i < s.length; i++) {
    const c = s.charCodeAt(i);
    r += c >= 0x20 && c < 0x7F && c !== 0x25 ? s[i] : '%' + c.toString(16).toUpperCase().padStart(4, '0');
  }
  return r;
}

const lines = [];
const seen = new Set();
while (lines.length < count) {
  groupCount = 0;
  names = [];
  const mode = pick(['', '', 'u', 'v']);
  let flags = '';
  for (const f of ['d', 'g', 'i', 'm', 's']) if (chance(f === 'i' ? 0.4 : 0.15)) flags += f;
  flags += mode;
  if (chance(0.15)) flags += 'y';
  let pattern = genDisjunction(0, mode);
  if (chance(0.05)) {
    // Duplicate named groups in different alternatives
    names.push('d');
    pattern = '(?:(?<d>' + genAlternative(2, mode) + ')|(?<d>' + genAlternative(2, mode) + '))' + pattern + (chance(0.5) ? '\\k<d>' : '');
  }
  const key = pattern + '/' + flags;
  if (seen.has(key)) continue;
  seen.add(key);
  // V8 differs from the specification under "v" and "i" (it folds \p{General_Category}, does not fold \p{ASCII} nor the
  // one-character \q{} strings, and an empty modifiers group changes the flags after it): see RegExpV8.md
  if (flags.includes('v') && flags.includes('i') && /\\[pPq]\{|\(\?-?[ims]*-?[ims]*:\)/.test(pattern)) continue;
  // V8 applies the "i" of a modifiers group to the rest of a "u" or "v" pattern, or ignores it
  if (mode !== '' && /\(\?-?[ims]+-?[ims]*:/.test(pattern)) continue;
  let re;
  try {
    re = new RegExp(pattern, flags);
  } catch (e) {
    lines.push(JSON.stringify({ p: hex(pattern), f: flags, e: true }));
    continue;
  }
  const inputs = [];
  for (let k = 0; k < 3; k++) {
    const input = genInput();
    let lastIndex = chance(0.7) ? 0 : Math.floor(random() * (input.length + 2));
    // V8 is not consistent when lastIndex is inside a surrogate pair under "u" or "v"
    if (mode !== '' && lastIndex > 0 && lastIndex < input.length && /[\uDC00-\uDFFF]/.test(input[lastIndex]) && /[\uD800-\uDBFF]/.test(input[lastIndex - 1])) lastIndex = 0;
    re = new RegExp(pattern, flags);
    re.lastIndex = lastIndex;
    const m = re.exec(input);
    // V8 can match inside a surrogate pair under "u" or "v" (\B for instance)
    if (m && mode !== '' && m.index > 0 && m.index < input.length && /[\uDC00-\uDFFF]/.test(input[m.index]) && /[\uD800-\uDBFF]/.test(input[m.index - 1])) continue;
    let result = null;
    if (m) {
      result = [m.index];
      for (let i = 0; i < m.length; i++) result.push(m[i] === undefined ? null : hex(m[i]));
      result.push(re.lastIndex);
    }
    inputs.push({ s: hex(input), i: lastIndex, r: result });
  }
  lines.push(JSON.stringify({ p: hex(pattern), f: flags, t: inputs }));
}

fs.writeFileSync(output, lines.join('\n') + '\n');
console.log('wrote ' + lines.length + ' lines to ' + output);
