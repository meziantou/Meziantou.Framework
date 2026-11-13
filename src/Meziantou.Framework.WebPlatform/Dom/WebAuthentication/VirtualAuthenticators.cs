using System.Security.Cryptography;
using Meziantou.Framework.WebPlatform.Cryptography;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The host hooks of the Web Authentication API (https://w3c.github.io/webauthn/, <see cref="BrowsingContextOptions.WebAuthentication"/>).</summary>
public sealed class WebAuthenticationOptions
{
    /// <summary>
    /// Gets or sets the authenticators available to the documents (<c>navigator.credentials.create()</c> and
    /// <c>navigator.credentials.get()</c>): software authenticators that the application adds and configures, as the
    /// virtual authenticators of WebDriver (https://w3c.github.io/webauthn/#sctn-automation-virtual-authenticators). An
    /// environment can be shared by several browsing contexts. The default is null: no authenticator is available, so the
    /// ceremonies fail with a <c>NotAllowedError</c>.
    /// </summary>
    public VirtualAuthenticatorEnvironment? Authenticators { get; init; }
}

/// <summary>The protocol that a virtual authenticator speaks (https://w3c.github.io/webauthn/#sctn-automation-virtual-authenticators).</summary>
public enum VirtualAuthenticatorProtocol
{
    /// <summary><c>ctap1/u2f</c>: an authenticator of the FIDO U2F protocol: no discoverable credential, no user verification, no extension, ES256 only.</summary>
    Ctap1U2f,

    /// <summary><c>ctap2</c>: an authenticator of CTAP 2.0.</summary>
    Ctap2,

    /// <summary><c>ctap2_1</c>: an authenticator of CTAP 2.1.</summary>
    Ctap21,
}

/// <summary>The configuration of a virtual authenticator (https://w3c.github.io/webauthn/#authenticator-configuration).</summary>
public sealed class VirtualAuthenticatorOptions
{
    /// <summary>The extension identifiers that the virtual authenticators support (the <c>prf</c> and <c>largeBlob</c> extensions).</summary>
    public static IReadOnlyList<string> SupportedExtensions { get; } = ["largeBlob", "prf"];

    /// <summary>Gets the protocol of the authenticator.</summary>
    public VirtualAuthenticatorProtocol Protocol { get; init; } = VirtualAuthenticatorProtocol.Ctap2;

    /// <summary>
    /// Gets the transport of the authenticator, a value of <c>AuthenticatorTransport</c> (<c>usb</c>, <c>nfc</c>,
    /// <c>ble</c>, <c>smart-card</c>, <c>hybrid</c> or <c>internal</c>). An <c>internal</c> authenticator is a platform
    /// authenticator, the others are roaming authenticators. The default is <c>usb</c>.
    /// </summary>
    public string Transport { get; init; } = "usb";

    /// <summary>Gets whether the authenticator can store client-side discoverable credentials (resident keys).</summary>
    public bool HasResidentKey { get; init; }

    /// <summary>Gets whether the authenticator can perform user verification.</summary>
    public bool HasUserVerification { get; init; }

    /// <summary>Gets whether the user consents to the operations of the authenticator (the test of user presence). The default is true.</summary>
    public bool IsUserConsenting { get; init; } = true;

    /// <summary>Gets whether the user verification succeeds.</summary>
    public bool IsUserVerified { get; init; }

    /// <summary>Gets the authenticator extensions that the authenticator supports: values of <see cref="SupportedExtensions"/>.</summary>
    public IReadOnlyList<string> Extensions { get; init; } = [];

    /// <summary>Gets the default backup eligibility of the credentials that the authenticator creates.</summary>
    public bool DefaultBackupEligibility { get; init; }

    /// <summary>Gets the default backup state of the credentials that the authenticator creates.</summary>
    public bool DefaultBackupState { get; init; }
}

/// <summary>
/// A public key credential source of a virtual authenticator, as the credential parameters of WebDriver
/// (https://w3c.github.io/webauthn/#credential-parameters): a snapshot returned by <see cref="VirtualAuthenticator.GetCredentials"/>,
/// or a credential given to <see cref="VirtualAuthenticator.AddCredential"/>.
/// </summary>
public sealed class VirtualCredential
{
    /// <summary>Gets the credential ID.</summary>
    public required ReadOnlyMemory<byte> CredentialId { get; init; }

    /// <summary>Gets whether the credential is a client-side discoverable credential (a resident key).</summary>
    public bool IsResidentCredential { get; init; }

    /// <summary>Gets the RP ID that the credential is scoped to.</summary>
    public required string RpId { get; init; }

    /// <summary>Gets the private key, as an asymmetric key package (RFC 5958, PKCS #8): an ECDSA key on the P-256 curve, an RSA key or an Ed25519 key.</summary>
    public required ReadOnlyMemory<byte> PrivateKey { get; init; }

    /// <summary>Gets the user handle of the credential, or null. A discoverable credential must have one.</summary>
    public ReadOnlyMemory<byte>? UserHandle { get; init; }

    /// <summary>Gets the signature counter of the credential, or null when the credential has no counter.</summary>
    public uint? SignCount { get; init; }

    /// <summary>Gets the large blob of the credential (the <c>largeBlob</c> extension), or null.</summary>
    public ReadOnlyMemory<byte>? LargeBlob { get; init; }

    /// <summary>Gets the backup eligibility of the credential, or null for the default of the authenticator.</summary>
    public bool? BackupEligibility { get; init; }

    /// <summary>Gets the backup state of the credential, or null for the default of the authenticator.</summary>
    public bool? BackupState { get; init; }

    /// <summary>Gets the name of the user of the credential.</summary>
    public string UserName { get; init; } = "";

    /// <summary>Gets the display name of the user of the credential.</summary>
    public string UserDisplayName { get; init; } = "";
}

/// <summary>
/// The virtual authenticator database (https://w3c.github.io/webauthn/#virtual-authenticator-database): the software
/// authenticators available to the browsing contexts whose <see cref="WebAuthenticationOptions.Authenticators"/> is this
/// environment.
/// </summary>
/// <remarks>The environment can be used from any thread.</remarks>
public sealed class VirtualAuthenticatorEnvironment
{
    private readonly Lock _lock = new();
    private readonly List<VirtualAuthenticator> _authenticators = [];
    private readonly Dictionary<string, long> _authentications = new(StringComparer.Ordinal);

    /// <summary>Gets the authenticators, in the order they were added.</summary>
    public IReadOnlyList<VirtualAuthenticator> Authenticators
    {
        get
        {
            lock (_lock)
            {
                return [.. _authenticators];
            }
        }
    }

    /// <summary>Adds a virtual authenticator (https://w3c.github.io/webauthn/#add-virtual-authenticator).</summary>
    /// <exception cref="ArgumentException">An extension is not supported, or the transport is not a value of <c>AuthenticatorTransport</c>.</exception>
    public VirtualAuthenticator AddAuthenticator(VirtualAuthenticatorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Transport is not ("usb" or "nfc" or "ble" or "smart-card" or "hybrid" or "internal"))
            throw new ArgumentException($"'{options.Transport}' is not an authenticator transport", nameof(options));

        foreach (var extension in options.Extensions)
        {
            if (!VirtualAuthenticatorOptions.SupportedExtensions.Contains(extension, StringComparer.Ordinal))
                throw new ArgumentException($"The '{extension}' extension is not supported by the virtual authenticators", nameof(options));
        }

        var authenticator = new VirtualAuthenticator(Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)), options);
        lock (_lock)
        {
            _authenticators.Add(authenticator);
        }

        return authenticator;
    }

    /// <summary>Removes a virtual authenticator (https://w3c.github.io/webauthn/#remove-virtual-authenticator). Returns false when there is no authenticator with this id.</summary>
    public bool RemoveAuthenticator(string authenticatorId)
    {
        lock (_lock)
        {
            return _authenticators.RemoveAll(authenticator => authenticator.Id == authenticatorId) > 0;
        }
    }

    /// <summary>Returns the authenticator with this id, or null.</summary>
    public VirtualAuthenticator? GetAuthenticator(string authenticatorId)
    {
        lock (_lock)
        {
            return _authenticators.Find(authenticator => authenticator.Id == authenticatorId);
        }
    }

    /// <summary>Records that the user agent mediated an authentication for an origin (the condition of a conditional create).</summary>
    internal void RecordAuthentication(string origin, long timestamp)
    {
        lock (_lock)
        {
            _authentications[origin] = timestamp;
        }
    }

    /// <summary>The timestamp of the last authentication mediated for an origin, or null.</summary>
    internal long? GetLastAuthentication(string origin)
    {
        lock (_lock)
        {
            return _authentications.TryGetValue(origin, out var timestamp) ? timestamp : null;
        }
    }
}

