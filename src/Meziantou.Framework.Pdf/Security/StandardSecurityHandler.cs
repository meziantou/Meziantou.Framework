using System.Buffers.Binary;
using System.Security.Cryptography;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Text;

namespace Meziantou.Framework.Pdf.Security;

/// <summary>
/// The standard security handler (ISO 32000-2, 7.6.4): the file key computed from a password, and the encryption of the
/// strings and streams of each object with RC4 (revisions 2 and 3), AES-128 (revision 4) or AES-256 (revisions 5 and 6).
/// </summary>
internal sealed class StandardSecurityHandler
{
    private const int PermissionsReservedBits = unchecked((int)0xFFFFF0C0);

    private readonly byte[] _fileKey;
    private readonly int _revision;
    private readonly CryptMethod _stringMethod;
    private readonly CryptMethod _streamMethod;
    private readonly CryptMethod _embeddedFileMethod;
    private readonly Dictionary<string, CryptMethod> _cryptFilters;

    private StandardSecurityHandler(PdfDictionary dictionary, byte[] fileKey, int revision, int permissions, bool encryptMetadata, CryptMethod stringMethod, CryptMethod streamMethod, CryptMethod embeddedFileMethod, Dictionary<string, CryptMethod> cryptFilters, bool isOwner)
    {
        Dictionary = dictionary;
        _fileKey = fileKey;
        _revision = revision;
        RawPermissions = permissions;
        EncryptMetadata = encryptMetadata;
        _stringMethod = stringMethod;
        _streamMethod = streamMethod;
        _embeddedFileMethod = embeddedFileMethod;
        _cryptFilters = cryptFilters;
        IsOwner = isOwner;
    }

    /// <summary>Gets the encryption dictionary, with direct values only.</summary>
    public PdfDictionary Dictionary { get; }

    /// <summary>Gets the <c>/P</c> value.</summary>
    public int RawPermissions { get; }

    public PdfPermissions Permissions => (PdfPermissions)RawPermissions & PdfPermissions.All;

    public bool EncryptMetadata { get; }

    /// <summary>Gets a value indicating whether the password is the owner password: the permissions do not apply.</summary>
    public bool IsOwner { get; }

    public PdfEncryptionAlgorithm Algorithm => _revision switch
    {
        2 => PdfEncryptionAlgorithm.Rc4With40BitKey,
        >= 5 => PdfEncryptionAlgorithm.AesWith256BitKey,
        _ when _streamMethod == CryptMethod.AesV2 || _stringMethod == CryptMethod.AesV2 => PdfEncryptionAlgorithm.AesWith128BitKey,
        _ => PdfEncryptionAlgorithm.Rc4With128BitKey,
    };

    private static ReadOnlySpan<byte> Padding =>
    [
        0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41, 0x64, 0x00, 0x4E, 0x56, 0xFF, 0xFA, 0x01, 0x08,
        0x2E, 0x2E, 0x00, 0xB6, 0xD0, 0x68, 0x3E, 0x80, 0x2F, 0x0C, 0xA9, 0xFE, 0x64, 0x53, 0x69, 0x7A,
    ];

