using System.Security.Cryptography;
using Meziantou.Framework.Hashing;

namespace Meziantou.Framework.WebPlatform.Cryptography;

/// <summary>
/// The concrete hybrid KEMs MLKEM768-P256, MLKEM768-X25519 and MLKEM1024-P384 of draft-irtf-cfrg-concrete-hybrid-kems-04
/// (https://datatracker.ietf.org/doc/draft-irtf-cfrg-concrete-hybrid-kems/04/, section 4), used by the "Modern Algorithms
/// in the Web Cryptography API" draft (https://wicg.github.io/webcrypto-modern-algos/#hybrid-kems). The three instances
/// use the CG framework of draft-irtf-cfrg-hybrid-kems-12 (https://datatracker.ietf.org/doc/draft-irtf-cfrg-hybrid-kems/12/,
/// section 5.5): ML-KEM combined with a nominal group, SHAKE256 as the PRG, and SHA3-256 over the C2PRI combiner as the KDF.
/// </summary>
/// <remarks>
/// The decapsulation key is the 32-byte seed (draft-irtf-cfrg-hybrid-kems-12 section 5: Ndk = Nseed): the component keys
/// are expanded from it on every operation. The scalar multiplications of the NIST curves on secret scalars use the base
/// class library (constant time), or are blinded with a key pair of the base class library for the public point of a
/// scalar (<see cref="EcCurve.MultiplyGeneratorBlinded"/>); the points received from the peer are validated here first.
/// The private keys given to the base class library always include their public point: on macOS, a private key without
/// it goes through the legacy keychain, which costs 50-90 ms per key.
/// </remarks>
internal static class HybridKem
{
    /// <summary>The length of the seed, which is also the decapsulation key (Nseed = Ndk = 32 for the three instances).</summary>
    public const int SeedLength = 32;

    /// <summary>The length of the shared secret (Nss = 32, the output of SHA3-256).</summary>
    public const int SharedKeyLength = 32;

    // FIPS 203: the length of the message m of ML-KEM.Encaps_internal, which is Nrandom of ML-KEM (concrete draft appendix A)
    private const int MlKemRandomLength = 32;

    // Concrete draft section 4.1: MLKEM768-P256 (hex 4d4c4b454d3736382d50323536)
    private static readonly Instance MlKem768P256 = new(768, Group.P256, "MLKEM768-P256"u8.ToArray());

    // Concrete draft section 4.2: \.//^\ (hex 5c2e2f2f5e5c), the label of X-Wing
    private static readonly Instance MlKem768X25519 = new(768, Group.X25519, [0x5c, 0x2e, 0x2f, 0x2f, 0x5e, 0x5c]);

    // Concrete draft section 4.3: MLKEM1024-P384 (hex 4d4c4b454d313032342d50333834)
    private static readonly Instance MlKem1024P384 = new(1024, Group.P384, "MLKEM1024-P384"u8.ToArray());

    /// <summary>Whether <paramref name="name"/> is one of the three concrete hybrid KEMs (exact names, case-sensitive).</summary>
    public static bool IsSupported(string name) => TryGetInstance(name) is not null;

    /// <summary>Gets Nek, the length of the encapsulation key: KEM_PQ.Nek + Group_T.Nelem (generic draft section 5).</summary>
    public static int GetEncapsulationKeyLength(string name) => GetInstance(name).EncapsulationKeyLength;

    /// <summary>Gets Nct, the length of the ciphertext: KEM_PQ.Nct + Group_T.Nelem (generic draft section 5).</summary>
    public static int GetCiphertextLength(string name) => GetInstance(name).CiphertextLength;

    /// <summary>
    /// Gets the length of the randomness of <see cref="Encapsulate(string, ReadOnlySpan{byte}, ReadOnlySpan{byte})"/>:
    /// Nrandom = PQ.Nrandom + T.Nrandom, with PQ.Nrandom = 32 for ML-KEM and T.Nrandom = Group_T.Nseed (generic draft
    /// appendix A, concrete draft appendix A).
    /// </summary>
    internal static int GetRandomnessLength(string name) => MlKemRandomLength + GetInstance(name).Group.SeedLength;

    /// <summary>
    /// The encapsulation key of DeriveKeyPair(seed) (generic draft sections 5.2 and 5.5, DecapsToEncaps):
    /// concat(ek_PQ, ek_T).
    /// </summary>
    /// <exception cref="ArgumentException">The seed is not 32 bytes long, or the name is not supported.</exception>
    public static byte[] GetEncapsulationKey(string name, ReadOnlySpan<byte> seed)
    {
        var instance = GetInstance(name);
        using var keys = ExpandDecapsulationKey(instance, seed);
        var result = new byte[instance.EncapsulationKeyLength];
        keys.EncapsulationKeyPq.CopyTo(result, 0);
        keys.EncapsulationKeyT.CopyTo(result, keys.EncapsulationKeyPq.Length);
        return result;
    }

