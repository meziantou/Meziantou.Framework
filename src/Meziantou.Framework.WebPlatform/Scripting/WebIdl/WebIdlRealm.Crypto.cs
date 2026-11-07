using System.Security.Cryptography;
using Meziantou.Framework.Hashing;
using Meziantou.Framework.WebPlatform.Cryptography;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Scripting;

// The Web Cryptography API (https://w3c.github.io/webcrypto/), implemented in JavaScript (WebCrypto.js): the algorithm
// normalization converts the dictionaries to a type that depends on the algorithm name, and the attributes of CryptoKey
// return cached objects. The cryptographic primitives and the ASN.1 structures are implemented in .NET (Cryptography/) and
// given to the script as hooks. The script is evaluated in a realm when crypto or one of its interfaces is first used.
public sealed partial class WebIdlRealm<TAdapter, TValue>
{
    private static string? s_webCryptoScript;
    private TValue? _webCrypto;

    /// <summary>
    /// Defines the crypto attribute of the global object (https://w3c.github.io/webcrypto/#crypto-interface: a member of the
    /// WindowOrWorkerGlobalScope mixin) and the interface objects (SubtleCrypto and CryptoKey are [SecureContext]).
    /// </summary>
    private void DefineCryptoGlobals()
    {
        foreach (var name in (ReadOnlySpan<string>)["Crypto", "CryptoKey", "SubtleCrypto"])
        {
            if (name != "Crypto" && !IsSecureContext)
                continue;

            var interfaceName = name;
            Adapter.DefineLazyDataProperty(Global, Key(interfaceName), () => Adapter.Get(GetWebCrypto(), Key(interfaceName)), writable: true, enumerable: false, configurable: true);
        }

        // [SameObject] readonly attribute Crypto crypto: the members of Window ([Global]) are on the global object, the ones of
        // WorkerGlobalScope on its prototype
        DefineAttribute(Window is null ? GetPrototype("WorkerGlobalScope") : Global, "crypto", static (realm, thisValue) =>
        {
            var global = realm.ThisGlobalScope(thisValue);
            var targetRealm = ReferenceEquals(global, realm.GlobalScope) ? realm : realm.Agent.TryGetRealm(global) ?? realm;
            return targetRealm.Adapter.Get(targetRealm.GetWebCrypto(), targetRealm.Key("crypto"));
        }, setter: null);
    }