/// <summary>
/// A software authenticator (https://w3c.github.io/webauthn/#virtual-authenticators): it creates and stores public key
/// credential sources, and signs the assertions with them. Its credentials can be added, listed and removed by the
/// application, as with the WebDriver extension commands.
/// </summary>
/// <remarks>The authenticator can be used from any thread.</remarks>
public sealed class VirtualAuthenticator
{
    private readonly Lock _lock = new();
    private readonly List<StoredCredential> _credentials = [];
    private volatile bool _isUserConsenting;
    private volatile bool _isUserVerified;

    internal VirtualAuthenticator(string id, VirtualAuthenticatorOptions options)
    {
        Id = id;
        Protocol = options.Protocol;
        Transport = options.Transport;
        HasResidentKey = options.HasResidentKey;
        HasUserVerification = options.HasUserVerification;
        _isUserConsenting = options.IsUserConsenting;
        _isUserVerified = options.IsUserVerified;
        Extensions = [.. options.Extensions];
        DefaultBackupEligibility = options.DefaultBackupEligibility;
        DefaultBackupState = options.DefaultBackupState;
    }

    /// <summary>Gets the id of the authenticator (https://w3c.github.io/webauthn/#authenticatorid).</summary>
    public string Id { get; }

    /// <summary>Gets the protocol of the authenticator.</summary>
    public VirtualAuthenticatorProtocol Protocol { get; }