    /// <summary>Encaps(ek) of the CG framework (generic draft section 5.5), with randomness from the system CSPRNG.</summary>
    /// <exception cref="WebCryptoException">The encapsulation key is invalid (an OperationError).</exception>
    public static (byte[] SharedKey, byte[] Ciphertext) Encapsulate(string name, ReadOnlySpan<byte> encapsulationKey)
    {
        var instance = GetInstance(name);
        Span<byte> randomness = stackalloc byte[MlKemRandomLength + instance.Group.SeedLength];
        RandomNumberGenerator.Fill(randomness);
        try
        {
            return Encapsulate(instance, encapsulationKey, randomness);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(randomness);
        }
    }

    /// <summary>
    /// EncapsDerand(ek, randomness) (generic draft appendix A, concrete draft appendix A): the first 32 bytes are the
    /// message m of ML-KEM.Encaps_internal, the last Group_T.Nseed bytes are the seed of Group_T.RandomScalar for the
    /// ephemeral scalar. Only used by the tests (the test vectors of the concrete draft appendix B).
    /// </summary>
    /// <exception cref="ArgumentException">The randomness does not have the length <see cref="GetRandomnessLength(string)"/>.</exception>
    /// <exception cref="WebCryptoException">The encapsulation key is invalid (an OperationError).</exception>
    internal static (byte[] SharedKey, byte[] Ciphertext) Encapsulate(string name, ReadOnlySpan<byte> encapsulationKey, ReadOnlySpan<byte> randomness)
    {
        var instance = GetInstance(name);
        if (randomness.Length != MlKemRandomLength + instance.Group.SeedLength)
            throw new ArgumentException($"The randomness must be {MlKemRandomLength + instance.Group.SeedLength} bytes long.", nameof(randomness));

        return Encapsulate(instance, encapsulationKey, randomness);
    }

    /// <summary>Decaps(dk, ct) of the CG framework (generic draft section 5.5), the decapsulation key being the seed.</summary>
    /// <exception cref="ArgumentException">The seed is not 32 bytes long, or the name is not supported.</exception>
    /// <exception cref="WebCryptoException">The ciphertext is invalid (an OperationError).</exception>
    public static byte[] Decapsulate(string name, ReadOnlySpan<byte> seed, ReadOnlySpan<byte> ciphertext)
    {
        var instance = GetInstance(name);
        if (seed.Length != SeedLength)
            throw new ArgumentException("The seed must be 32 bytes long.", nameof(seed));

        // (ct_PQ, ct_T) = split(KEM_PQ.Nct, Group_T.Nelem, ct): the split fails when the length is not Nct
        if (ciphertext.Length != instance.CiphertextLength)
            throw WebCryptoException.OperationError("The ciphertext length is invalid");

        var ciphertextPq = ciphertext[..instance.MlKemCiphertextLength];
        var ciphertextT = ciphertext[instance.MlKemCiphertextLength..];

        // Validate the public input before expanding the key: Group_T.Exp returns an error for an invalid element, and the
        // hybrid KEM is then explicitly rejecting (generic draft section 4.2). This only depends on the ciphertext.
        ValidateElement(instance.Group, ciphertextT, "The ciphertext is invalid");

        using var keys = ExpandDecapsulationKey(instance, seed);

        // prepareDecapsG (generic draft section 5.1.1). ML-KEM.Decaps cannot fail here: the expanded decapsulation key is
        // well-formed and the ciphertext has the expected length; an invalid ciphertext is implicitly rejected.
        var sharedSecretPq = MlKem.Decapsulate(instance.MlKemParameterSet, keys.DecapsulationKeyPq, ciphertextPq);
        var sharedSecretT = Exp(instance.Group, ciphertextT, keys.DecapsulationKeyT, keys.EncapsulationKeyT);
        try
        {
            return Combine(instance, sharedSecretPq, sharedSecretT, ciphertextT, keys.EncapsulationKeyT);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(sharedSecretPq);
            CryptographicOperations.ZeroMemory(sharedSecretT);
        }
    }