    /// <summary>Returns the object returned by WebCrypto.js in this realm: the interface objects, crypto and the serialization functions.</summary>
    private TValue GetWebCrypto()
    {
        if (_webCrypto is { } webCrypto)
            return webCrypto;

        return Adapter.RunInRealm(EngineRealm, () =>
        {
            if (_webCrypto is { } existing)
                return existing;

            var script = s_webCryptoScript ??= LoadEmbeddedScript("Meziantou.Framework.WebPlatform.Scripting.WebIdl.WebCrypto.js");
            var adapter = Adapter;
            var interfaces = CreateObject();
            foreach (var name in (ReadOnlySpan<string>)["DOMException", "QuotaExceededError"])
            {
                Adapter.Set(interfaces, Key(name), GetInterfaceObject(name));
            }

            var hooks = CreateObject();
            Adapter.Set(hooks, Key("interfaces"), interfaces);
            Adapter.Set(hooks, Key("internals"), Agent.CryptoInternals ??= EvaluateBuiltIn("new WeakMap()"));
            Adapter.Set(hooks, Key("SharedArrayBuffer"), SharedArrayBufferConstructor ?? Adapter.Undefined);
            Adapter.Set(hooks, Key("isSecureContext"), Adapter.FromBoolean(IsSecureContext));

            // The .NET exceptions (e.g. the DataError of an invalid ASN.1 structure) are thrown as JavaScript exceptions
            void Define(string name, int length, JsHostFunction<TValue> function) => Adapter.Set(hooks, Key(name), adapter.CreateFunction(name, length, (thisValue, arguments) =>
            {
                try
                {
                    return function(thisValue, arguments);
                }
                catch (Exception ex) when (!adapter.TryGetThrownValue(ex, out TValue? _))
                {
                    throw ToJsException(ex switch
                    {
                        WebCryptoException cryptoException => new DomException(ex.Message, cryptoException.Name),
                        CryptographicException or ArgumentException or OutOfMemoryException => new DomException(ex.Message, "OperationError"),
                        NotSupportedException => new DomException(ex.Message, "NotSupportedError"),
                        _ => ex,
                    });
                }
            }));

            Define("queueTask", 1, (_, args) =>
            {
                var callback = args[0];
                GlobalScope.QueueGlobalTask(() => adapter.Call(callback, adapter.Undefined, []));
                return adapter.Undefined;
            });
            Define("utf8Decode", 1, (_, args) => Adapter.FromString(System.Text.Encoding.UTF8.GetString(CryptoBytes(args[0]))));
            Define("randomBytes", 1, (_, args) => CryptoResult(RandomNumberGenerator.GetBytes((int)Adapter.ToNumber(args[0]))));
            Define("fillRandom", 1, (_, args) =>
            {
                var view = TryToBufferSource(args[0], uint.MaxValue, allowShared: false) ?? throw CreateTypeError("The value is not an ArrayBufferView");
                RandomNumberGenerator.Fill(view.Bytes.Span);
                return adapter.Undefined;
            });

            // SHA (https://w3c.github.io/webcrypto/#sha), HMAC, HKDF and PBKDF2
            Define("digest", 2, (_, args) => CryptoResult(Hashes.Digest(Adapter.ToString(args[0]), CryptoBytes(args[1]))));
            Define("hmac", 3, (_, args) => CryptoResult(Hashes.Hmac(Adapter.ToString(args[0]), CryptoBytes(args[1]), CryptoBytes(args[2]))));
            Define("hkdf", 5, (_, args) => CryptoResult(Hashes.Hkdf(Adapter.ToString(args[0]), CryptoBytes(args[1]), CryptoBytes(args[2]), CryptoBytes(args[3]), (int)Adapter.ToNumber(args[4]))));
            Define("pbkdf2", 5, (_, args) => CryptoResult(Hashes.Pbkdf2(Adapter.ToString(args[0]), CryptoBytes(args[1]), CryptoBytes(args[2]), Adapter.ToNumber(args[3]), (int)Adapter.ToNumber(args[4]))));

            // AES
            Define("aesCbc", 4, (_, args) => CryptoResultOrNull(Adapter.ToBoolean(args[0])
                ? AesModes.CbcEncrypt(CryptoBytes(args[1]), CryptoBytes(args[2]), CryptoBytes(args[3]))
                : AesModes.CbcDecrypt(CryptoBytes(args[1]), CryptoBytes(args[2]), CryptoBytes(args[3]))));
            Define("aesCtr", 4, (_, args) => CryptoResult(AesModes.Ctr(CryptoBytes(args[0]), CryptoBytes(args[1]), (int)Adapter.ToNumber(args[2]), CryptoBytes(args[3]))));
            Define("aesGcm", 6, (_, args) => CryptoResultOrNull(Adapter.ToBoolean(args[0])
                ? AesModes.GcmEncrypt(CryptoBytes(args[1]), CryptoBytes(args[2]), CryptoBytes(args[3]), (int)Adapter.ToNumber(args[4]), CryptoBytes(args[5]))
                : AesModes.GcmDecrypt(CryptoBytes(args[1]), CryptoBytes(args[2]), CryptoBytes(args[3]), (int)Adapter.ToNumber(args[4]), CryptoBytes(args[5]))));
            Define("aesKeyWrap", 3, (_, args) => CryptoResultOrNull(Adapter.ToBoolean(args[0])
                ? AesKeyWrap.Wrap(CryptoBytes(args[1]), CryptoBytes(args[2]))
                : AesKeyWrap.Unwrap(CryptoBytes(args[1]), CryptoBytes(args[2]))));

            // RSA (https://w3c.github.io/webcrypto/#rsassa-pkcs1): the handles are { n, e, d, p, q, dp, dq, qi }
            Define("rsaGenerate", 2, (_, args) => FromRsaKey(RsaKey.Generate((int)Adapter.ToNumber(args[0]), RsaKey.Os2Ip(CryptoBytes(args[1])))));
            Define("rsaImport", 2, (_, args) => Adapter.ToString(args[0]) switch
            {
                "spki" => FromRsaKey(ParseRsaSpki(CryptoBytes(args[1]))),
                _ => FromRsaKey(ParseRsaPkcs8(CryptoBytes(args[1]))),
            });
            Define("rsaFromComponents", 8, (_, args) => FromRsaKey(RsaKey.Create(CryptoBytes(args[0]), CryptoBytes(args[1]), OptionalCryptoBytes(args[2]), OptionalCryptoBytes(args[3]), OptionalCryptoBytes(args[4]), OptionalCryptoBytes(args[5]), OptionalCryptoBytes(args[6]), OptionalCryptoBytes(args[7]))));
            Define("rsaExport", 2, (_, args) =>
            {
                var key = ToRsaKey(args[1]);
                return CryptoResult(Adapter.ToString(args[0]) == "spki"
                    ? KeyEncodings.EncodeSubjectPublicKeyInfo(KeyEncodings.RsaEncryption, KeyEncodings.EncodeNull(), key.EncodeRsaPublicKey())
                    : KeyEncodings.EncodePrivateKeyInfo(KeyEncodings.RsaEncryption, KeyEncodings.EncodeNull(), key.EncodeRsaPrivateKey()));
            });
            Define("rsaSign", 5, (_, args) =>
            {
                var key = ToRsaKey(args[2]);
                var hash = Adapter.ToString(args[1]);
                var data = CryptoBytes(args[3]);
                return CryptoResult(Adapter.ToString(args[0]) == "pss" ? key.SignPss(hash, data, (int)Adapter.ToNumber(args[4])) : key.SignPkcs1(hash, data));
            });
            Define("rsaVerify", 6, (_, args) =>
            {
                var key = ToRsaKey(args[2]);
                var hash = Adapter.ToString(args[1]);
                var data = CryptoBytes(args[3]);
                var signature = CryptoBytes(args[4]);
                return Adapter.FromBoolean(Adapter.ToString(args[0]) == "pss" ? key.VerifyPss(hash, data, signature, (int)Adapter.ToNumber(args[5])) : key.VerifyPkcs1(hash, data, signature));
            });
            Define("rsaOaep", 5, (_, args) =>
            {
                var key = ToRsaKey(args[2]);
                var hash = Adapter.ToString(args[1]);
                return CryptoResult(Adapter.ToBoolean(args[0]) ? key.EncryptOaep(hash, CryptoBytes(args[3]), CryptoBytes(args[4])) : key.DecryptOaep(hash, CryptoBytes(args[3]), CryptoBytes(args[4])));
            });

            // ECDSA and ECDH (https://w3c.github.io/webcrypto/#ecdsa): the handles are { crv, x, y, d }
            Define("ecGenerate", 1, (_, args) => FromEcKey(EcKey.Generate(GetCurve(args[0]))));
            Define("ecImport", 3, (_, args) =>
            {
                // The curve of spki and pkcs8 comes from the parameters: an unsupported curve is null
                var data = CryptoBytes(args[2]);
                var key = Adapter.ToString(args[0]) switch
                {
                    "spki" => EcKey.ParseSubjectPublicKeyInfo(KeyEncodings.ParseSubjectPublicKeyInfo(data)),
                    "pkcs8" => EcKey.ParsePrivateKeyInfo(KeyEncodings.ParsePrivateKeyInfo(data)),
                    _ => EcKey.DecodePublicPoint(GetCurve(args[1]), data),
                };
                return key is null ? Adapter.Null : FromEcKey(key);
            });
            Define("ecFromJwk", 4, (_, args) =>
            {
                var curve = GetCurve(args[0]);
                var x = CryptoBytes(args[1]);
                var y = CryptoBytes(args[2]);
                return FromEcKey(Adapter.IsUndefined(args[3]) ? EcKey.CreatePublic(curve, x, y) : EcKey.CreatePrivate(curve, ExactLength(CryptoBytes(args[3]), curve.OrderLength), ExactLength(x, curve.FieldLength), ExactLength(y, curve.FieldLength)));
            });
            Define("ecExport", 2, (_, args) =>
            {
                var key = ToEcKey(args[1]);
                return CryptoResult(Adapter.ToString(args[0]) switch
                {
                    "spki" => key.EncodeSubjectPublicKeyInfo(),
                    "pkcs8" => key.EncodePrivateKeyInfo(),
                    _ => key.EncodePublicPoint(),
                });
            });
            Define("ecSign", 3, (_, args) => CryptoResult(ToEcKey(args[1]).Sign(Adapter.ToString(args[0]), CryptoBytes(args[2]))));
            Define("ecVerify", 4, (_, args) => Adapter.FromBoolean(ToEcKey(args[1]).Verify(Adapter.ToString(args[0]), CryptoBytes(args[2]), CryptoBytes(args[3]))));
            Define("ecDerive", 2, (_, args) => CryptoResult(ToEcKey(args[0]).DeriveSharedSecret(ToEcKey(args[1]))));

            // Ed25519, X25519, Ed448 and X448: the handles are { pub, priv }
            Define("okpGenerate", 1, (_, args) =>
            {
                var algorithm = Adapter.ToString(args[0]);
                var privateKey = OkpKeys.GeneratePrivateKey(algorithm);
                return FromOkpKey(OkpKeys.GetPublicKey(algorithm, privateKey), privateKey);
            });
            Define("okpImport", 3, (_, args) =>
            {
                var algorithm = Adapter.ToString(args[0]);
                var data = CryptoBytes(args[2]);
                if (Adapter.ToString(args[1]) == "spki")
                    return FromOkpKey(OkpKeys.ParseSubjectPublicKeyInfo(algorithm, data), null);

                var privateKey = OkpKeys.ParsePrivateKeyInfo(algorithm, data);
                return FromOkpKey(OkpKeys.GetPublicKey(algorithm, privateKey), privateKey);
            });
            Define("okpPublicKey", 2, (_, args) => CryptoResult(OkpKeys.GetPublicKey(Adapter.ToString(args[0]), CryptoBytes(args[1]))));
            Define("okpExport", 3, (_, args) =>
            {
                var algorithm = Adapter.ToString(args[0]);
                return CryptoResult(Adapter.ToString(args[1]) == "spki"
                    ? OkpKeys.EncodeSubjectPublicKeyInfo(algorithm, CryptoBytes(args[2]))
                    : OkpKeys.EncodePrivateKeyInfo(algorithm, CryptoBytes(args[2])));
            });
            Define("okpSign", 4, (_, args) => CryptoResult(OkpKeys.Sign(Adapter.ToString(args[0]), CryptoBytes(args[1]), CryptoBytes(args[2]), CryptoBytes(args[3]))));
            Define("okpVerify", 5, (_, args) => Adapter.FromBoolean(OkpKeys.Verify(Adapter.ToString(args[0]), CryptoBytes(args[1]), CryptoBytes(args[2]), CryptoBytes(args[3]), CryptoBytes(args[4]))));
            Define("okpDerive", 3, (_, args) => CryptoResult(OkpKeys.DeriveSharedSecret(Adapter.ToString(args[0]), CryptoBytes(args[1]), CryptoBytes(args[2]))));

            // AES-OCB (https://wicg.github.io/webcrypto-modern-algos/#aes-ocb) and Argon2 (https://wicg.github.io/webcrypto-modern-algos/#argon2)
            Define("aesOcb", 6, (_, args) => CryptoResultOrNull(Adapter.ToBoolean(args[0])
                ? AesOcb.Encrypt(CryptoBytes(args[1]), CryptoBytes(args[2]), CryptoBytes(args[3]), CryptoBytes(args[5]), (int)Adapter.ToNumber(args[4]))
                : AesOcb.Decrypt(CryptoBytes(args[1]), CryptoBytes(args[2]), CryptoBytes(args[3]), CryptoBytes(args[5]), (int)Adapter.ToNumber(args[4]))));
            Define("argon2", 10, (_, args) => CryptoResult(Argon2.DeriveBytes(
                Adapter.ToString(args[0]) switch { "Argon2d" => Argon2Type.Argon2d, "Argon2i" => Argon2Type.Argon2i, _ => Argon2Type.Argon2id },
                CryptoBytes(args[1]), CryptoBytes(args[2]), ToInt32(args[3]), ToInt32(args[4]), ToInt32(args[5]), outputLength: ToInt32(args[9]), CryptoBytes(args[7]), CryptoBytes(args[8]), (Argon2Version)ToInt32(args[6]))));

            // SHA-3, cSHAKE, TurboSHAKE, KangarooTwelve, KMAC and ChaCha20-Poly1305 (https://wicg.github.io/webcrypto-modern-algos/)
            Define("xof", 6, (_, args) => CryptoResult(Hashes.ExtendableOutput(Adapter.ToString(args[0]), CryptoBytes(args[1]), ToInt32(args[2]), CryptoBytes(args[3]), CryptoBytes(args[4]), (byte)Adapter.ToNumber(args[5]))));
            Define("kmac", 5, (_, args) => CryptoResult(Kmac.HashData(Adapter.ToString(args[0]) == "KMAC128" ? 128 : 256, CryptoBytes(args[1]), CryptoBytes(args[2]), ToInt32(args[3]), CryptoBytes(args[4]))));
            Define("chacha20Poly1305", 5, (_, args) =>
            {
                var key = CryptoBytes(args[1]);
                var nonce = CryptoBytes(args[2]);
                var associatedData = CryptoBytes(args[3]);
                var input = CryptoBytes(args[4]);
                using var aead = new ChaCha20Poly1305(key);
                if (Adapter.ToBoolean(args[0]))
                {
                    var output = new byte[input.Length + 16];
                    aead.Encrypt(nonce, input, output.AsSpan(0, input.Length), output.AsSpan(input.Length), associatedData);
                    return CryptoResult(output);
                }

                var plaintext = new byte[input.Length - 16];
                try
                {
                    aead.Decrypt(nonce, input.AsSpan(0, plaintext.Length), input.AsSpan(plaintext.Length), plaintext, associatedData);
                }
                catch (AuthenticationTagMismatchException)
                {
                    return Adapter.Null;
                }

                return CryptoResult(plaintext);
            });

            // ML-KEM and ML-DSA: the handles are { pub, seed }
            Define("pqGenerate", 1, (_, args) =>
            {
                var algorithm = Adapter.ToString(args[0]);
                var seed = PostQuantumKeys.GenerateSeed(algorithm);
                return FromSeedKey(PostQuantumKeys.GetPublicKey(algorithm, seed), seed);
            });
            Define("pqFromSeed", 2, (_, args) =>
            {
                var algorithm = Adapter.ToString(args[0]);
                var seed = CryptoBytes(args[1]);
                if (seed.Length != PostQuantumKeys.GetSeedLength(algorithm))
                    throw WebCryptoException.DataError("The seed does not have the length of the algorithm");

                return FromSeedKey(PostQuantumKeys.GetPublicKey(algorithm, seed), seed);
            });
            Define("pqImport", 3, (_, args) =>
            {
                var algorithm = Adapter.ToString(args[0]);
                var data = CryptoBytes(args[2]);
                if (Adapter.ToString(args[1]) == "spki")
                    return FromSeedKey(PostQuantumKeys.ParseSubjectPublicKeyInfo(algorithm, data), null);

                var seed = PostQuantumKeys.ParsePrivateKeyInfo(algorithm, data);
                return FromSeedKey(PostQuantumKeys.GetPublicKey(algorithm, seed), seed);
            });
            Define("pqExport", 3, (_, args) =>
            {
                var algorithm = Adapter.ToString(args[0]);
                return CryptoResult(Adapter.ToString(args[1]) == "spki"
                    ? PostQuantumKeys.EncodeSubjectPublicKeyInfo(algorithm, CryptoBytes(args[2]))
                    : PostQuantumKeys.EncodePrivateKeyInfo(algorithm, CryptoBytes(args[2])));
            });
            Define("pqPublicKeyLength", 1, (_, args) => Adapter.FromNumber(PostQuantumKeys.GetPublicKeyLength(Adapter.ToString(args[0]))));
            Define("pqEncapsulate", 2, (_, args) =>
            {
                var (sharedKey, ciphertext) = PostQuantumKeys.Encapsulate(Adapter.ToString(args[0]), CryptoBytes(args[1]));
                return FromEncapsulation(sharedKey, ciphertext);
            });
            Define("pqDecapsulate", 3, (_, args) => CryptoResult(PostQuantumKeys.Decapsulate(Adapter.ToString(args[0]), CryptoBytes(args[1]), CryptoBytes(args[2]))));
            Define("pqSign", 4, (_, args) => CryptoResult(PostQuantumKeys.Sign(Adapter.ToString(args[0]), CryptoBytes(args[1]), CryptoBytes(args[2]), CryptoBytes(args[3]))));
            Define("pqVerify", 5, (_, args) => Adapter.FromBoolean(PostQuantumKeys.Verify(Adapter.ToString(args[0]), CryptoBytes(args[1]), CryptoBytes(args[2]), CryptoBytes(args[3]), CryptoBytes(args[4]))));

            // The hybrid KEMs (https://wicg.github.io/webcrypto-modern-algos/#hybrid-kems): the handles are { pub, seed }
            Define("hybridFromSeed", 2, (_, args) =>
            {
                var seed = CryptoBytes(args[1]);
                return FromSeedKey(HybridKem.GetEncapsulationKey(Adapter.ToString(args[0]), seed), seed);
            });
            Define("hybridPublicKeyLength", 1, (_, args) => Adapter.FromNumber(HybridKem.GetEncapsulationKeyLength(Adapter.ToString(args[0]))));
            Define("hybridEncapsulate", 2, (_, args) =>
            {
                var (sharedKey, ciphertext) = HybridKem.Encapsulate(Adapter.ToString(args[0]), CryptoBytes(args[1]));
                return FromEncapsulation(sharedKey, ciphertext);
            });
            Define("hybridDecapsulate", 3, (_, args) => CryptoResult(HybridKem.Decapsulate(Adapter.ToString(args[0]), CryptoBytes(args[1]), CryptoBytes(args[2]))));

            var result = Adapter.Call(EvaluateBuiltIn(script), Adapter.Undefined, [hooks]);
            _webCrypto = result;
            return result;
        });
    }

