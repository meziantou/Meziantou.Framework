// CryptoJS: hashes (MD5, SHA-1, SHA-2, SHA-3, RIPEMD-160), HMAC, PBKDF2, AES and TripleDES with explicit keys and IVs
// (no random bytes), encoders and padding modes
import CryptoJS from "crypto-js";

export default () => {
  const message = "The quick brown fox jumps over the lazy dog ✓";
  const key = CryptoJS.enc.Hex.parse("000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f");
  const iv = CryptoJS.enc.Hex.parse("0f0e0d0c0b0a09080706050403020100");
  const results = [];
  for (const name of ["MD5", "SHA1", "SHA224", "SHA256", "SHA384", "SHA512", "SHA3", "RIPEMD160"])
    results.push(name + " " + CryptoJS[name](message).toString());
  results.push("SHA3-256 " + CryptoJS.SHA3(message, { outputLength: 256 }).toString());
  results.push("HMAC " + CryptoJS.HmacSHA256(message, "secret").toString(CryptoJS.enc.Base64));
  results.push("PBKDF2 " + CryptoJS.PBKDF2("password", CryptoJS.enc.Utf8.parse("salt"), { keySize: 8, iterations: 100, hasher: CryptoJS.algo.SHA256 }).toString());

  for (const mode of ["CBC", "CFB", "CTR", "OFB", "ECB"]) {
    const encrypted = CryptoJS.AES.encrypt(message, key, { iv, mode: CryptoJS.mode[mode], padding: mode === "CBC" || mode === "ECB" ? CryptoJS.pad.Pkcs7 : CryptoJS.pad.NoPadding });
    const decrypted = CryptoJS.AES.decrypt(encrypted, key, { iv, mode: CryptoJS.mode[mode], padding: mode === "CBC" || mode === "ECB" ? CryptoJS.pad.Pkcs7 : CryptoJS.pad.NoPadding }).toString(CryptoJS.enc.Utf8);
    results.push(`AES-${mode} ${encrypted.ciphertext.toString()} ${decrypted === message}`);
  }

  const tripleDes = CryptoJS.TripleDES.encrypt("data", CryptoJS.enc.Hex.parse("0123456789abcdeffedcba98765432100123456789abcdef"), { iv: CryptoJS.enc.Hex.parse("0001020304050607") });
  results.push("TripleDES " + tripleDes.toString());
  results.push("RC4 " + CryptoJS.RC4.encrypt("data", CryptoJS.enc.Utf8.parse("key")).ciphertext.toString());
  const progressive = CryptoJS.algo.SHA256.create();
  for (const part of ["a", "b", "c"].map(letter => letter.repeat(100)))
    progressive.update(part);
  results.push("progressive " + progressive.finalize().toString());
  results.push("encoders " + [CryptoJS.enc.Base64.stringify(CryptoJS.enc.Utf8.parse(message)), CryptoJS.enc.Utf16.stringify(CryptoJS.enc.Utf8.parse("ab")), CryptoJS.enc.Latin1.parse("\u00ff").toString(), CryptoJS.enc.Base64url.stringify(CryptoJS.enc.Hex.parse("fbff"))].join(" "));
  return results.join("\n");
};