    /// <summary>Reads the encryption dictionary of a file and authenticates the password (7.6.4.4).</summary>
    /// <exception cref="UnsupportedPdfFeatureException">The document uses another security handler, or an unknown version.</exception>
    /// <exception cref="InvalidPdfContentException">The encryption dictionary is invalid.</exception>
    /// <exception cref="PdfPasswordException">The password is neither the user nor the owner password.</exception>
    public static StandardSecurityHandler Open(PdfDictionary dictionary, ReadOnlySpan<byte> id, string? password)
    {
        var filter = dictionary.GetName(PdfNames.Filter)?.Value;
        if (filter != "Standard")
            throw new UnsupportedPdfFeatureException($"The document is encrypted with the security handler '{filter}': only the standard security handler is supported");

        var version = (int)dictionary.GetInteger(PdfName.Create("V"), 0);
        var revision = (int)dictionary.GetInteger(PdfName.Create("R"), 0);
        if (version is not (1 or 2 or 4 or 5) || revision is < 2 or > 6)
            throw new UnsupportedPdfFeatureException($"The standard security handler of version {version} and revision {revision} is not supported");

        var owner = dictionary.TryGetResolved<PdfString>(PdfName.Create("O"), out var o) ? o.Span.ToArray() : throw new InvalidPdfContentException("The encryption dictionary has no /O");
        var user = dictionary.TryGetResolved<PdfString>(PdfName.Create("U"), out var u) ? u.Span.ToArray() : throw new InvalidPdfContentException("The encryption dictionary has no /U");
        var permissions = unchecked((int)dictionary.GetInteger(PdfName.Create("P"), 0));
        var encryptMetadata = version < 4 || dictionary.GetBoolean(PdfName.Create("EncryptMetadata"), defaultValue: true);

        // The crypt filters of versions 4 and 5 (7.6.6); the methods of versions 1 and 2 are RC4
        var cryptFilters = new Dictionary<string, CryptMethod>(StringComparer.Ordinal) { ["Identity"] = CryptMethod.None };
        var keyLength = version == 1 ? 5 : ReadKeyLength(dictionary.GetInteger(PdfNames.Length, 40));
        CryptMethod stringMethod;
        CryptMethod streamMethod;
        CryptMethod embeddedFileMethod;
        if (version >= 4)
        {
            if (dictionary.GetResolved(PdfName.Create("CF")) is PdfDictionary filters)
            {
                foreach (var (name, value) in filters)
                {
                    if (PdfReference.ResolveIfReference(value) is not PdfDictionary cryptFilter)
                        continue;

                    var method = cryptFilter.GetName(PdfName.Create("CFM"))?.Value switch
                    {
                        "V2" => CryptMethod.Rc4,
                        "AESV2" => CryptMethod.AesV2,
                        "AESV3" => CryptMethod.AesV3,
                        _ => CryptMethod.None,
                    };
                    cryptFilters[name.Value] = method;
                    if (method == CryptMethod.Rc4 && cryptFilter.TryGetResolved<PdfInteger>(PdfNames.Length, out var length))
                    {
                        keyLength = ReadKeyLength(length.Value);
                    }
                }
            }

            stringMethod = GetCryptFilter(cryptFilters, dictionary.GetName(PdfName.Create("StrF"))?.Value);
            streamMethod = GetCryptFilter(cryptFilters, dictionary.GetName(PdfName.Create("StmF"))?.Value);
            embeddedFileMethod = dictionary.GetName(PdfName.Create("EFF")) is { } eff ? GetCryptFilter(cryptFilters, eff.Value) : streamMethod;
            if (stringMethod == CryptMethod.AesV2 || streamMethod == CryptMethod.AesV2)
            {
                keyLength = 16;
            }
        }
        else
        {
            stringMethod = CryptMethod.Rc4;
            streamMethod = CryptMethod.Rc4;
            embeddedFileMethod = CryptMethod.Rc4;
        }

        byte[] fileKey;
        bool isOwner;
        if (revision >= 5)
        {
            if (owner.Length < 48 || user.Length < 48)
                throw new InvalidPdfContentException("The /O or /U entry of the encryption dictionary is too short");

            var ownerEncryptedKey = dictionary.TryGetResolved<PdfString>(PdfName.Create("OE"), out var oe) ? oe.Span.ToArray() : [];
            var userEncryptedKey = dictionary.TryGetResolved<PdfString>(PdfName.Create("UE"), out var ue) ? ue.Span.ToArray() : [];
            var bytes = SaslPrep.GetBytes(password ?? "");
            if (TryAuthenticateAes256(bytes, owner.AsSpan(0, 48), user.AsSpan(0, 48), ownerEncryptedKey, revision, isOwnerPassword: true) is { } ownerKey)
            {
                fileKey = ownerKey;
                isOwner = true;
            }
            else if (TryAuthenticateAes256(bytes, owner.AsSpan(0, 48), user.AsSpan(0, 48), userEncryptedKey, revision, isOwnerPassword: false) is { } userKey)
            {
                fileKey = userKey;
                isOwner = false;
            }
            else
            {
                throw new PdfPasswordException("The password is not the user or the owner password of the document");
            }
        }
        else
        {
            if (owner.Length < 32 || user.Length < 32)
                throw new InvalidPdfContentException("The /O or /U entry of the encryption dictionary is too short");

            owner = owner[..32];
            var bytes = GetLegacyPasswordBytes(password ?? "");

            // Without a /Length, the key of revisions 3 and 4 is 40 bits; some writers omit it for 128-bit keys
            int[] keyLengths = revision >= 3 && !dictionary.ContainsKey(PdfNames.Length) && keyLength != 16 ? [keyLength, 16] : [keyLength];
            fileKey = [];
            isOwner = false;
            foreach (var candidate in keyLengths)
            {
                if (TryAuthenticateOwner(bytes, owner, user, permissions, id, revision, candidate, encryptMetadata) is { } ownerKey)
                {
                    fileKey = ownerKey;
                    isOwner = true;
                    break;
                }

                if (TryAuthenticateUser(bytes, owner, user, permissions, id, revision, candidate, encryptMetadata) is { } userKey)
                {
                    fileKey = userKey;
                    break;
                }
            }

            if (fileKey.Length == 0)
                throw new PdfPasswordException("The password is not the user or the owner password of the document");
        }

        // AES-256 needs the 32-byte key of revisions 5 and 6
        if (revision < 5 && (stringMethod == CryptMethod.AesV3 || streamMethod == CryptMethod.AesV3 || embeddedFileMethod == CryptMethod.AesV3 || cryptFilters.ContainsValue(CryptMethod.AesV3)))
            throw new InvalidPdfContentException($"The crypt filter AESV3 needs the revision 5 or 6 of the standard security handler, not {revision}");

        // Revisions 5 and 6 protect the permissions with the file key (7.6.4.4.12): permissions that do not match are
        // not trusted, and none is granted
        if (revision >= 5 && !ArePermissionsAuthentic(dictionary, fileKey, permissions, encryptMetadata))
        {
            permissions = PermissionsReservedBits;
        }

        return new StandardSecurityHandler(CopyDirect(dictionary), fileKey, revision, permissions, encryptMetadata, stringMethod, streamMethod, embeddedFileMethod, cryptFilters, isOwner);
    }