    /// <summary>Gets the transport of the authenticator.</summary>
    public string Transport { get; }

    /// <summary>Gets whether the authenticator can store client-side discoverable credentials.</summary>
    public bool HasResidentKey { get; }

    /// <summary>Gets whether the authenticator can perform user verification.</summary>
    public bool HasUserVerification { get; }

    /// <summary>Gets or sets whether the user consents to the operations of the authenticator.</summary>
    public bool IsUserConsenting
    {
        get => _isUserConsenting;
        set => _isUserConsenting = value;
    }

    /// <summary>Gets or sets whether the user verification succeeds (https://w3c.github.io/webauthn/#set-user-verified).</summary>
    public bool IsUserVerified
    {
        get => _isUserVerified;
        set => _isUserVerified = value;
    }

    /// <summary>Gets the authenticator extensions that the authenticator supports.</summary>
    public IReadOnlyList<string> Extensions { get; }

    /// <summary>Gets the default backup eligibility of the credentials that the authenticator creates.</summary>
    public bool DefaultBackupEligibility { get; }

    /// <summary>Gets the default backup state of the credentials that the authenticator creates.</summary>
    public bool DefaultBackupState { get; }

    /// <summary>Whether the authenticator is a platform authenticator (its transport is <c>internal</c>).</summary>
    internal bool IsPlatform => Transport == "internal";

    /// <summary>https://w3c.github.io/webauthn/#authenticator-attachment-modality</summary>
    internal string Attachment => IsPlatform ? "platform" : "cross-platform";

    /// <summary>Whether the authenticator can store discoverable credentials (a U2F authenticator cannot).</summary>
    internal bool SupportsResidentKeys => HasResidentKey && Protocol != VirtualAuthenticatorProtocol.Ctap1U2f;

    /// <summary>Whether the authenticator can perform user verification (a U2F authenticator cannot).</summary>
    internal bool SupportsUserVerification => HasUserVerification && Protocol != VirtualAuthenticatorProtocol.Ctap1U2f;