    /// <summary>
    /// Creates a CryptoKey in this realm from the record of a serialized key (https://w3c.github.io/webcrypto/#cryptokey-interface-serialization).
    /// CryptoKey is not exposed in the realms that are not secure contexts, where the deserialization is a DataCloneError
    /// (https://html.spec.whatwg.org/multipage/structured-data.html#structureddeserialize).
    /// </summary>
    private TValue DeserializeCryptoKey(TValue serialized)
    {
        if (!IsSecureContext)
            throw ToJsException(new DomException("A CryptoKey cannot be deserialized in a context that is not secure", "DataCloneError"));

        return Adapter.Call(Adapter.Get(GetWebCrypto(), Key("deserializeKey")), Adapter.Undefined, [serialized]);
    }

    /// <summary>The bytes of a Uint8Array created by WebCrypto.js.</summary>
    private byte[] CryptoBytes(TValue value) => TryToBufferSource(value, 1u << (int)JsBufferKind.Uint8Array, allowShared: false)?.Bytes.ToArray() ?? throw CreateTypeError("The value is not a Uint8Array");

    private TValue CryptoResult(byte[] bytes) => FromBuffer(JsBufferKind.Uint8Array, bytes);

    private byte[]? OptionalCryptoBytes(TValue value) => Adapter.IsUndefined(value) || Adapter.IsNull(value) ? null : CryptoBytes(value);