    private static (byte[] SharedKey, byte[] Ciphertext) Encapsulate(Instance instance, ReadOnlySpan<byte> encapsulationKey, ReadOnlySpan<byte> randomness)
    {
        // (ek_PQ, ek_T) = split(KEM_PQ.Nek, Group_T.Nelem, ek)
        if (encapsulationKey.Length != instance.EncapsulationKeyLength)
            throw WebCryptoException.OperationError("The encapsulation key length is invalid");

        var encapsulationKeyPq = encapsulationKey[..instance.MlKemEncapsulationKeyLength];
        var encapsulationKeyT = encapsulationKey[instance.MlKemEncapsulationKeyLength..];

        // ML-KEM.Encaps fails on an encapsulation key that does not pass the modulus check (FIPS 203 §7.2), and
        // Group_T.Exp(ek_T, sk_E) fails on an invalid element (generic draft section 4.2)
        if (!MlKem.IsValidEncapsulationKey(instance.MlKemParameterSet, encapsulationKeyPq))
            throw WebCryptoException.OperationError("The encapsulation key is invalid");

        ValidateElement(instance.Group, encapsulationKeyT, "The encapsulation key is invalid");

        // prepareEncapsG (generic draft section 5.1.1), derandomized (appendix A): the PQ component uses the first
        // PQ.Nrandom bytes, the traditional component the last T.Nrandom bytes
        var (sharedSecretPq, ciphertextPq) = MlKem.Encapsulate(instance.MlKemParameterSet, encapsulationKeyPq, randomness[..MlKemRandomLength]);
        var ephemeralScalar = RandomScalar(instance.Group, randomness[MlKemRandomLength..]);
        byte[]? sharedSecretT = null;
        try
        {
            // ct_T = Exp(g, sk_E), ss_T = ElementToSharedSecret(Exp(ek_T, sk_E))
            var ciphertextT = ExpBase(instance.Group, ephemeralScalar);
            sharedSecretT = Exp(instance.Group, encapsulationKeyT, ephemeralScalar, ciphertextT);
            var sharedKey = Combine(instance, sharedSecretPq, sharedSecretT, ciphertextT, encapsulationKeyT);

            // ct_H = concat(ct_PQ, ct_T)
            var ciphertext = new byte[instance.CiphertextLength];
            ciphertextPq.CopyTo(ciphertext, 0);
            ciphertextT.CopyTo(ciphertext, ciphertextPq.Length);
            return (sharedKey, ciphertext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(ephemeralScalar);
            CryptographicOperations.ZeroMemory(sharedSecretPq);
            if (sharedSecretT is not null)
            {
                CryptographicOperations.ZeroMemory(sharedSecretT);
            }
        }
    }

    /// <summary>
    /// expandDecapsKeyG(seed) (generic draft section 5.1.1): seed_full = SHAKE256(seed) of KEM_PQ.Nseed + Group_T.Nseed
    /// bytes, split into the 64-byte seed d || z of ML-KEM (KeyGen_internal, concrete draft section 3.2.1) and the seed of
    /// Group_T.RandomScalar.
    /// </summary>
    private static ExpandedKeys ExpandDecapsulationKey(Instance instance, ReadOnlySpan<byte> seed)
    {
        if (seed.Length != SeedLength)
            throw new ArgumentException("The seed must be 32 bytes long.", nameof(seed));

        Span<byte> fullSeed = stackalloc byte[MlKem.SeedLength + instance.Group.SeedLength];
        Shake.HashData(256, seed, fullSeed);
        try
        {
            var (encapsulationKeyPq, decapsulationKeyPq) = MlKem.GenerateKeyFromSeed(instance.MlKemParameterSet, fullSeed[..MlKem.SeedLength]);
            var decapsulationKeyT = RandomScalar(instance.Group, fullSeed[MlKem.SeedLength..]);
            var encapsulationKeyT = ExpBase(instance.Group, decapsulationKeyT);
            return new ExpandedKeys(encapsulationKeyPq, encapsulationKeyT, decapsulationKeyPq, decapsulationKeyT);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(fullSeed);
        }
    }

    /// <summary>
    /// The C2PRI combiner with SHA3-256 as the KDF (generic draft section 5.1.3):
    /// KDF(concat(ss_PQ, ss_T, ct_T, ek_T, label)).
    /// </summary>
    private static byte[] Combine(Instance instance, ReadOnlySpan<byte> sharedSecretPq, ReadOnlySpan<byte> sharedSecretT, ReadOnlySpan<byte> ciphertextT, ReadOnlySpan<byte> encapsulationKeyT)
    {
        // At most 32 + 48 + 97 + 97 + 14 bytes: the input of the KDF fits on the stack
        Span<byte> input = stackalloc byte[sharedSecretPq.Length + sharedSecretT.Length + ciphertextT.Length + encapsulationKeyT.Length + instance.Label.Length];
        var offset = 0;
        sharedSecretPq.CopyTo(input[offset..]);
        offset += sharedSecretPq.Length;
        sharedSecretT.CopyTo(input[offset..]);
        offset += sharedSecretT.Length;
        ciphertextT.CopyTo(input[offset..]);
        offset += ciphertextT.Length;
        encapsulationKeyT.CopyTo(input[offset..]);
        offset += encapsulationKeyT.Length;
        instance.Label.CopyTo(input[offset..]);

        var result = Sha3.HashData(256, input);
        CryptographicOperations.ZeroMemory(input);
        return result;
    }

    /// <summary>
    /// Group_T.RandomScalar(seed). For the NIST curves (concrete draft section 3.1.1), rejection sampling: the first
    /// Nscalar-byte big-endian integer of the seed in [1, order), else the next one, and an error when the seed is
    /// exhausted. For Curve25519 (section 3.1.2), the identity function (X25519 clamps the scalar itself).
    /// </summary>
    private static byte[] RandomScalar(Group group, ReadOnlySpan<byte> seed)
    {
        if (group.Curve is null)
            return seed.ToArray();

        for (var start = 0; start + group.ScalarLength <= seed.Length; start += group.ScalarLength)
        {
            var candidate = seed.Slice(start, group.ScalarLength);
            if (IsInScalarRange(candidate, group.Order))
                return candidate.ToArray();
        }

        // Cryptographically negligible for a uniformly random seed (< 2^-128 for P-256, < 2^-192 for P-384)
        throw WebCryptoException.OperationError("The rejection sampling of the scalar failed");
    }

    /// <summary>
    /// Whether the big-endian integer <paramref name="candidate"/> is in [1, order), computed without branching on its
    /// bytes: the borrow of candidate - order is 1 when candidate &lt; order. Only the result may leak, as in the draft's
    /// loop, whose number of iterations depends on it.
    /// </summary>
    private static bool IsInScalarRange(ReadOnlySpan<byte> candidate, ReadOnlySpan<byte> order)
    {
        var borrow = 0;
        var accumulator = 0;
        for (var i = candidate.Length - 1; i >= 0; i--)
        {
            var difference = candidate[i] - order[i] - borrow;
            borrow = (difference >> 31) & 1;
            accumulator |= candidate[i];
        }

        return (borrow & (((accumulator - 1) >> 31) + 1)) != 0;
    }

    /// <summary>Group_T.Exp(g, x): the public element of a scalar, encoded with Nelem bytes.</summary>
    private static byte[] ExpBase(Group group, byte[] scalar)
    {
        if (group.Curve is null)
            return X25519.GetPublicKey(scalar);

        var point = group.Curve.MultiplyGeneratorBlinded(scalar);

        // Elliptic-Curve-Point-to-Octet-String (SEC 1 section 2.3.3), uncompressed: 04 || X || Y, each coordinate
        // left-padded to the length of the field
        var length = group.Curve.FieldLength;
        var result = new byte[group.ElementLength];
        result[0] = 4;
        RsaKey.I2Osp(point.X, length).CopyTo(result, 1);
        RsaKey.I2Osp(point.Y, length).CopyTo(result, 1 + length);
        return result;
    }

    /// <summary>
    /// Group_T.ElementToSharedSecret(Group_T.Exp(element, scalar)): the X coordinate of the point for the NIST curves
    /// (concrete draft section 3.1.1), X25519(scalar, element) for Curve25519 (section 3.1.2). The element is validated by
    /// the caller (<see cref="ValidateElement"/>), and <paramref name="publicElement"/> is Exp(g, scalar).
    /// </summary>
    private static byte[] Exp(Group group, ReadOnlySpan<byte> element, byte[] scalar, ReadOnlySpan<byte> publicElement)
    {
        // RFC 7748 X25519 accepts every 32-byte string, and the draft (as X-Wing) does not reject the all-zero output
        if (group.Curve is null)
            return X25519.DeriveSharedSecret(scalar, element);

        var length = group.Curve.FieldLength;
        using var ecdh = ECDiffieHellman.Create(new ECParameters
        {
            Curve = group.Curve.BclCurve,
            D = scalar,
            Q = new ECPoint { X = publicElement.Slice(1, length).ToArray(), Y = publicElement.Slice(1 + length, length).ToArray() },
        });
        using var peer = ECDiffieHellman.Create(new ECParameters
        {
            Curve = group.Curve.BclCurve,
            Q = new ECPoint { X = element.Slice(1, length).ToArray(), Y = element.Slice(1 + length, length).ToArray() },
        });

        // The raw secret agreement is the X coordinate of the shared point with the length of the field (Nss)
        return ecdh.DeriveRawSecretAgreement(peer.PublicKey);
    }

    /// <summary>
    /// Validates an element received from the peer (ek_T or ct_T). For the NIST curves, the element must be the
    /// uncompressed encoding of a point of the curve (SEC 1 section 2.3.4); the curves have a cofactor of 1, so every
    /// such point is in the prime-order group, and the identity cannot be encoded with Nelem bytes. Every 32-byte string is
    /// a valid Curve25519 element.
    /// </summary>
    private static void ValidateElement(Group group, ReadOnlySpan<byte> element, string message)
    {
        if (group.Curve is null)
            return;

        var length = group.Curve.FieldLength;
        if (element.Length != group.ElementLength || element[0] != 4)
            throw WebCryptoException.OperationError(message);

        var x = new System.Numerics.BigInteger(element.Slice(1, length), isUnsigned: true, isBigEndian: true);
        var y = new System.Numerics.BigInteger(element.Slice(1 + length, length), isUnsigned: true, isBigEndian: true);
        if (!group.Curve.IsOnCurve((x, y)))
            throw WebCryptoException.OperationError(message);
    }

    private static Instance GetInstance(string name)
    {
        return TryGetInstance(name) ?? throw new ArgumentException($"The hybrid KEM '{name}' is not supported.", nameof(name));
    }

    private static Instance? TryGetInstance(string name) => name switch
    {
        "MLKEM768-P256" => MlKem768P256,
        "MLKEM768-X25519" => MlKem768X25519,
        "MLKEM1024-P384" => MlKem1024P384,
        _ => null,
    };

    /// <summary>A concrete hybrid KEM (concrete draft section 4): KEM_PQ, Group_T and the label.</summary>
    private sealed class Instance
    {
        public Instance(int mlKemParameterSet, Group group, byte[] label)
        {
            MlKemParameterSet = mlKemParameterSet;
            Group = group;
            Label = label;
            MlKemEncapsulationKeyLength = MlKem.GetEncapsulationKeyLength(mlKemParameterSet);
            MlKemCiphertextLength = MlKem.GetCiphertextLength(mlKemParameterSet);
        }

        public int MlKemParameterSet { get; }

        public Group Group { get; }

        public byte[] Label { get; }

        public int MlKemEncapsulationKeyLength { get; }

        public int MlKemCiphertextLength { get; }

        public int EncapsulationKeyLength => MlKemEncapsulationKeyLength + Group.ElementLength;

        public int CiphertextLength => MlKemCiphertextLength + Group.ElementLength;
    }

    /// <summary>A nominal group (concrete draft section 3.1) with its constants Nseed, Nscalar and Nelem.</summary>
    private sealed class Group
    {
        // Section 3.1.1: Nseed = 128 for P-256 (up to 3 rejections), 48 for P-384 (no room for a rejection)
        public static readonly Group P256 = new(EcCurve.P256, seedLength: 128);
        public static readonly Group P384 = new(EcCurve.P384, seedLength: 48);

        // Section 3.1.2: Nseed = Nscalar = Nelem = 32
        public static readonly Group X25519 = new(curve: null, seedLength: 32);

        private Group(EcCurve? curve, int seedLength)
        {
            Curve = curve;
            SeedLength = seedLength;
            if (curve is null)
            {
                ScalarLength = 32;
                ElementLength = 32;
                Order = [];
            }
            else
            {
                ScalarLength = curve.OrderLength;
                ElementLength = 1 + (2 * curve.FieldLength);
                Order = RsaKey.I2Osp(curve.N, curve.OrderLength);
            }
        }

        /// <summary>Gets the NIST curve, or null for Curve25519.</summary>
        public EcCurve? Curve { get; }

        /// <summary>Gets Nseed, the length of the input of RandomScalar.</summary>
        public int SeedLength { get; }

        /// <summary>Gets Nscalar, the length of a scalar.</summary>
        public int ScalarLength { get; }

        /// <summary>Gets Nelem, the length of an encoded element.</summary>
        public int ElementLength { get; }

        /// <summary>Gets the big-endian order of the NIST curve (Nscalar bytes).</summary>
        public byte[] Order { get; }
    }

    /// <summary>The output of expandDecapsKeyG: the component keys, cleared when disposed.</summary>
    private readonly record struct ExpandedKeys(byte[] EncapsulationKeyPq, byte[] EncapsulationKeyT, byte[] DecapsulationKeyPq, byte[] DecapsulationKeyT) : IDisposable
    {
        public void Dispose()
        {
            CryptographicOperations.ZeroMemory(DecapsulationKeyPq);
            CryptographicOperations.ZeroMemory(DecapsulationKeyT);
        }
    }
}