    internal bool SupportsExtension(string extension) => Protocol != VirtualAuthenticatorProtocol.Ctap1U2f && Extensions.Contains(extension, StringComparer.Ordinal);

    internal bool SupportsAlgorithm(int algorithm) => Protocol == VirtualAuthenticatorProtocol.Ctap1U2f ? algorithm == CredentialKey.ES256 : CredentialKey.IsSupported(algorithm);

    /// <summary>
    /// Adds a credential to the authenticator (https://w3c.github.io/webauthn/#add-credential). A discoverable credential of
    /// the same RP ID and user handle replaces the existing one.
    /// </summary>
    /// <exception cref="ArgumentException">The private key is not valid, or the credential is discoverable and the authenticator does not support them or it has no user handle.</exception>
    public void AddCredential(VirtualCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);
        if (credential.IsResidentCredential && credential.UserHandle is null)
            throw new ArgumentException("A discoverable credential must have a user handle", nameof(credential));

        if (credential.IsResidentCredential && !HasResidentKey)
            throw new ArgumentException("The authenticator does not support discoverable credentials", nameof(credential));

        CredentialKey key;
        try
        {
            key = CredentialKey.Import(credential.PrivateKey.Span);
        }
        catch (Exception ex) when (ex is CryptographicException or WebCryptoException)
        {
            throw new ArgumentException("The private key is not a valid asymmetric key package: " + ex.Message, nameof(credential), ex);
        }

