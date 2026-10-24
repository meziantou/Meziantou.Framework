// @noble/hashes: SHA-2, SHA-3, Keccak, BLAKE2, BLAKE3, RIPEMD-160, HMAC, HKDF, PBKDF2 and scrypt in typed arrays and
// 32-bit and 64-bit arithmetic
import { sha256, sha384, sha512, sha512_256 } from "@noble/hashes/sha2.js";
import { sha3_256, sha3_512, keccak_256, shake128 } from "@noble/hashes/sha3.js";
import { blake2b, blake2s } from "@noble/hashes/blake2.js";
import { blake3 } from "@noble/hashes/blake3.js";
import { ripemd160 } from "@noble/hashes/legacy.js";
import { hmac } from "@noble/hashes/hmac.js";
import { hkdf } from "@noble/hashes/hkdf.js";
import { pbkdf2 } from "@noble/hashes/pbkdf2.js";
import { scrypt } from "@noble/hashes/scrypt.js";
import { bytesToHex, utf8ToBytes } from "@noble/hashes/utils.js";

export default () => {
  const message = utf8ToBytes("The quick brown fox jumps over the lazy dog ✓");
  const big = new Uint8Array(10000).map((_, index) => (index * 31) & 0xFF);
  const hashes = { sha256, sha384, sha512, sha512_256, sha3_256, sha3_512, keccak_256, blake2b, blake2s, blake3, ripemd160 };
  const results = Object.entries(hashes).map(([name, hash]) => `${name} ${bytesToHex(hash(message))} ${bytesToHex(hash(big)).slice(0, 16)}`);
  results.push("shake128 " + bytesToHex(shake128(message, { dkLen: 40 })));
  results.push("blake2b-key " + bytesToHex(blake2b(message, { key: utf8ToBytes("key"), dkLen: 32, personalization: utf8ToBytes("personal16bytes!") })));
  results.push("blake3-derive " + bytesToHex(blake3(message, { context: utf8ToBytes("ctx"), dkLen: 64 })));
  results.push("hmac " + bytesToHex(hmac(sha256, utf8ToBytes("key"), message)));
  results.push("hkdf " + bytesToHex(hkdf(sha256, utf8ToBytes("input key"), utf8ToBytes("salt"), utf8ToBytes("info"), 42)));
  results.push("pbkdf2 " + bytesToHex(pbkdf2(sha256, "password", "salt", { c: 200, dkLen: 32 })));
  results.push("scrypt " + bytesToHex(scrypt("password", "salt", { N: 64, r: 8, p: 1, dkLen: 32 })));
  const incremental = sha256.create();
  for (let i = 0; i < big.length; i += 77)
    incremental.update(big.subarray(i, i + 777));
  results.push("incremental " + (bytesToHex(incremental.digest()) === bytesToHex(sha256(big))));
  return results.join("\n");
};