    // A number of the parameters (e.g. the memory of Argon2): the values that are not an Int32 are not supported
    private int ToInt32(TValue value)
    {
        var number = Adapter.ToNumber(value);
        if (number is < int.MinValue or > int.MaxValue)
            throw new WebCryptoException("OperationError", "The parameter is too large");

        return (int)number;
    }

    private static byte[] ExactLength(byte[] value, int length) => value.Length == length ? value : throw new WebCryptoException("DataError", "The key component does not have the expected length");

    private EcCurve GetCurve(TValue name) => EcCurve.FromName(Adapter.ToString(name)) ?? throw new WebCryptoException("NotSupportedError", "The named curve is not supported");

    private static RsaKey ParseRsaSpki(byte[] data)
    {
        var spki = KeyEncodings.ParseSubjectPublicKeyInfo(data);
        if (spki.Algorithm != KeyEncodings.RsaEncryption)
            throw new WebCryptoException("DataError", "The algorithm of the key is not rsaEncryption");

        return RsaKey.ParseRsaPublicKey(spki.PublicKey);
    }

    private static RsaKey ParseRsaPkcs8(byte[] data)
    {
        var privateKeyInfo = KeyEncodings.ParsePrivateKeyInfo(data);
        if (privateKeyInfo.Algorithm != KeyEncodings.RsaEncryption)
            throw new WebCryptoException("DataError", "The algorithm of the key is not rsaEncryption");

        return RsaKey.ParseRsaPrivateKey(privateKeyInfo.PrivateKey);
    }

