// pako: zlib, raw deflate and gzip (compression levels, strategies, dictionaries, streaming chunks) and inflate
import { deflate, deflateRaw, gzip, inflate, inflateRaw, ungzip, Deflate, Inflate } from "pako";

// The strategies of zlib (pako does not export them)
const strategies = { Z_FILTERED: 1, Z_HUFFMAN_ONLY: 2, Z_RLE: 3, Z_FIXED: 4 };

const text = Array.from({ length: 60 }, (_, index) => `Line ${index}: The quick brown fox jumps over the lazy dog. ${"abc".repeat(index % 7)} ✓ ü 日本`).join("\n");
const bytes = new TextEncoder().encode(text);

const hash = data => {
  let value = 0x811c9dc5;
  for (const byte of data) {
    value ^= byte;
    value = Math.imul(value, 0x01000193) >>> 0;
  }
  return value.toString(16);
};

export default () => {
  const results = [];
  for (const level of [0, 1, 6, 9]) {
    const compressed = deflate(bytes, { level });
    results.push(`level ${level}: ${compressed.length} bytes, hash ${hash(compressed)}, round trip ${new TextDecoder().decode(inflate(compressed)) === text}`);
  }

  for (const strategy of Object.values(strategies)) {
    const compressed = deflateRaw(bytes, { strategy, windowBits: 10, memLevel: 4 });
    results.push(`strategy ${strategy}: ${compressed.length} ${hash(compressed)} ${inflateRaw(compressed, { windowBits: 10 }).length}`);
  }

  const zipped = gzip(text, { header: { name: "file.txt", mtime: 0, os: 3, comment: "c" } });
  results.push(`gzip ${zipped.length} ${hash(zipped)} ${ungzip(zipped, { to: "string" }).length}`);

  const dictionary = new TextEncoder().encode("The quick brown fox jumps over the lazy dog.");
  const withDictionary = deflate(bytes.subarray(0, 500), { dictionary });
  results.push(`dictionary ${withDictionary.length} ${new TextDecoder().decode(inflate(withDictionary, { dictionary })) === text.slice(0, new TextDecoder().decode(bytes.subarray(0, 500)).length)}`);

  const streaming = new Deflate({ level: 9 });
  for (let i = 0; i < bytes.length; i += 1000)
    streaming.push(bytes.subarray(i, i + 1000), i + 1000 >= bytes.length);
  const inflater = new Inflate();
  for (let i = 0; i < streaming.result.length; i += 333)
    inflater.push(streaming.result.subarray(i, i + 333));
  results.push(`streaming ${streaming.result.length} ${hash(streaming.result)} ${inflater.result.length} ${inflater.err}`);

  const corrupted = deflate(bytes).slice();
  corrupted[50] ^= 0xFF;
  const failing = new Inflate();
  failing.push(corrupted);
  results.push(`corrupted ${failing.err} ${failing.msg}`);
  return results.join("\n");
};