    /// <summary>Creates the security handler of the encryption of a document that is saved, with its encryption dictionary.</summary>
    public static StandardSecurityHandler Create(PdfEncryption encryption, ReadOnlySpan<byte> id)
    {
        var permissions = PermissionsReservedBits | (int)(encryption.Permissions & PdfPermissions.All);
        var userPassword = encryption.UserPassword ?? "";
        var ownerPassword = string.IsNullOrEmpty(encryption.OwnerPassword) ? userPassword : encryption.OwnerPassword;
        var encryptMetadata = encryption.EncryptMetadata;
        var cryptFilters = new Dictionary<string, CryptMethod>(StringComparer.Ordinal) { ["Identity"] = CryptMethod.None };
        var dictionary = new PdfDictionary { [PdfNames.Filter] = PdfName.Create("Standard") };
        if (encryption.Algorithm == PdfEncryptionAlgorithm.AesWith256BitKey)
        {
            var fileKey = RandomNumberGenerator.GetBytes(32);
            var userBytes = SaslPrep.GetBytes(userPassword);
            var ownerBytes = SaslPrep.GetBytes(ownerPassword);

            // Algorithm 8: the user hash, its validation and key salts, and the file key encrypted with the key salt
            var userSalts = RandomNumberGenerator.GetBytes(16);
            var user = new byte[48];
            HashAes256(userBytes, userSalts.AsSpan(0, 8), [], revision: 6).CopyTo(user, 0);
            userSalts.CopyTo(user, 32);
            var userEncryptedKey = EncryptKey(HashAes256(userBytes, userSalts.AsSpan(8, 8), [], revision: 6), fileKey);

            // Algorithm 9: the same for the owner password, with the user entry
            var ownerSalts = RandomNumberGenerator.GetBytes(16);
            var owner = new byte[48];
            HashAes256(ownerBytes, ownerSalts.AsSpan(0, 8), user, revision: 6).CopyTo(owner, 0);
            ownerSalts.CopyTo(owner, 32);
            var ownerEncryptedKey = EncryptKey(HashAes256(ownerBytes, ownerSalts.AsSpan(8, 8), user, revision: 6), fileKey);

            // Algorithm 10: the permissions, encrypted with the file key
            var perms = new byte[16];
            BinaryPrimitives.WriteInt32LittleEndian(perms, permissions);
            perms.AsSpan(4, 4).Fill(0xFF);
            perms[8] = encryptMetadata ? (byte)'T' : (byte)'F';
            perms[9] = (byte)'a';
            perms[10] = (byte)'d';
            perms[11] = (byte)'b';
            RandomNumberGenerator.Fill(perms.AsSpan(12));
            using (var aes = Aes.Create())
            {
                aes.Key = fileKey;
                perms = aes.EncryptEcb(perms, PaddingMode.None);
            }

            dictionary[PdfName.Create("V")] = PdfInteger.Get(5);
            dictionary[PdfName.Create("R")] = PdfInteger.Get(6);
            dictionary[PdfNames.Length] = PdfInteger.Get(256);
            AddCryptFilter(dictionary, "AESV3", 32);
            dictionary[PdfName.Create("O")] = PdfString.FromBytes(owner, hexadecimal: true);
            dictionary[PdfName.Create("U")] = PdfString.FromBytes(user, hexadecimal: true);
            dictionary[PdfName.Create("OE")] = PdfString.FromBytes(ownerEncryptedKey, hexadecimal: true);
            dictionary[PdfName.Create("UE")] = PdfString.FromBytes(userEncryptedKey, hexadecimal: true);
            dictionary[PdfName.Create("Perms")] = PdfString.FromBytes(perms, hexadecimal: true);
            dictionary[PdfName.Create("P")] = PdfInteger.Get(permissions);
            if (!encryptMetadata)
            {
                dictionary[PdfName.Create("EncryptMetadata")] = PdfBoolean.False;
            }

            cryptFilters["StdCF"] = CryptMethod.AesV3;
            return new StandardSecurityHandler(dictionary, fileKey, 6, permissions, encryptMetadata, CryptMethod.AesV3, CryptMethod.AesV3, CryptMethod.AesV3, cryptFilters, isOwner: true);
        }

        var (version, revision, keyLength, method) = encryption.Algorithm switch
        {
            PdfEncryptionAlgorithm.Rc4With40BitKey => (1, 2, 5, CryptMethod.Rc4),
            PdfEncryptionAlgorithm.Rc4With128BitKey => (2, 3, 16, CryptMethod.Rc4),
            PdfEncryptionAlgorithm.AesWith128BitKey => (4, 4, 16, CryptMethod.AesV2),
            _ => throw new ArgumentOutOfRangeException(nameof(encryption), encryption.Algorithm, "The encryption algorithm is not valid."),
        };

        // Revision 2 and 3 encrypt the metadata: only revision 4 has the entry
        if (revision < 4)
        {
            encryptMetadata = true;
        }

        // The passwords of revisions 2 to 4 are in PDFDocEncoding: another character would be replaced, and weaken them
        if (!IsLegacyPassword(userPassword) || !IsLegacyPassword(ownerPassword))
            throw new ArgumentException("A password of the RC4 and AES-128 algorithms can only have the characters of PDFDocEncoding: use AES-256 for other characters", nameof(encryption));

        var ownerKey = ComputeOwnerKey(GetLegacyPasswordBytes(ownerPassword), revision, keyLength);
        var ownerEntry = ComputeOwnerEntry(ownerKey, GetLegacyPasswordBytes(userPassword), revision);
        var legacyFileKey = ComputeFileKey(GetLegacyPasswordBytes(userPassword), ownerEntry, permissions, id, revision, keyLength, encryptMetadata);
        var userEntry = ComputeUserEntry(legacyFileKey, id, revision);
        dictionary[PdfName.Create("V")] = PdfInteger.Get(version);
        dictionary[PdfName.Create("R")] = PdfInteger.Get(revision);
        dictionary[PdfNames.Length] = PdfInteger.Get(keyLength * 8);
        if (version == 4)
        {
            AddCryptFilter(dictionary, "AESV2", 16);
            cryptFilters["StdCF"] = CryptMethod.AesV2;
            if (!encryptMetadata)
            {
                dictionary[PdfName.Create("EncryptMetadata")] = PdfBoolean.False;
            }
        }

        dictionary[PdfName.Create("O")] = PdfString.FromBytes(ownerEntry, hexadecimal: true);
        dictionary[PdfName.Create("U")] = PdfString.FromBytes(userEntry, hexadecimal: true);
        dictionary[PdfName.Create("P")] = PdfInteger.Get(permissions);
        return new StandardSecurityHandler(dictionary, legacyFileKey, revision, permissions, encryptMetadata, method, method, method, cryptFilters, isOwner: true);
    }