    // The handles are objects without prototype, so the script does not observe the members of Object.prototype
    private TValue CreateHandle() => Adapter.RunInRealm(EngineRealm, () => Adapter.CreateObject(Adapter.Null));

    private TValue FromRsaKey(RsaKey key)
    {
        var handle = CreateHandle();
        SetMember(handle, "n", CryptoResult(RsaKey.ToMinimalBytes(key.N)));
        SetMember(handle, "e", CryptoResult(RsaKey.ToMinimalBytes(key.E)));
        if (key.IsPrivate)
        {
            SetMember(handle, "d", CryptoResult(RsaKey.ToMinimalBytes(key.D!.Value)));
            SetMember(handle, "p", CryptoResult(RsaKey.ToMinimalBytes(key.P!.Value)));
            SetMember(handle, "q", CryptoResult(RsaKey.ToMinimalBytes(key.Q!.Value)));
            SetMember(handle, "dp", CryptoResult(RsaKey.ToMinimalBytes(key.DP!.Value)));
            SetMember(handle, "dq", CryptoResult(RsaKey.ToMinimalBytes(key.DQ!.Value)));
            SetMember(handle, "qi", CryptoResult(RsaKey.ToMinimalBytes(key.QI!.Value)));
        }

        return handle;
    }

    private RsaKey ToRsaKey(TValue handle)
    {
        byte[]? Member(string name) => OptionalCryptoBytes(Adapter.Get(handle, Key(name)));
        return RsaKey.Create(Member("n")!, Member("e")!, Member("d"), Member("p"), Member("q"), Member("dp"), Member("dq"), Member("qi"));
    }

