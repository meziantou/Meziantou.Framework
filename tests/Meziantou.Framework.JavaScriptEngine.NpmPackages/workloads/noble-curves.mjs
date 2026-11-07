// @noble/curves: elliptic curve cryptography with BigInt (ed25519, x25519, secp256k1 with recovery, P-256, P-384,
// schnorr) and deterministic signatures (RFC 6979), from fixed private keys
import { ed25519, x25519 } from "@noble/curves/ed25519.js";
import { secp256k1, schnorr } from "@noble/curves/secp256k1.js";
import { p256, p384 } from "@noble/curves/nist.js";
import { bytesToHex, hexToBytes } from "@noble/curves/utils.js";
import { sha256 } from "@noble/hashes/sha2.js";

export default () => {
  const message = new TextEncoder().encode("message to sign ✓");
  const hash = sha256(message);
  const key = hexToBytes("9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60");
  const results = [];

  const edPublic = ed25519.getPublicKey(key);
  const edSignature = ed25519.sign(message, key);
  results.push("ed25519 " + bytesToHex(edPublic) + " " + bytesToHex(edSignature) + " " + ed25519.verify(edSignature, message, edPublic) + " " + ed25519.verify(edSignature, new Uint8Array([1]), edPublic));
  results.push("x25519 " + bytesToHex(x25519.getSharedSecret(key, x25519.getPublicKey(hexToBytes("77076d0a7318a57d3c16c17251b26645df4c2f87ebc0992ab177fba51db92c2a")))));

  for (const [name, curve] of [["secp256k1", secp256k1], ["p256", p256], ["p384", p384]]) {
    const privateKey = curve === p384 ? hexToBytes("6b9d3dad2e1b8c1c05b19875b6659f4de23c3b667bf297ba9aa47740787137d896d5724e4c70a825f872c9ea60d2edf5") : hexToBytes("c9afa9d845ba75166b5c215767b1d6934e50c3db36e89b127b8a622b120f6721");
    const publicKey = curve.getPublicKey(privateKey);
    const signature = curve.sign(hash, privateKey, { prehash: false, format: "recovered" });
    const compact = signature.slice(1);
    results.push(`${name} ${bytesToHex(publicKey)} ${bytesToHex(signature)} ${curve.verify(compact, hash, publicKey, { prehash: false })}`);
    const recovered = curve.Signature.fromBytes(signature, "recovered").recoverPublicKey(hash).toBytes();
    results.push(`${name} recovered ${bytesToHex(recovered) === bytesToHex(publicKey)}`);
  }

  const schnorrKey = hexToBytes("b7e151628aed2a6abf7158809cf4f3c762e7160f38b4da56a784d9045190cfef");
  const auxRand = hexToBytes("0000000000000000000000000000000000000000000000000000000000000001");
  const schnorrSignature = schnorr.sign(hash, schnorrKey, auxRand);
  results.push("schnorr " + bytesToHex(schnorr.getPublicKey(schnorrKey)) + " " + bytesToHex(schnorrSignature) + " " + schnorr.verify(schnorrSignature, hash, schnorr.getPublicKey(schnorrKey)));
  const point = secp256k1.Point.BASE.multiply(123456789n).add(secp256k1.Point.BASE.double());
  results.push("point " + point.toAffine().x.toString(16) + " " + point.toAffine().y.toString(16));
  results.push("shared " + bytesToHex(secp256k1.getSharedSecret(hexToBytes("c9afa9d845ba75166b5c215767b1d6934e50c3db36e89b127b8a622b120f6721"), secp256k1.getPublicKey(schnorrKey))));
  return results.join("\n");
};