        Store(new StoredCredential(credential.CredentialId.ToArray(), credential.RpId, key)
        {
            IsResident = credential.IsResidentCredential,
            UserHandle = credential.UserHandle?.ToArray(),
            SignCount = credential.SignCount,
            LargeBlob = SupportsExtension("largeBlob") ? credential.LargeBlob?.ToArray() : null,
            BackupEligibility = credential.BackupEligibility ?? DefaultBackupEligibility,
            BackupState = credential.BackupState ?? DefaultBackupState,
            UserName = credential.UserName,
            UserDisplayName = credential.UserDisplayName,
        });
    }

    /// <summary>Returns a snapshot of the credentials of the authenticator (https://w3c.github.io/webauthn/#get-credentials).</summary>
    public IReadOnlyList<VirtualCredential> GetCredentials()
    {
        lock (_lock)
        {
            return [.. _credentials.Select(credential => new VirtualCredential
            {
                CredentialId = credential.Id.ToArray(),
                IsResidentCredential = credential.IsResident,
                RpId = credential.RpId,
                PrivateKey = credential.Key.ExportPkcs8(),
                UserHandle = credential.UserHandle is { } userHandle ? (ReadOnlyMemory<byte>?)userHandle.ToArray() : null,
                SignCount = credential.SignCount,
                LargeBlob = credential.LargeBlob is { } largeBlob ? (ReadOnlyMemory<byte>?)largeBlob.ToArray() : null,
                BackupEligibility = credential.BackupEligibility,
                BackupState = credential.BackupState,
                UserName = credential.UserName,
                UserDisplayName = credential.UserDisplayName,
            })];
        }
    }

    /// <summary>Removes a credential (https://w3c.github.io/webauthn/#remove-credential). Returns false when the authenticator has no credential with this id.</summary>
    public bool RemoveCredential(ReadOnlySpan<byte> credentialId)
    {
        lock (_lock)
        {
            for (var i = 0; i < _credentials.Count; i++)
            {
                if (credentialId.SequenceEqual(_credentials[i].Id))
                {
                    _credentials.RemoveAt(i);
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Removes all the credentials (https://w3c.github.io/webauthn/#remove-all-credentials).</summary>
    public void RemoveAllCredentials()
    {
        lock (_lock)
        {
            _credentials.Clear();
        }
    }

    /// <summary>
    /// Sets the properties of a credential (https://w3c.github.io/webauthn/#set-credential-properties): the null values are
    /// not changed. Returns false when the authenticator has no credential with this id.
    /// </summary>
    /// <param name="credentialId">The id of the credential.</param>
    /// <param name="backupEligibility">The backup eligibility, or null.</param>
    /// <param name="backupState">The backup state, or null.</param>
    public bool SetCredentialProperties(ReadOnlySpan<byte> credentialId, bool? backupEligibility, bool? backupState)
    {
        lock (_lock)
        {
            if (Find(credentialId) is not { } credential)
                return false;

            credential.BackupEligibility = backupEligibility ?? credential.BackupEligibility;
            credential.BackupState = backupState ?? credential.BackupState;
            return true;
        }
    }

    /// <summary>
    /// Sets the signature counter of a credential (https://w3c.github.io/webauthn/#set-credential-properties): null removes
    /// the counter. Returns false when the authenticator has no credential with this id.
    /// </summary>
    public bool SetSignCount(ReadOnlySpan<byte> credentialId, uint? signCount)
    {
        lock (_lock)
        {
            if (Find(credentialId) is not { } credential)
                return false;

            credential.SignCount = signCount;
            return true;
        }
    }

    private StoredCredential? Find(ReadOnlySpan<byte> credentialId)
    {
        foreach (var credential in _credentials)
        {
            if (credentialId.SequenceEqual(credential.Id))
                return credential;
        }

        return null;
    }

    internal void Store(StoredCredential credential)
    {
        lock (_lock)
        {
            _credentials.RemoveAll(existing => existing.Id.AsSpan().SequenceEqual(credential.Id)
                || (credential.IsResident && existing.IsResident && existing.RpId == credential.RpId && existing.UserHandle.AsSpan().SequenceEqual(credential.UserHandle)));
            _credentials.Add(credential);
        }
    }

    /// <summary>https://w3c.github.io/webauthn/#credential-id-lookup: the credential of an RP ID with this id, or null.</summary>
    internal StoredCredential? Lookup(ReadOnlySpan<byte> credentialId, string rpId)
    {
        lock (_lock)
        {
            return Find(credentialId) is { } credential && credential.RpId == rpId ? credential : null;
        }
    }

    /// <summary>The discoverable credentials of an RP ID, the most recent first.</summary>
    internal List<StoredCredential> GetDiscoverableCredentials(string rpId)
    {
        lock (_lock)
        {
            var result = _credentials.Where(credential => credential.IsResident && credential.RpId == rpId).ToList();
            result.Reverse();
            return result;
        }
    }

    /// <summary>Removes the credentials that match a predicate (the authenticator actions of the signal methods).</summary>
    internal void RemoveCredentials(Predicate<StoredCredential> predicate)
    {
        lock (_lock)
        {
            _credentials.RemoveAll(predicate);
        }
    }

    /// <summary>Runs an action on the credentials while they are locked (the authenticator actions of the signal methods).</summary>
    internal void UpdateCredentials(Action<StoredCredential> action)
    {
        lock (_lock)
        {
            foreach (var credential in _credentials)
            {
                action(credential);
            }
        }
    }

    /// <summary>Increments the signature counter of a credential, and returns the value of the authenticator data (0 without counter).</summary>
    internal uint IncrementSignCount(StoredCredential credential)
    {
        lock (_lock)
        {
            if (credential.SignCount is not { } count)
                return 0;

            count = count == uint.MaxValue ? count : count + 1;
            credential.SignCount = count;
            return count;
        }
    }
}

/// <summary>A public key credential source of a virtual authenticator (https://w3c.github.io/webauthn/#public-key-credential-source).</summary>
internal sealed class StoredCredential(byte[] id, string rpId, CredentialKey key)
{
    public byte[] Id { get; } = id;

    public string RpId { get; } = rpId;

    public CredentialKey Key { get; } = key;

    public bool IsResident { get; init; }

    public byte[]? UserHandle { get; init; }

    public uint? SignCount { get; set; }

    public byte[]? LargeBlob { get; set; }

    public bool BackupEligibility { get; set; }

    public bool BackupState { get; set; }

    public string UserName { get; set; } = "";

    public string UserDisplayName { get; set; } = "";

    /// <summary>The secret of the pseudo-random function of the credential (https://w3c.github.io/webauthn/#prf-extension), created when it is first used.</summary>
    public byte[] PrfSecret => field ??= RandomNumberGenerator.GetBytes(32);
}
