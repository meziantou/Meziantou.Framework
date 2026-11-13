using System.Security.Cryptography;
using System.Text;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The client of Web Authentication (https://w3c.github.io/webauthn/#webauthn-client): the [[Create]] and
/// [[DiscoverFromExternalSource]] internal methods of <see cref="PublicKeyCredential"/>, and the authenticator operations of
/// the virtual authenticators of <see cref="WebAuthenticationOptions.Authenticators"/>.
/// </summary>
/// <remarks>
/// The ceremonies are performed by a task of the event loop: the client issues the request to the authenticators that are
/// available at that time, in the order they were added, and the first one that succeeds completes the ceremony. When none of
/// them can (no authenticator, no eligible authenticator, or an error of each of them), the client behaves as a user who
/// cancels the dialog of the user agent: the ceremony fails with a <c>NotAllowedError</c>, without waiting for the end of
/// the timeout. A request with the conditional mediation waits for an interaction of the user with an autofill
/// form control, which a virtual environment never performs: it is pending until it is aborted.
/// </remarks>
internal static class WebAuthenticationClient
{
    /// <summary>The time during which an authentication allows a conditional create (https://w3c.github.io/webauthn/#sctn-createCredential, step 21).</summary>
    private static readonly TimeSpan ConditionalCreateWindow = TimeSpan.FromMinutes(5);

    private static readonly string[] DefaultCredentialAlgorithms = ["public-key"];

    public static Exception ToException(object? reason) => reason as Exception ?? new AbortSignalException(reason);

    /// <summary>https://w3c.github.io/webauthn/#sctn-createCredential</summary>
    public static Task<Credential?> Create(Window window, CredentialCreationOptions options, bool sameOriginWithAncestors)
    {
        var publicKey = Snapshot(options.PublicKey!);
        CreateRequest request;
        try
        {
            request = PrepareCreate(window, options, publicKey, sameOriginWithAncestors);
        }
        catch (Exception ex) when (ex is DomException or TypeErrorException or AbortSignalException || ReferenceEquals(ex, options.Signal?.Reason))
        {
            return RejectByTask(window, ex);
        }

        return Run(window, options.Signal, pending: false, () => RunCreate(window, request));
    }

    /// <summary>https://w3c.github.io/webauthn/#sctn-discover-from-external-source</summary>
    public static Task<Credential?> DiscoverFromExternalSource(Window window, CredentialRequestOptions options, bool sameOriginWithAncestors)
    {
        var publicKey = Snapshot(options.PublicKey!);
        GetRequest request;
        try
        {
            request = PrepareGet(window, options, publicKey, sameOriginWithAncestors);
        }
        catch (Exception ex) when (ex is DomException or TypeErrorException or AbortSignalException || ReferenceEquals(ex, options.Signal?.Reason))
        {
            return RejectByTask(window, ex);
        }

        // The conditional mediation waits for the user to select a discovered credential in an autofill form control
        var conditional = options.Mediation == CredentialMediationRequirement.Conditional;
        return Run(window, options.Signal, pending: conditional, () => RunGet(window, request));
    }

    // "All BufferSource objects used in this algorithm MUST be snapshotted when the algorithm begins": the bytes of the buffer
    // sources of the scripts are copied
    private static PublicKeyCredentialCreationOptions Snapshot(PublicKeyCredentialCreationOptions options) => new()
    {
        Rp = options.Rp,
        User = new PublicKeyCredentialUserEntity { Id = options.User.Id.ToArray(), Name = options.User.Name, DisplayName = options.User.DisplayName },
        Challenge = options.Challenge.ToArray(),
        PubKeyCredParams = options.PubKeyCredParams,
        Timeout = options.Timeout,
        ExcludeCredentials = [.. options.ExcludeCredentials.Select(Snapshot)],
        AuthenticatorSelection = options.AuthenticatorSelection,
        Hints = options.Hints,
        Attestation = options.Attestation,
        AttestationFormats = options.AttestationFormats,
        Extensions = Snapshot(options.Extensions),
    };

    private static PublicKeyCredentialRequestOptions Snapshot(PublicKeyCredentialRequestOptions options) => new()
    {
        Challenge = options.Challenge.ToArray(),
        Timeout = options.Timeout,
        RpId = options.RpId,
        AllowCredentials = [.. options.AllowCredentials.Select(Snapshot)],
        UserVerification = options.UserVerification,
        Hints = options.Hints,
        Extensions = Snapshot(options.Extensions),
    };

    private static PublicKeyCredentialDescriptor Snapshot(PublicKeyCredentialDescriptor descriptor) => new()
    {
        Type = descriptor.Type,
        Id = descriptor.Id.ToArray(),
        Transports = descriptor.Transports,
    };

    private static AuthenticationExtensionsPrfValues Snapshot(AuthenticationExtensionsPrfValues values) => new()
    {
        First = values.First.ToArray(),
        Second = values.Second is { } second ? (ReadOnlyMemory<byte>?)second.ToArray() : null,
    };

    private static AuthenticationExtensionsClientInputs? Snapshot(AuthenticationExtensionsClientInputs? extensions) => extensions is null ? null : new()
    {
        Appid = extensions.Appid,
        AppidExclude = extensions.AppidExclude,
        CredProps = extensions.CredProps,
        Prf = extensions.Prf is { } prf ? new AuthenticationExtensionsPrfInputs
        {
            Eval = prf.Eval is { } eval ? Snapshot(eval) : null,
            EvalByCredential = prf.EvalByCredential is { } evalByCredential ? [.. evalByCredential.Select(entry => KeyValuePair.Create(entry.Key, Snapshot(entry.Value)))] : null,
        } : null,
        LargeBlob = extensions.LargeBlob is { } largeBlob ? new AuthenticationExtensionsLargeBlobInputs
        {
            Support = largeBlob.Support,
            Read = largeBlob.Read,
            Write = largeBlob.Write is { } write ? (ReadOnlyMemory<byte>?)write.ToArray() : null,
        } : null,
        RemoteClientDataJson = extensions.RemoteClientDataJson,
    };

    private static Task<Credential?> RejectByTask(Window window, Exception exception)
    {
        var result = new TaskCompletionSource<Credential?>();
        window.QueueGlobalTask(() => result.TrySetException(exception));
        return result.Task;
    }

    /// <summary>Runs a ceremony from a task, unless the signal is aborted before (its abort reason rejects the promise).</summary>
    private static Task<Credential?> Run(Window window, AbortSignal? signal, bool pending, Func<Credential> ceremony)
    {
        var result = new TaskCompletionSource<Credential?>();
        Action? abort = null;
        if (signal is not null)
        {
            abort = () => result.TrySetException(ToException(signal.Reason));
            signal.AddAbortAlgorithm(abort);
        }

        if (pending)
            return result.Task;

        window.QueueGlobalTask(() =>
        {
            if (abort is not null)
            {
                signal!.RemoveAbortAlgorithm(abort);
            }

            if (result.Task.IsCompleted)
                return;

            try
            {
                result.TrySetResult(ceremony());
            }
            catch (Exception ex) when (ex is DomException or TypeErrorException)
            {
                result.TrySetException(ex);
            }
        });
        return result.Task;
    }

    /// <summary>The RP ID of a ceremony: the effective domain of the origin, or an RP ID that is a registrable domain suffix of it.</summary>
    private static string GetRpId(OriginRecord origin, string? requestedRpId)
    {
        if (origin.EffectiveDomain is not { Kind: UrlHostKind.Domain } effectiveDomain)
            throw new DomException("The effective domain of the origin is not a valid domain", "SecurityError");

        if (requestedRpId is null)
            return effectiveDomain.Serialized;

        if (!Document.IsRegistrableDomainSuffixOfOrIsEqualTo(requestedRpId, effectiveDomain))
            throw new DomException($"'{requestedRpId}' is not a registrable domain suffix of '{effectiveDomain.Serialized}'", "SecurityError");

        return requestedRpId;
    }

    private static byte[] SerializeClientData(Window window, string type, ReadOnlySpan<byte> challenge, bool sameOriginWithAncestors)
    {
        var origin = window.Document.Origin;
        var topOrigin = sameOriginWithAncestors ? null : (window.Document.BrowsingContext?.Top.Document.Origin ?? origin).Serialize();
        return WebAuthenticationEncoding.SerializeClientData(type, challenge, origin.Serialize(), crossOrigin: !sameOriginWithAncestors, topOrigin);
    }

    private static IReadOnlyList<VirtualAuthenticator> GetAuthenticators(GlobalScope? global) => global?.Options.WebAuthentication?.Authenticators?.Authenticators ?? [];

    /// <summary>
    /// The DOMString members that take the values of an enumeration ignore the unknown values
    /// (https://w3c.github.io/webauthn/#sct-domstring-backwards-compatibility): the member is then not present.
    /// </summary>
    private static string? Known(string? value, params ReadOnlySpan<string> values)
    {
        foreach (var known in values)
        {
            if (value == known)
                return value;
        }

        return null;
    }

    private static string UserVerificationRequirement(string value) => Known(value, "required", "preferred", "discouraged") ?? "preferred";

    // ----------------------------------------------------------------------------------------------------------------
    // Registration
    // ----------------------------------------------------------------------------------------------------------------

    private sealed record CreateRequest(
        PublicKeyCredentialCreationOptions Options,
        string RpId,
        List<int> Algorithms,
        byte[] ClientDataJson,
        byte[] ClientDataHash,
        bool Conditional,
        bool CredProps,
        bool Prf,
        byte[]? PrfSalt1,
        byte[]? PrfSalt2,
        bool LargeBlob,
        bool LargeBlobRequired);

    private static CreateRequest PrepareCreate(Window window, CredentialCreationOptions options, PublicKeyCredentialCreationOptions publicKey, bool sameOriginWithAncestors)
    {
        var conditional = options.Mediation == CredentialMediationRequirement.Conditional;
        if (!sameOriginWithAncestors)
        {
            if (conditional)
                throw DomException.NotAllowed("A conditional create is not allowed in a cross-origin context");

            if (!window.HasTransientActivation)
                throw DomException.NotAllowed("A credential can only be created by a cross-origin context with a transient activation");

            window.ConsumeUserActivation();
        }

        if (publicKey.User.Id.Length is < 1 or > 64)
            throw new TypeErrorException("The user ID must be between 1 and 64 bytes long");

        var origin = window.Document.Origin;
        if (origin.IsOpaque)
            throw DomException.NotAllowed("The origin of the caller is opaque");

        var rpId = GetRpId(origin, publicKey.Rp.Id);

        List<int> algorithms;
        if (publicKey.PubKeyCredParams.Count == 0)
        {
            algorithms = [CredentialKey.ES256, CredentialKey.RS256];
        }
        else
        {
            algorithms = [.. publicKey.PubKeyCredParams.Where(parameter => DefaultCredentialAlgorithms.Contains(parameter.Type, StringComparer.Ordinal)).Select(parameter => parameter.Alg)];
            if (algorithms.Count == 0)
                throw DomException.NotSupported("No parameter of pubKeyCredParams has the public-key type");
        }

        // The registration extensions supported by the client (https://w3c.github.io/webauthn/#sctn-defined-client-extensions)
        var extensions = publicKey.Extensions;
        byte[]? salt1 = null;
        byte[]? salt2 = null;
        if (extensions?.Prf is { } prf)
        {
            if (prf.EvalByCredential is not null)
                throw DomException.NotSupported("evalByCredential of the prf extension is only valid for an authentication");

            if (prf.Eval is { } eval)
            {
                salt1 = PrfSalt(eval.First.Span);
                salt2 = eval.Second is { } second ? PrfSalt(second.Span) : null;
            }
        }

        if (extensions?.LargeBlob is { } largeBlob && (largeBlob.Read is not null || largeBlob.Write is not null))
            throw DomException.NotSupported("read and write of the largeBlob extension are only valid for an authentication");

        var clientDataJson = SerializeClientData(window, "webauthn.create", publicKey.Challenge.Span, sameOriginWithAncestors);
        if (options.Signal is { Aborted: true } signal)
            throw ToException(signal.Reason);

        if (conditional)
        {
            // The user agent must have recently mediated an authentication of the origin
            var serializedOrigin = origin.Serialize();
            var last = window.Options.WebAuthentication?.Authenticators?.GetLastAuthentication(serializedOrigin);
            if (last is not { } timestamp || window.EventLoop.TimeProvider.GetElapsedTime(timestamp) > ConditionalCreateWindow)
                throw DomException.NotAllowed("The user agent has not recently mediated an authentication for this origin");
        }

        return new CreateRequest(
            publicKey,
            rpId,
            algorithms,
            clientDataJson,
            SHA256.HashData(clientDataJson),
            conditional,
            CredProps: extensions?.CredProps is true,
            Prf: extensions?.Prf is not null,
            salt1,
            salt2,
            LargeBlob: extensions?.LargeBlob is not null,
            LargeBlobRequired: extensions?.LargeBlob?.Support == "required");
    }

    private static PublicKeyCredential RunCreate(Window window, CreateRequest request)
    {
        var options = request.Options;
        var selection = options.AuthenticatorSelection;
        var attachment = Known(selection?.AuthenticatorAttachment, "platform", "cross-platform");
        var residentKey = Known(selection?.ResidentKey, "discouraged", "preferred", "required");
        var userVerificationRequirement = UserVerificationRequirement(selection?.UserVerification ?? "preferred");
        var attestation = Known(options.Attestation, "none", "indirect", "direct", "enterprise") ?? "none";
        foreach (var authenticator in GetAuthenticators(window))
        {
            // The candidate authenticator (https://w3c.github.io/webauthn/#create-candidate-authenticator)
            if (request.LargeBlobRequired && !authenticator.SupportsExtension("largeBlob"))
                continue;

            if (selection is not null)
            {
                if (attachment is not null && attachment != authenticator.Attachment)
                    continue;

                if ((residentKey == "required" || (residentKey is null && selection.RequireResidentKey)) && !authenticator.SupportsResidentKeys)
                    continue;

                if (userVerificationRequirement == "required" && !authenticator.SupportsUserVerification)
                    continue;
            }

            // https://w3c.github.io/webauthn/#effective-resident-key-requirement-for-credential-creation
            var requireResidentKey = residentKey switch
            {
                "required" => true,
                "preferred" => authenticator.SupportsResidentKeys,
                "discouraged" => false,
                _ => selection?.RequireResidentKey ?? false,
            };

            // https://w3c.github.io/webauthn/#effective-user-verification-requirement-for-credential-creation: the conditional
            // create does not ask for the user presence nor the user verification, unless it is required
            var userVerification = userVerificationRequirement switch
            {
                "required" => true,
                "preferred" => authenticator.SupportsUserVerification && !request.Conditional,
                _ => false,
            };

            var attestationFormats = attestation == "none" ? (IReadOnlyList<string>)["none"] : options.AttestationFormats;
            var result = MakeCredential(authenticator, request, requireResidentKey, requireUserPresence: !request.Conditional, userVerification, attestationFormats);
            if (result.Error == "InvalidStateError")
                throw DomException.InvalidState("The authenticator contains one of the excluded credentials");

            if (result.Error is not null)
                continue;

            var credential = result.Credential!;
            var outputs = new AuthenticationExtensionsClientOutputs
            {
                // https://w3c.github.io/webauthn/#sctn-authenticator-credential-properties-extension: a CTAP 2.0 authenticator that
                // supports the discoverable credentials may create one even when it is not requested, so the property is unknown
                CredProps = request.CredProps
                    ? new CredentialPropertiesOutput { Rk = authenticator.Protocol == VirtualAuthenticatorProtocol.Ctap2 && authenticator.SupportsResidentKeys && !requireResidentKey ? null : requireResidentKey }
                    : null,
                Prf = request.Prf ? CreatePrfOutputs(authenticator, credential, request.PrfSalt1, request.PrfSalt2, enabled: authenticator.SupportsExtension("prf")) : null,
                LargeBlob = request.LargeBlob ? new AuthenticationExtensionsLargeBlobOutputs { Supported = authenticator.SupportsExtension("largeBlob") } : null,
            };

            var response = new AuthenticatorAttestationResponse(
                request.ClientDataJson,
                result.AttestationObject!,
                result.AuthenticatorData!,
                [authenticator.Transport],
                credential.Key.ExportSubjectPublicKeyInfo(),
                credential.Key.Algorithm);
            return new PublicKeyCredential([.. credential.Id], response, authenticator.Attachment, outputs);
        }

        throw DomException.NotAllowed("No authenticator could create the credential");
    }

    private sealed record MakeCredentialResult(string? Error, StoredCredential? Credential = null, byte[]? AuthenticatorData = null, byte[]? AttestationObject = null);

    /// <summary>The authenticatorMakeCredential operation of a virtual authenticator (https://w3c.github.io/webauthn/#sctn-op-make-cred).</summary>
    private static MakeCredentialResult MakeCredential(VirtualAuthenticator authenticator, CreateRequest request, bool requireResidentKey, bool requireUserPresence, bool userVerification, IReadOnlyList<string> attestationFormats)
    {
        var options = request.Options;

        // Step 2: the first algorithm that the authenticator supports
        int? algorithm = null;
        foreach (var candidate in request.Algorithms)
        {
            if (authenticator.SupportsAlgorithm(candidate))
            {
                algorithm = candidate;
                break;
            }
        }

        if (algorithm is null)
            return new("NotSupportedError");

        // Step 3: an excluded credential is on the authenticator; the error is returned once the user consents
        foreach (var descriptor in options.ExcludeCredentials)
        {
            if (descriptor.Type == "public-key" && authenticator.Lookup(descriptor.Id.Span, request.RpId) is not null)
                return new(authenticator.IsUserConsenting ? "InvalidStateError" : "NotAllowedError");
        }

        // Steps 4-5
        if ((requireResidentKey && !authenticator.SupportsResidentKeys) || (userVerification && !authenticator.SupportsUserVerification))
            return new("ConstraintError");

        // Step 6: the authorization gesture
        if (requireUserPresence && !authenticator.IsUserConsenting)
            return new("NotAllowedError");

        if (userVerification && !authenticator.IsUserVerified)
            return new("NotAllowedError");

        // Step 7: the new credential source
        var credential = new StoredCredential(RandomNumberGenerator.GetBytes(16), request.RpId, CredentialKey.Generate(algorithm.Value))
        {
            IsResident = requireResidentKey,
            UserHandle = options.User.Id.ToArray(),
            SignCount = 0,
            BackupEligibility = authenticator.DefaultBackupEligibility,
            BackupState = authenticator.DefaultBackupState,
            UserName = options.User.Name,
            UserDisplayName = options.User.DisplayName,
        };
        authenticator.Store(credential);

        // Steps 9-13: the authenticator data with the attested credential data, and the attestation object
        var flags = (byte)(AuthenticatorDataFlags.AttestedCredentialData
            | (requireUserPresence ? AuthenticatorDataFlags.UserPresent : 0)
            | (userVerification ? AuthenticatorDataFlags.UserVerified : 0)
            | (credential.BackupEligibility ? AuthenticatorDataFlags.BackupEligibility : 0)
            | (credential.BackupState ? AuthenticatorDataFlags.BackupState : 0));
        var authenticatorData = WebAuthenticationEncoding.EncodeAuthenticatorData(SHA256.HashData(Encoding.UTF8.GetBytes(request.RpId)), flags, signCount: 0, credential.Id, credential.Key.ExportCoseKey());

        // The "none" attestation when the relying party prefers it, else a packed self attestation
        // (https://w3c.github.io/webauthn/#sctn-packed-attestation): the AAGUID is zero and there is no certificate
        var noneFirst = attestationFormats.FirstOrDefault(format => format is "none" or "packed") == "none";
        var attestationObject = noneFirst
            ? WebAuthenticationEncoding.EncodeAttestationObject("none", algorithm: null, [], authenticatorData)
            : WebAuthenticationEncoding.EncodeAttestationObject("packed", algorithm, credential.Key.Sign([.. authenticatorData, .. request.ClientDataHash]), authenticatorData);
        return new(Error: null, credential, authenticatorData, attestationObject);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Authentication
    // ----------------------------------------------------------------------------------------------------------------

    private sealed record GetRequest(
        PublicKeyCredentialRequestOptions Options,
        string RpId,
        IReadOnlyList<PublicKeyCredentialDescriptor> AllowCredentials,
        IReadOnlyList<PublicKeyCredentialDescriptor> CredentialIdFilter,
        byte[] ClientDataJson,
        byte[] ClientDataHash,
        string Origin);

    private static GetRequest PrepareGet(Window window, CredentialRequestOptions options, PublicKeyCredentialRequestOptions publicKey, bool sameOriginWithAncestors)
    {
        // Steps 3-4: the conditional mediation filters the discovered credentials instead
        var conditional = options.Mediation == CredentialMediationRequirement.Conditional;
        var allowCredentials = conditional ? [] : publicKey.AllowCredentials;
        var credentialIdFilter = conditional ? publicKey.AllowCredentials : [];

        var origin = window.Document.Origin;
        if (origin.IsOpaque)
            throw DomException.NotAllowed("The origin of the caller is opaque");

        var rpId = GetRpId(origin, publicKey.RpId);

        // The authentication extensions supported by the client
        var extensions = publicKey.Extensions;
        if (extensions?.Prf is { EvalByCredential: { } evalByCredential })
        {
            if (evalByCredential.Count > 0 && allowCredentials.Count == 0)
                throw DomException.NotSupported("evalByCredential of the prf extension requires allowCredentials");

            foreach (var (key, _) in evalByCredential)
            {
                if (key.Length == 0 || WebAuthenticationEncoding.Base64UrlDecode(key) is not { } id || !allowCredentials.Any(descriptor => descriptor.Id.Span.SequenceEqual(id)))
                    throw DomException.Syntax($"'{key}' is not the base64url encoding of a credential ID of allowCredentials");
            }
        }

        if (extensions?.LargeBlob is { } largeBlob)
        {
            if (largeBlob.Support is not null)
                throw DomException.NotSupported("support of the largeBlob extension is only valid for a registration");

            if (largeBlob.Read is not null && largeBlob.Write is not null)
                throw DomException.NotSupported("read and write of the largeBlob extension cannot be used together");

            if (largeBlob.Write is not null && allowCredentials.Count != 1)
                throw DomException.NotSupported("write of the largeBlob extension requires exactly one credential in allowCredentials");
        }

        var clientDataJson = SerializeClientData(window, "webauthn.get", publicKey.Challenge.Span, sameOriginWithAncestors);
        if (options.Signal is { Aborted: true } signal)
            throw ToException(signal.Reason);

        return new GetRequest(publicKey, rpId, allowCredentials, credentialIdFilter, clientDataJson, SHA256.HashData(clientDataJson), origin.Serialize());
    }

    private static PublicKeyCredential RunGet(Window window, GetRequest request)
    {
        var options = request.Options;
        var userVerificationRequirement = UserVerificationRequirement(options.UserVerification);
        foreach (var authenticator in GetAuthenticators(window))
        {
            // https://w3c.github.io/webauthn/#sctn-issuing-cred-request-to-authenticator
            if (userVerificationRequirement == "required" && !authenticator.SupportsUserVerification)
                continue;

            var userVerification = userVerificationRequirement switch
            {
                "required" => true,
                "preferred" => authenticator.SupportsUserVerification,
                _ => false,
            };

            List<StoredCredential>? allowedCredentials = null;
            byte[]? savedCredentialId = null;
            if (request.AllowCredentials.Count > 0)
            {
                // The credentials of allowCredentials bound to the authenticator
                allowedCredentials = [];
                foreach (var descriptor in request.AllowCredentials)
                {
                    if (descriptor.Type == "public-key" && authenticator.Lookup(descriptor.Id.Span, request.RpId) is { } bound && !allowedCredentials.Contains(bound))
                    {
                        allowedCredentials.Add(bound);
                    }
                }

                if (allowedCredentials.Count == 0)
                    continue;

                if (allowedCredentials.Count == 1)
                {
                    savedCredentialId = allowedCredentials[0].Id;
                }
            }

            if (GetAssertion(authenticator, request, allowedCredentials, userVerification) is not { } assertion)
                continue;

            var credential = assertion.Credential;
            var credentialId = savedCredentialId ?? credential.Id;
            if (request.CredentialIdFilter.Count > 0 && !request.CredentialIdFilter.Any(descriptor => descriptor.Id.Span.SequenceEqual(credentialId)))
                continue;

            if (request.CredentialIdFilter.Count == 0 && request.AllowCredentials.Count == 0 && assertion.UserHandle is null)
                continue;

            var outputs = new AuthenticationExtensionsClientOutputs
            {
                Prf = options.Extensions?.Prf is { } prf ? GetPrfOutputs(authenticator, credential, prf) : null,
                LargeBlob = options.Extensions?.LargeBlob is { } largeBlob ? ProcessLargeBlob(authenticator, credential, largeBlob) : null,
            };

            window.Options.WebAuthentication?.Authenticators?.RecordAuthentication(request.Origin, window.EventLoop.TimeProvider.GetTimestamp());
            var response = new AuthenticatorAssertionResponse(request.ClientDataJson, assertion.AuthenticatorData, assertion.Signature, assertion.UserHandle);
            return new PublicKeyCredential([.. credentialId], response, authenticator.Attachment, outputs);
        }

        throw DomException.NotAllowed("No authenticator has an eligible credential");
    }

    private sealed record AssertionResult(StoredCredential Credential, byte[] AuthenticatorData, byte[] Signature, byte[]? UserHandle);

    /// <summary>The authenticatorGetAssertion operation of a virtual authenticator (https://w3c.github.io/webauthn/#sctn-op-get-assertion), or null on error.</summary>
    private static AssertionResult? GetAssertion(VirtualAuthenticator authenticator, GetRequest request, List<StoredCredential>? allowedCredentials, bool userVerification)
    {
        // Steps 3-6: the credential options (the allowed credentials, or the discoverable credentials of the RP ID)
        var credentialOptions = allowedCredentials ?? authenticator.GetDiscoverableCredentials(request.RpId);
        if (credentialOptions.Count == 0)
            return null;

        if (userVerification && !authenticator.SupportsUserVerification)
            return null;

        // Step 7: the authorization gesture; the user selects the first credential
        if (!authenticator.IsUserConsenting || (userVerification && !authenticator.IsUserVerified))
            return null;

        var credential = credentialOptions[0];
        var signCount = authenticator.IncrementSignCount(credential);
        var flags = (byte)(AuthenticatorDataFlags.UserPresent
            | (userVerification ? AuthenticatorDataFlags.UserVerified : 0)
            | (credential.BackupEligibility ? AuthenticatorDataFlags.BackupEligibility : 0)
            | (credential.BackupState ? AuthenticatorDataFlags.BackupState : 0));
        var authenticatorData = WebAuthenticationEncoding.EncodeAuthenticatorData(SHA256.HashData(Encoding.UTF8.GetBytes(request.RpId)), flags, signCount, [], []);
        var signature = credential.Key.Sign([.. authenticatorData, .. request.ClientDataHash]);

        // The user handle of a discoverable credential is always returned
        var userHandle = credential.IsResident || allowedCredentials is null ? credential.UserHandle : null;
        return new AssertionResult(credential, authenticatorData, signature, userHandle is null ? null : [.. userHandle]);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Extensions
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>https://w3c.github.io/webauthn/#prf-extension: SHA-256(UTF8Encode("WebAuthn PRF") || 0x00 || input).</summary>
    private static byte[] PrfSalt(ReadOnlySpan<byte> input) => SHA256.HashData([.. "WebAuthn PRF"u8, 0x00, .. input]);

    /// <summary>The PRF of a credential of a virtual authenticator: HMAC-SHA-256 keyed by a secret of the credential.</summary>
    private static byte[] EvaluatePrf(StoredCredential credential, byte[] salt) => HMACSHA256.HashData(credential.PrfSecret, salt);

    private static AuthenticationExtensionsPrfOutputs CreatePrfOutputs(VirtualAuthenticator authenticator, StoredCredential credential, byte[]? salt1, byte[]? salt2, bool? enabled)
    {
        AuthenticationExtensionsPrfValues? results = null;
        if (salt1 is not null && authenticator.SupportsExtension("prf"))
        {
            results = new AuthenticationExtensionsPrfValues
            {
                First = EvaluatePrf(credential, salt1),
                Second = salt2 is null ? null : (ReadOnlyMemory<byte>?)EvaluatePrf(credential, salt2),
            };
        }

        return new AuthenticationExtensionsPrfOutputs { Enabled = enabled, Results = results };
    }

    private static AuthenticationExtensionsPrfOutputs GetPrfOutputs(VirtualAuthenticator authenticator, StoredCredential credential, AuthenticationExtensionsPrfInputs prf)
    {
        var encodedId = WebAuthenticationEncoding.Base64UrlEncode(credential.Id);
        var values = prf.EvalByCredential?.FirstOrDefault(entry => entry.Key == encodedId).Value ?? prf.Eval;
        return values is null
            ? new AuthenticationExtensionsPrfOutputs()
            : CreatePrfOutputs(authenticator, credential, PrfSalt(values.First.Span), values.Second is { } second ? PrfSalt(second.Span) : null, enabled: null);
    }

    /// <summary>https://w3c.github.io/webauthn/#sctn-large-blob-extension (authentication): the blob is read from or written to the credential.</summary>
    private static AuthenticationExtensionsLargeBlobOutputs ProcessLargeBlob(VirtualAuthenticator authenticator, StoredCredential credential, AuthenticationExtensionsLargeBlobInputs largeBlob)
    {
        var supported = authenticator.SupportsExtension("largeBlob");
        if (largeBlob.Write is { } write)
        {
            if (supported)
            {
                credential.LargeBlob = write.ToArray();
            }

            return new AuthenticationExtensionsLargeBlobOutputs { Written = supported };
        }

        if (largeBlob.Read is true && supported && credential.LargeBlob is { } blob)
            return new AuthenticationExtensionsLargeBlobOutputs { Blob = blob.ToArray() };

        return new AuthenticationExtensionsLargeBlobOutputs();
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Static methods
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>https://w3c.github.io/webauthn/#sctn-isUserVerifyingPlatformAuthenticatorAvailable</summary>
    public static bool HasUserVerifyingPlatformAuthenticator(GlobalScope? global) => GetAuthenticators(global).Any(authenticator => authenticator.IsPlatform && authenticator.SupportsUserVerification);

    /// <summary>https://w3c.github.io/webauthn/#sctn-getClientCapabilities: the keys are sorted in lexicographical order.</summary>
    public static IReadOnlyList<KeyValuePair<string, bool>> GetClientCapabilities(GlobalScope? global)
    {
        var authenticators = GetAuthenticators(global);
        var capabilities = new List<KeyValuePair<string, bool>>
        {
            new("conditionalCreate", true),
            new("conditionalGet", true),
            new("extension:credProps", true),
            new("extension:largeBlob", true),
            new("extension:prf", true),
            new("hybridTransport", false),
            new("passkeyPlatformAuthenticator", authenticators.Any(authenticator => authenticator.IsPlatform && authenticator.SupportsUserVerification && authenticator.SupportsResidentKeys)),
            new("relatedOrigins", false),
            new("signalAllAcceptedCredentials", true),
            new("signalCurrentUserDetails", true),
            new("signalUnknownCredential", true),
            new("userVerifyingPlatformAuthenticator", HasUserVerifyingPlatformAuthenticator(global)),
        };
        capabilities.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
        return capabilities;
    }

    /// <summary>
    /// The signal methods (https://w3c.github.io/webauthn/#sctn-signal-methods): the RP ID is validated (the asynchronous
    /// RP ID validation algorithm), then the authenticator actions are performed on every available authenticator.
    /// </summary>
    public static Task Signal(GlobalScope? global, string rpId, Action<VirtualAuthenticator> action)
    {
        if (global is not Window window)
            return Task.FromException(new DomException("The signal methods are only available in a window", "SecurityError"));

        try
        {
            if (window.Document.Origin.IsOpaque)
                throw new DomException("The origin of the caller is opaque", "SecurityError");

            GetRpId(window.Document.Origin, rpId);
        }
        catch (DomException ex)
        {
            return Task.FromException(ex);
        }

        var result = new TaskCompletionSource();
        window.QueueGlobalTask(() =>
        {
            foreach (var authenticator in GetAuthenticators(window))
            {
                action(authenticator);
            }

            result.TrySetResult();
        });
        return result.Task;
    }
}