    /// <summary>Gets the method of the strings and streams of an object; a stream can name another crypt filter (7.6.6) or not be encrypted.</summary>
    public CryptMethod GetStreamMethod(PdfDictionary dictionary)
    {
        // The cross-reference streams are not encrypted (7.6.2), nor the metadata when the dictionary says so (7.6.4.2)
        var type = dictionary.GetName(PdfNames.Type)?.Value;
        if (type == "XRef")
            return CryptMethod.None;

        if (type == "Metadata" && !EncryptMetadata)
            return CryptMethod.None;

        // A /Crypt filter, the first of the filters, names the crypt filter of the stream
        var filters = dictionary.GetResolved(PdfNames.Filter);
        var first = filters switch
        {
            PdfName name => name,
            PdfArray { Count: > 0 } array => array.GetResolved(0) as PdfName,
            _ => null,
        };
        if (first == PdfNames.Crypt)
        {
            var parameters = dictionary.GetResolved(PdfNames.DecodeParms) switch
            {
                PdfDictionary d => d,
                PdfArray { Count: > 0 } array => array.GetResolved(0) as PdfDictionary,
                _ => null,
            };
            var name = parameters?.GetName(PdfNames.Name)?.Value ?? "Identity";
            return GetCryptFilter(_cryptFilters, name);
        }

        return type == "EmbeddedFile" ? _embeddedFileMethod : _streamMethod;
    }

