// Parses each file of JSONTestSuite's test_parsing directory with the JSON.parse of V8, and writes the results as JSON lines.
// Usage: node JSONTestSuiteV8.generate.js <test_parsing directory> <output>
'use strict';
const fs = require('fs');
const path = require('path');

const directory = process.argv[2];
const output = process.argv[3] || 'JSONTestSuiteV8.jsonl';

// The files are bytes: decode them as a host does (the UTF-8 decode of the Encoding Standard: a leading BOM is removed,
// the invalid sequences become U+FFFD)
const decoder = new TextDecoder('utf-8');

// JSON.stringify writes null for the infinite numbers and 0 for -0: write them as { number: '...' }
const replacer = (key, value) => typeof value === 'number' && (!Number.isFinite(value) || Object.is(value, -0)) ? { number: Object.is(value, -0) ? '-0' : String(value) } : value;
const lines = [];
for (const name of fs.readdirSync(directory).filter(name => name.endsWith('.json')).sort()) {
  const text = decoder.decode(fs.readFileSync(path.join(directory, name)));
  let value;
  try {
    value = JSON.parse(text);
  } catch (e) {
    if (!(e instanceof SyntaxError)) {
      throw e;
    }

    lines.push(JSON.stringify({ f: name, e: true }));
    continue;
  }

  // JSON.stringify escapes the lone surrogates, so the result is a well-formed string
  lines.push(JSON.stringify({ f: name, r: JSON.stringify(value, replacer) }));
}

fs.writeFileSync(output, lines.join('\n') + '\n');
