// fflate: deflate, gzip, zlib and ZIP archives (synchronous API), with and without compression, and unzip with filters
import { deflateSync, inflateSync, gzipSync, gunzipSync, zlibSync, unzlibSync, zipSync, unzipSync, strToU8, strFromU8, Zip, ZipDeflate, Unzip, UnzipInflate } from "fflate";

const text = Array.from({ length: 300 }, (_, index) => `${index}: lorem ipsum dolor sit amet ${"✓".repeat(index % 5)}`).join("\n");
const hex = bytes => Array.from(bytes.subarray(0, 24), byte => byte.toString(16).padStart(2, "0")).join("");

export default () => {
  const data = strToU8(text);
  const results = [];
  for (const level of [0, 1, 6, 9]) {
    const compressed = deflateSync(data, { level, mem: 8 });
    results.push(`deflate ${level}: ${compressed.length} ${hex(compressed)} ${strFromU8(inflateSync(compressed)) === text}`);
  }

  const gz = gzipSync(data, { mtime: 0, filename: "a.txt" });
  results.push(`gzip ${gz.length} ${hex(gz)} ${strFromU8(gunzipSync(gz)).length}`);
  const zl = zlibSync(data, { level: 9 });
  results.push(`zlib ${zl.length} ${hex(zl)} ${unzlibSync(zl).length}`);

  const zip = zipSync({
    "readme.txt": strToU8("Hello ZIP"),
    "data/big.txt": [data, { level: 9 }],
    "data/stored.bin": [new Uint8Array([1, 2, 3, 4]), { level: 0 }],
    "empty/": {},
  }, { mtime: new Date(Date.UTC(2020, 0, 1)) });
  results.push(`zip ${zip.length} ${hex(zip)}`);
  const unzipped = unzipSync(zip, { filter: file => !file.name.endsWith(".bin") });
  results.push(Object.entries(unzipped).map(([name, content]) => `${name} ${content.length}`).join(", "));

  const chunks = [];
  const streaming = new Zip((error, chunk, final) => { if (error) throw error; chunks.push(chunk); if (final) results.push(`streamed zip ${chunks.reduce((sum, item) => sum + item.length, 0)}`); });
  const entry = new ZipDeflate("stream.txt", { level: 6 });
  entry.mtime = new Date(Date.UTC(2020, 0, 1));
  streaming.add(entry);
  entry.push(data.subarray(0, 1000));
  entry.push(data.subarray(1000), true);
  streaming.end();

  const names = [];
  const unzip = new Unzip(file => { names.push(file.name + " " + file.compression); const parts = []; file.ondata = (error, chunk, final) => { parts.push(chunk); if (final) names.push(file.name + " -> " + parts.reduce((sum, part) => sum + part.length, 0)); }; file.start(); });
  unzip.register(UnzipInflate);
  const joined = new Uint8Array(chunks.reduce((sum, item) => sum + item.length, 0));
  let offset = 0;
  for (const chunk of chunks) {
    joined.set(chunk, offset);
    offset += chunk.length;
  }
  unzip.push(joined, true);
  results.push(names.join(", "));
  return results.join("\n");
};
