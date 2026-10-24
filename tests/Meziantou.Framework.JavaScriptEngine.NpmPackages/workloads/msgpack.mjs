// @msgpack/msgpack: encoding and decoding of all the types of MessagePack, extension types and timestamps
import { encode, decode, decodeMulti, ExtensionCodec, EXT_TIMESTAMP } from "@msgpack/msgpack";

const extensionCodec = new ExtensionCodec();
extensionCodec.register({
  type: 1,
  encode: value => value instanceof Set ? encode([...value]) : null,
  decode: data => new Set(decode(data)),
});

const value = {
  nil: null, t: true, f: false,
  ints: [0, 1, -1, 127, 128, -32, -33, 255, 256, 65535, 65536, -2147483648, 4294967296, Number.MAX_SAFE_INTEGER, -Number.MAX_SAFE_INTEGER],
  floats: [0.5, -1.25, 3.141592653589793, 1e300],
  strings: ["", "a", "é", "日本語", "😀", "x".repeat(40), "y".repeat(300)],
  binary: new Uint8Array([1, 2, 3, 255]),
  nested: { a: [1, [2, [3, { b: "c" }]]] },
  date: new Date(Date.UTC(2020, 0, 2, 3, 4, 5, 678)),
  set: new Set([1, "two"]),
  big: 2n ** 63n - 1n,
};

const hex = bytes => Array.from(bytes, byte => byte.toString(16).padStart(2, "0")).join("");

export default () => {
  const results = [];
  const bytes = encode(value, { extensionCodec, useBigInt64: true });
  results.push(hex(bytes));
  const decoded = decode(bytes, { extensionCodec, useBigInt64: true });
  results.push(JSON.stringify(decoded, (key, item) => item instanceof Set ? ["Set", ...item] : item instanceof Uint8Array ? ["Uint8Array", ...item] : typeof item === "bigint" ? item.toString() : item));
  results.push(hex(encode({ b: 1, a: 2 }, { sortKeys: true })));
  results.push(JSON.stringify([...decodeMulti(new Uint8Array([...encode(1), ...encode("x"), ...encode([true])]))]));
  results.push(String(EXT_TIMESTAMP) + " " + hex(encode(new Date(-1))));
  try {
    decode(new Uint8Array([0xc1]));
  }
  catch (error) {
    results.push(error.name + ": " + error.message);
  }

  return results.join("\n----\n");
};