    /// <summary>Decrypts the strings and the streams of an object read from the file (7.6.2).</summary>
    public PdfObject Decrypt(PdfObject value, int number, int generation) => Transform(value, number, generation, encrypt: false, depth: 0);

    public byte[] DecryptString(ReadOnlySpan<byte> data, int number, int generation) => Apply(_stringMethod, data, number, generation, encrypt: false);

    public byte[] EncryptString(ReadOnlySpan<byte> data, int number, int generation) => Apply(_stringMethod, data, number, generation, encrypt: true);

    public byte[] EncryptStream(ReadOnlySpan<byte> data, CryptMethod method, int number, int generation) => Apply(method, data, number, generation, encrypt: true);

    /// <summary>Gets a value indicating whether the strings of a dictionary are not encrypted: the signature of a signature dictionary (7.6.2).</summary>
    public static bool IsSignatureContents(PdfDictionary dictionary, PdfName key) => key == PdfNames.Contents && dictionary.GetName(PdfNames.Type)?.Value is "Sig" or "DocTimeStamp";

    private PdfObject Transform(PdfObject value, int number, int generation, bool encrypt, int depth)
    {
        if (depth > 256)
            return value;

        switch (value)
        {
            case PdfString text:
                return PdfString.FromBytes(Apply(_stringMethod, text.Span, number, generation, encrypt), text.IsHexadecimal);

            case PdfArray array:
                for (var i = 0; i < array.Count; i++)
                {
                    if (array[i] is PdfString or PdfArray or PdfDictionary)
                    {
                        array[i] = Transform(array[i], number, generation, encrypt, depth + 1);
                    }
                }

                return array;

            case PdfDictionary dictionary:
                TransformDictionary(dictionary, number, generation, encrypt, depth);
                return dictionary;

            case PdfStream stream:
                TransformDictionary(stream.Dictionary, number, generation, encrypt, depth);
                var method = GetStreamMethod(stream.Dictionary);
                if (method != CryptMethod.None)
                {
                    stream.SetLoadedData(Apply(method, stream.EncodedData.Span, number, generation, encrypt));
                }

                return stream;

            default:
                return value;
        }
    }

    private void TransformDictionary(PdfDictionary dictionary, int number, int generation, bool encrypt, int depth)
    {
        List<(PdfName Key, PdfObject Value)>? changes = null;
        foreach (var (key, item) in dictionary)
        {
            if (item is not (PdfString or PdfArray or PdfDictionary) || IsSignatureContents(dictionary, key))
                continue;

            (changes ??= []).Add((key, Transform(item, number, generation, encrypt, depth + 1)));
        }

        if (changes is not null)
        {
            foreach (var (key, item) in changes)
            {
                dictionary.SetParsed(key, item);
            }
        }
    }

    private byte[] Apply(CryptMethod method, ReadOnlySpan<byte> data, int number, int generation, bool encrypt)
    {
        switch (method)
        {
            case CryptMethod.Rc4:
                return Rc4.Transform(GetObjectKey(number, generation, aes: false), data);

            case CryptMethod.AesV2:
                return encrypt ? EncryptAes(GetObjectKey(number, generation, aes: true), data) : DecryptAes(GetObjectKey(number, generation, aes: true), data);

            case CryptMethod.AesV3:
                return encrypt ? EncryptAes(_fileKey, data) : DecryptAes(_fileKey, data);

            default:
                return data.ToArray();
        }
    }