    private TValue FromEcKey(EcKey key)
    {
        var handle = CreateHandle();
        SetMember(handle, "crv", Adapter.FromString(key.Curve.Name));
        SetMember(handle, "x", CryptoResult(key.X));
        SetMember(handle, "y", CryptoResult(key.Y));
        if (key.D is { } d)
        {
            SetMember(handle, "d", CryptoResult(d));
        }

        return handle;
    }

    private EcKey ToEcKey(TValue handle) => EcKey.Restore(GetCurve(Adapter.Get(handle, Key("crv"))), CryptoBytes(Adapter.Get(handle, Key("x"))), CryptoBytes(Adapter.Get(handle, Key("y"))), OptionalCryptoBytes(Adapter.Get(handle, Key("d"))));

    private TValue FromSeedKey(byte[] publicKey, byte[]? seed)
    {
        var handle = CreateHandle();
        SetMember(handle, "pub", CryptoResult(publicKey));
        if (seed is not null)
        {
            SetMember(handle, "seed", CryptoResult(seed));
        }

        return handle;
    }

    private TValue FromEncapsulation(byte[] sharedKey, byte[] ciphertext)
    {
        var result = CreateHandle();
        SetMember(result, "sharedKey", CryptoResult(sharedKey));
        SetMember(result, "ciphertext", CryptoResult(ciphertext));
        return result;
    }

    private TValue FromOkpKey(byte[] publicKey, byte[]? privateKey)
    {
        var handle = CreateHandle();
        SetMember(handle, "pub", CryptoResult(publicKey));
        if (privateKey is not null)
        {
            SetMember(handle, "priv", CryptoResult(privateKey));
        }

        return handle;
    }

    private TValue CryptoResultOrNull(byte[]? bytes) => bytes is null ? Adapter.Null : FromBuffer(JsBufferKind.Uint8Array, bytes);
}