    /// <summary>Algorithm 1 (7.6.3.3): the key of an object, from the file key and the number and generation of the object.</summary>
    private byte[] GetObjectKey(int number, int generation, bool aes)
    {
        Span<byte> input = stackalloc byte[_fileKey.Length + 9];
        _fileKey.CopyTo(input);
        var position = _fileKey.Length;
        input[position++] = (byte)number;
        input[position++] = (byte)(number >> 8);
        input[position++] = (byte)(number >> 16);
        input[position++] = (byte)generation;
        input[position++] = (byte)(generation >> 8);
        if (aes)
        {
            "sAlT"u8.CopyTo(input[position..]);
            position += 4;
        }

        var hash = MD5.HashData(input[..position]);
        return hash[..Math.Min(_fileKey.Length + 5, 16)];
    }

    /// <summary>The data encrypted with AES in CBC mode, after a random initialization vector, with the padding of PKCS #7 (7.6.3.2).</summary>
    private static byte[] EncryptAes(byte[] key, ReadOnlySpan<byte> data)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        Span<byte> iv = stackalloc byte[16];
        RandomNumberGenerator.Fill(iv);
        var cipher = aes.EncryptCbc(data, iv, PaddingMode.PKCS7);
        var result = new byte[16 + cipher.Length];
        iv.CopyTo(result);
        cipher.CopyTo(result, 16);
        return result;
    }

    /// <summary>The data decrypted with AES: the files whose data is not a multiple of the block size or whose padding is wrong are read as far as possible.</summary>
    private static byte[] DecryptAes(byte[] key, ReadOnlySpan<byte> data)
    {
        if (data.Length < 32)
            return [];

        var length = (data.Length - 16) / 16 * 16;
        using var aes = Aes.Create();
        byte[] plain;
        try
        {
            aes.Key = key;
            plain = aes.DecryptCbc(data.Slice(16, length), data[..16], PaddingMode.None);
        }
        catch (CryptographicException ex)
        {
            throw new InvalidPdfContentException("The data cannot be decrypted", ex);
        }

        var padding = plain[^1];
        if (padding is >= 1 and <= 16 && padding <= plain.Length && plain.AsSpan(plain.Length - padding).IndexOfAnyExcept(padding) < 0)
            return plain[..^padding];

        return plain;
    }

    private static byte[] EncryptKey(byte[] key, byte[] fileKey)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        return aes.EncryptCbc(fileKey, new byte[16], PaddingMode.None);
    }

    private static int ReadKeyLength(long length)
    {
        // The length is in bits; some writers give the bytes of a crypt filter
        var bytes = length <= 16 ? length : length / 8;
        return (int)Math.Clamp(bytes, 5, 16);
    }

    private static CryptMethod GetCryptFilter(Dictionary<string, CryptMethod> filters, string? name)
    {
        if (name is null)
            return CryptMethod.None;

        return filters.TryGetValue(name, out var method) ? method : throw new InvalidPdfContentException($"The crypt filter '{name}' is not defined");
    }

    private static void AddCryptFilter(PdfDictionary dictionary, string method, int length)
    {
        dictionary[PdfName.Create("CF")] = new PdfDictionary
        {
            [PdfName.Create("StdCF")] = new PdfDictionary
            {
                [PdfName.Create("AuthEvent")] = PdfName.Create("DocOpen"),
                [PdfName.Create("CFM")] = PdfName.Create(method),
                [PdfNames.Length] = PdfInteger.Get(length),
            },
        };
        dictionary[PdfName.Create("StmF")] = PdfName.Create("StdCF");
        dictionary[PdfName.Create("StrF")] = PdfName.Create("StdCF");
    }

    /// <summary>A copy of the encryption dictionary with direct values, so that it can be written in another file.</summary>
    private static PdfDictionary CopyDirect(PdfDictionary dictionary)
    {
        static PdfObject Copy(PdfObject value, int depth)
        {
            value = PdfReference.ResolveIfReference(value);
            if (depth > 16)
                return PdfNull.Instance;

            switch (value)
            {
                case PdfDictionary d:
                    var result = new PdfDictionary();
                    foreach (var (key, item) in d)
                    {
                        result[key] = Copy(item, depth + 1);
                    }

                    return result;
                case PdfArray a:
                    var array = new PdfArray();
                    foreach (var item in a)
                    {
                        array.Add(Copy(item, depth + 1));
                    }

                    return array;
                default:
                    return value;
            }
        }

        return (PdfDictionary)Copy(dictionary, 0);
    }

    private static bool IsLegacyPassword(string password)
    {
        foreach (var c in password)
        {
            if (!PdfTextStringEncoding.TryEncodePdfDocEncoding(c, out _))
                return false;
        }

        return true;
    }

    /// <summary>Algorithm 13: the <c>/Perms</c> entry, decrypted with the file key, holds the permissions and the metadata flag.</summary>
    private static bool ArePermissionsAuthentic(PdfDictionary dictionary, byte[] fileKey, int permissions, bool encryptMetadata)
    {
        if (!dictionary.TryGetResolved<PdfString>(PdfName.Create("Perms"), out var perms) || perms.Span.Length < 16)
            return false;

        using var aes = Aes.Create();
        aes.Key = fileKey;
        var value = aes.DecryptEcb(perms.Span[..16], PaddingMode.None);
        return value[9] == 'a' && value[10] == 'd' && value[11] == 'b'
            && BinaryPrimitives.ReadInt32LittleEndian(value) == permissions
            && value[8] == (encryptMetadata ? 'T' : 'F');
    }

    /// <summary>The password of revisions 2 to 4 (7.6.4.3.2): its characters in PDFDocEncoding, padded or truncated to 32 bytes.</summary>
    private static byte[] GetLegacyPasswordBytes(string password)
    {
        var result = new byte[32];
        var length = 0;
        foreach (var c in password)
        {
            if (length == 32)
                break;

            result[length++] = PdfTextStringEncoding.TryEncodePdfDocEncoding(c, out var code) ? code : (byte)'?';
        }

        Padding[..(32 - length)].CopyTo(result.AsSpan(length));
        return result;
    }

    /// <summary>Algorithm 2: the file key of revisions 2 to 4, from the padded user password.</summary>
    private static byte[] ComputeFileKey(ReadOnlySpan<byte> password, ReadOnlySpan<byte> owner, int permissions, ReadOnlySpan<byte> id, int revision, int keyLength, bool encryptMetadata)
    {
        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        md5.AppendData(password);
        md5.AppendData(owner);
        Span<byte> p = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(p, permissions);
        md5.AppendData(p);
        md5.AppendData(id);
        if (revision >= 4 && !encryptMetadata)
        {
            md5.AppendData([0xFF, 0xFF, 0xFF, 0xFF]);
        }

        var hash = md5.GetHashAndReset();
        if (revision >= 3)
        {
            for (var i = 0; i < 50; i++)
            {
                hash = MD5.HashData(hash.AsSpan(0, keyLength));
            }
        }

        return hash[..(revision == 2 ? 5 : keyLength)];
    }

    /// <summary>Algorithms 4 and 5: the <c>/U</c> entry of revisions 2 to 4.</summary>
    private static byte[] ComputeUserEntry(byte[] fileKey, ReadOnlySpan<byte> id, int revision)
    {
        if (revision == 2)
            return Rc4.Transform(fileKey, Padding);

        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        md5.AppendData(Padding);
        md5.AppendData(id);
        var value = Rc4.Transform(fileKey, md5.GetHashAndReset());
        var key = new byte[fileKey.Length];
        for (var i = 1; i <= 19; i++)
        {
            for (var k = 0; k < key.Length; k++)
            {
                key[k] = (byte)(fileKey[k] ^ i);
            }

            value = Rc4.Transform(key, value);
        }

        // The last 16 bytes are arbitrary
        var result = new byte[32];
        value.CopyTo(result, 0);
        return result;
    }

    /// <summary>Algorithm 3, steps a to d: the RC4 key of the <c>/O</c> entry, from the padded owner password.</summary>
    private static byte[] ComputeOwnerKey(ReadOnlySpan<byte> ownerPassword, int revision, int keyLength)
    {
        var hash = MD5.HashData(ownerPassword);
        if (revision >= 3)
        {
            for (var i = 0; i < 50; i++)
            {
                hash = MD5.HashData(hash);
            }
        }

        return hash[..(revision == 2 ? 5 : keyLength)];
    }

    /// <summary>Algorithm 3, steps e to h: the <c>/O</c> entry, the user password encrypted with the owner key.</summary>
    private static byte[] ComputeOwnerEntry(byte[] ownerKey, ReadOnlySpan<byte> userPassword, int revision)
    {
        var value = Rc4.Transform(ownerKey, userPassword);
        if (revision >= 3)
        {
            var key = new byte[ownerKey.Length];
            for (var i = 1; i <= 19; i++)
            {
                for (var k = 0; k < key.Length; k++)
                {
                    key[k] = (byte)(ownerKey[k] ^ i);
                }

                value = Rc4.Transform(key, value);
            }
        }

        return value;
    }

    /// <summary>Algorithm 6: the file key when the padded password is the user password; <see langword="null"/> otherwise.</summary>
    private static byte[]? TryAuthenticateUser(ReadOnlySpan<byte> password, byte[] owner, byte[] user, int permissions, ReadOnlySpan<byte> id, int revision, int keyLength, bool encryptMetadata)
    {
        var fileKey = ComputeFileKey(password, owner, permissions, id, revision, keyLength, encryptMetadata);
        var expected = ComputeUserEntry(fileKey, id, revision);
        var compared = revision == 2 ? 32 : 16;
        return CryptographicOperations.FixedTimeEquals(expected.AsSpan(0, compared), user.AsSpan(0, compared)) ? fileKey : null;
    }

    /// <summary>Algorithm 7: the file key when the padded password is the owner password, which decrypts the user password; <see langword="null"/> otherwise.</summary>
    private static byte[]? TryAuthenticateOwner(ReadOnlySpan<byte> password, byte[] owner, byte[] user, int permissions, ReadOnlySpan<byte> id, int revision, int keyLength, bool encryptMetadata)
    {
        var ownerKey = ComputeOwnerKey(password, revision, keyLength);
        byte[] userPassword;
        if (revision == 2)
        {
            userPassword = Rc4.Transform(ownerKey, owner);
        }
        else
        {
            userPassword = owner;
            var key = new byte[ownerKey.Length];
            for (var i = 19; i >= 0; i--)
            {
                for (var k = 0; k < key.Length; k++)
                {
                    key[k] = (byte)(ownerKey[k] ^ i);
                }

                userPassword = Rc4.Transform(key, userPassword);
            }
        }

        return TryAuthenticateUser(userPassword, owner, user, permissions, id, revision, keyLength, encryptMetadata);
    }

    /// <summary>Algorithms 2.A and 11/12: the file key of revisions 5 and 6 when the password matches the owner or the user hash.</summary>
    private static byte[]? TryAuthenticateAes256(byte[] password, ReadOnlySpan<byte> owner, ReadOnlySpan<byte> user, byte[] encryptedKey, int revision, bool isOwnerPassword)
    {
        var entry = isOwnerPassword ? owner : user;
        ReadOnlySpan<byte> userData = isOwnerPassword ? user : [];
        var hash = HashAes256(password, entry.Slice(32, 8), userData, revision);
        if (!CryptographicOperations.FixedTimeEquals(hash, entry[..32]))
            return null;

        if (encryptedKey.Length < 32)
            throw new InvalidPdfContentException("The /OE or /UE entry of the encryption dictionary is too short");

        var key = HashAes256(password, entry.Slice(40, 8), userData, revision);
        using var aes = Aes.Create();
        aes.Key = key;
        return aes.DecryptCbc(encryptedKey.AsSpan(0, 32), new byte[16], PaddingMode.None);
    }

    /// <summary>Algorithm 2.B (revision 6): the hash of a password, a salt and the user entry; revision 5 uses SHA-256 alone.</summary>
    private static byte[] HashAes256(ReadOnlySpan<byte> password, ReadOnlySpan<byte> salt, ReadOnlySpan<byte> userData, int revision)
    {
        var input = new byte[password.Length + salt.Length + userData.Length];
        password.CopyTo(input);
        salt.CopyTo(input.AsSpan(password.Length));
        userData.CopyTo(input.AsSpan(password.Length + salt.Length));
        var k = SHA256.HashData(input);
        if (revision == 5)
            return k;

        using var aes = Aes.Create();
        var round = 0;
        while (true)
        {
            // K1: the password, K and the user data, repeated 64 times, encrypted with the first half of K
            var blockLength = password.Length + k.Length + userData.Length;
            var k1 = new byte[blockLength * 64];
            for (var i = 0; i < 64; i++)
            {
                var block = k1.AsSpan(i * blockLength, blockLength);
                password.CopyTo(block);
                k.CopyTo(block[password.Length..]);
                userData.CopyTo(block[(password.Length + k.Length)..]);
            }

            aes.Key = k[..16];
            var e = aes.EncryptCbc(k1, k.AsSpan(16, 16), PaddingMode.None);

            // The first 16 bytes of E, as a number, modulo 3: as 256 is 1 modulo 3, the sum of the bytes
            var sum = 0;
            for (var i = 0; i < 16; i++)
            {
                sum += e[i];
            }

            k = (sum % 3) switch
            {
                0 => SHA256.HashData(e),
                1 => SHA384.HashData(e),
                _ => SHA512.HashData(e),
            };
            round++;
            if (round >= 64 && e[^1] <= round - 32)
                break;
        }

        return k[..32];
    }
}
