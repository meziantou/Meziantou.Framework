using System.ComponentModel;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>Whether a feature of a key system is required, optional or not allowed (https://w3c.github.io/encrypted-media/#dom-mediakeysrequirement).</summary>
/// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeysrequirement"/>
[IdlInterface("MediaKeysRequirement", "https://w3c.github.io/encrypted-media/#dom-mediakeysrequirement")]
public enum MediaKeysRequirement
{
    /// <summary>The feature must be supported, and the objects created from the configuration may use it.</summary>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeysrequirement-required"/>
    [IdlMember("required", "https://w3c.github.io/encrypted-media/#dom-mediakeysrequirement-required")]
    Required,

    /// <summary>The feature may be supported and used (only in a configuration passed to <c>requestMediaKeySystemAccess()</c>).</summary>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeysrequirement-optional"/>
    [IdlMember("optional", "https://w3c.github.io/encrypted-media/#dom-mediakeysrequirement-optional")]
    Optional,

    /// <summary>The feature must not be used.</summary>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeysrequirement-not-allowed"/>
    [IdlMember("not-allowed", "https://w3c.github.io/encrypted-media/#dom-mediakeysrequirement-not-allowed")]
    NotAllowed,
}

/// <summary>The type of a key session (https://w3c.github.io/encrypted-media/#dom-mediakeysessiontype).</summary>
/// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeysessiontype"/>
[IdlInterface("MediaKeySessionType", "https://w3c.github.io/encrypted-media/#dom-mediakeysessiontype")]
public enum MediaKeySessionType
{
    /// <summary>A session whose license and keys are not persisted.</summary>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeysessiontype-temporary"/>
    [IdlMember("temporary", "https://w3c.github.io/encrypted-media/#dom-mediakeysessiontype-temporary")]
    Temporary,

    /// <summary>A session whose license is persisted, and can be loaded again by its session ID (<see cref="MediaKeySession.Load"/>).</summary>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeysessiontype-persistent-license"/>
    [IdlMember("persistent-license", "https://w3c.github.io/encrypted-media/#dom-mediakeysessiontype-persistent-license")]
    PersistentLicense,
}

/// <summary>Why a key session was closed (https://w3c.github.io/encrypted-media/#dom-mediakeysessionclosedreason).</summary>
/// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeysessionclosedreason"/>
[IdlInterface("MediaKeySessionClosedReason", "https://w3c.github.io/encrypted-media/#dom-mediakeysessionclosedreason")]
public enum MediaKeySessionClosedReason
{
    /// <summary>An unrecoverable error of the CDM.</summary>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeysessionclosedreason-internal-error"/>
    [IdlMember("internal-error", "https://w3c.github.io/encrypted-media/#dom-mediakeysessionclosedreason-internal-error")]
    InternalError,

    /// <summary>The application called <see cref="MediaKeySession.Close"/>.</summary>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeysessionclosedreason-closed-by-application"/>
    [IdlMember("closed-by-application", "https://w3c.github.io/encrypted-media/#dom-mediakeysessionclosedreason-closed-by-application")]
    ClosedByApplication,

    /// <summary>The CDM received the acknowledgement of the record of license destruction.</summary>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeysessionclosedreason-release-acknowledged"/>
    [IdlMember("release-acknowledged", "https://w3c.github.io/encrypted-media/#dom-mediakeysessionclosedreason-release-acknowledged")]
    ReleaseAcknowledged,

    /// <summary>The hardware context of the CDM was reset.</summary>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeysessionclosedreason-hardware-context-reset"/>
    [IdlMember("hardware-context-reset", "https://w3c.github.io/encrypted-media/#dom-mediakeysessionclosedreason-hardware-context-reset")]
    HardwareContextReset,

    /// <summary>The session was closed to reclaim resources.</summary>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeysessionclosedreason-resource-evicted"/>
    [IdlMember("resource-evicted", "https://w3c.github.io/encrypted-media/#dom-mediakeysessionclosedreason-resource-evicted")]
    ResourceEvicted,
}

/// <summary>The status of a key (https://w3c.github.io/encrypted-media/#dom-mediakeystatus).</summary>
/// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeystatus"/>
[IdlInterface("MediaKeyStatus", "https://w3c.github.io/encrypted-media/#dom-mediakeystatus")]
public enum MediaKeyStatus
{
    /// <summary>The key is usable for decryption.</summary>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeystatus-usable"/>
    [IdlMember("usable", "https://w3c.github.io/encrypted-media/#dom-mediakeystatus-usable")]
    Usable,

    /// <summary>The expiration time of the key has passed.</summary>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeystatus-expired"/>
    [IdlMember("expired", "https://w3c.github.io/encrypted-media/#dom-mediakeystatus-expired")]
    Expired,

    /// <summary>The key is no longer available, but a record of license destruction is.</summary>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeystatus-released"/>
    [IdlMember("released", "https://w3c.github.io/encrypted-media/#dom-mediakeystatus-released")]
    Released,

    /// <summary>The output restrictions of the key cannot be met.</summary>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeystatus-output-restricted"/>
    [IdlMember("output-restricted", "https://w3c.github.io/encrypted-media/#dom-mediakeystatus-output-restricted")]
    OutputRestricted,

    /// <summary>The media data decrypted with the key may be presented at a lower quality.</summary>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeystatus-output-downscaled"/>
    [IdlMember("output-downscaled", "https://w3c.github.io/encrypted-media/#dom-mediakeystatus-output-downscaled")]
    OutputDownscaled,

    /// <summary>The key will become usable when its start time is reached.</summary>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeystatus-usable-in-future"/>
    [IdlMember("usable-in-future", "https://w3c.github.io/encrypted-media/#dom-mediakeystatus-usable-in-future")]
    UsableInFuture,

    /// <summary>The status of the key is being determined.</summary>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeystatus-status-pending"/>
    [IdlMember("status-pending", "https://w3c.github.io/encrypted-media/#dom-mediakeystatus-status-pending")]
    StatusPending,

    /// <summary>The key is not usable because of an error of the CDM.</summary>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeystatus-internal-error"/>
    [IdlMember("internal-error", "https://w3c.github.io/encrypted-media/#dom-mediakeystatus-internal-error")]
    InternalError,
}

/// <summary>The type of the message of a key session (https://w3c.github.io/encrypted-media/#dom-mediakeymessagetype).</summary>
/// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeymessagetype"/>
[IdlInterface("MediaKeyMessageType", "https://w3c.github.io/encrypted-media/#dom-mediakeymessagetype")]
public enum MediaKeyMessageType
{
    /// <summary>A request for a new license.</summary>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeymessagetype-license-request"/>
    [IdlMember("license-request", "https://w3c.github.io/encrypted-media/#dom-mediakeymessagetype-license-request")]
    LicenseRequest,

    /// <summary>A request to renew a license.</summary>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeymessagetype-license-renewal"/>
    [IdlMember("license-renewal", "https://w3c.github.io/encrypted-media/#dom-mediakeymessagetype-license-renewal")]
    LicenseRenewal,

    /// <summary>A record of license destruction.</summary>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeymessagetype-license-release"/>
    [IdlMember("license-release", "https://w3c.github.io/encrypted-media/#dom-mediakeymessagetype-license-release")]
    LicenseRelease,

    /// <summary>A request for App-Assisted Individualization.</summary>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeymessagetype-individualization-request"/>
    [IdlMember("individualization-request", "https://w3c.github.io/encrypted-media/#dom-mediakeymessagetype-individualization-request")]
    IndividualizationRequest,
}

/// <summary>A media type and its capabilities in a <see cref="MediaKeySystemConfiguration"/> (https://w3c.github.io/encrypted-media/#mediakeysystemmediacapability-dictionary).</summary>
/// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeysystemmediacapability"/>
[IdlInterface("MediaKeySystemMediaCapability", "https://w3c.github.io/encrypted-media/#dom-mediakeysystemmediacapability")]
public sealed class MediaKeySystemMediaCapability
{
    /// <summary>Gets the MIME type of the media resource, with its codecs (e.g. <c>video/mp4;codecs="avc1.4d401e"</c>).</summary>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeysystemmediacapability-contenttype"/>
    [IdlMember("contentType", "https://w3c.github.io/encrypted-media/#dom-mediakeysystemmediacapability-contenttype")]
    public string ContentType { get; init; } = "";

    /// <summary>Gets the encryption scheme of the content type (<c>cenc</c>, <c>cbcs</c>, <c>cbcs-1-9</c>), or null for any scheme.</summary>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeysystemmediacapability-encryptionscheme"/>
    [IdlMember("encryptionScheme", "https://w3c.github.io/encrypted-media/#dom-mediakeysystemmediacapability-encryptionscheme")]
    public string? EncryptionScheme { get; init; }

    /// <summary>Gets the robustness level of the content type, or the empty string for any level.</summary>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeysystemmediacapability-robustness"/>
    [IdlMember("robustness", "https://w3c.github.io/encrypted-media/#dom-mediakeysystemmediacapability-robustness")]
    public string Robustness { get; init; } = "";
}

/// <summary>A configuration of a key system (https://w3c.github.io/encrypted-media/#mediakeysystemconfiguration-dictionary).</summary>
/// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeysystemconfiguration"/>
[IdlInterface("MediaKeySystemConfiguration", "https://w3c.github.io/encrypted-media/#dom-mediakeysystemconfiguration")]
public sealed class MediaKeySystemConfiguration
{
    /// <summary>Gets the label of the configuration, kept in the configuration of the <see cref="MediaKeySystemAccess"/>.</summary>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeysystemconfiguration-label"/>
    [IdlMember("label", "https://w3c.github.io/encrypted-media/#dom-mediakeysystemconfiguration-label")]
    public string Label { get; init; } = "";

    /// <summary>Gets the Initialization Data Types (<c>cenc</c>, <c>keyids</c>, <c>webm</c>), or an empty list for any type.</summary>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeysystemconfiguration-initdatatypes"/>
    [IdlMember("initDataTypes", "https://w3c.github.io/encrypted-media/#dom-mediakeysystemconfiguration-initdatatypes")]
    public IReadOnlyList<string> InitDataTypes { get; init; } = [];

    /// <summary>Gets the audio types and their capabilities.</summary>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeysystemconfiguration-audiocapabilities"/>
    [IdlMember("audioCapabilities", "https://w3c.github.io/encrypted-media/#dom-mediakeysystemconfiguration-audiocapabilities")]
    public IReadOnlyList<MediaKeySystemMediaCapability> AudioCapabilities { get; init; } = [];

    /// <summary>Gets the video types and their capabilities.</summary>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeysystemconfiguration-videocapabilities"/>
    [IdlMember("videoCapabilities", "https://w3c.github.io/encrypted-media/#dom-mediakeysystemconfiguration-videocapabilities")]
    public IReadOnlyList<MediaKeySystemMediaCapability> VideoCapabilities { get; init; } = [];

    /// <summary>Gets whether a Distinctive Identifier is required.</summary>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeysystemconfiguration-distinctiveidentifier"/>
    [IdlMember("distinctiveIdentifier", "https://w3c.github.io/encrypted-media/#dom-mediakeysystemconfiguration-distinctiveidentifier")]
    public MediaKeysRequirement DistinctiveIdentifier { get; init; } = MediaKeysRequirement.Optional;

    /// <summary>Gets whether the ability to persist state is required (<c>required</c> to create <c>persistent-license</c> sessions).</summary>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeysystemconfiguration-persistentstate"/>
    [IdlMember("persistentState", "https://w3c.github.io/encrypted-media/#dom-mediakeysystemconfiguration-persistentstate")]
    public MediaKeysRequirement PersistentState { get; init; } = MediaKeysRequirement.Optional;

    /// <summary>Gets the session types that must be supported, or null when the member is not present (the configuration then requires <c>temporary</c>).</summary>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeysystemconfiguration-sessiontypes"/>
    [IdlMember("sessionTypes", "https://w3c.github.io/encrypted-media/#dom-mediakeysystemconfiguration-sessiontypes")]
    public IReadOnlyList<string>? SessionTypes { get; init; }
}

/// <summary>The policy requirements given to <see cref="MediaKeys.GetStatusForPolicy"/> (https://w3c.github.io/encrypted-media/#dom-mediakeyspolicy).</summary>
/// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeyspolicy"/>
[IdlInterface("MediaKeysPolicy", "https://w3c.github.io/encrypted-media/#dom-mediakeyspolicy")]
public sealed class MediaKeysPolicy
{
    /// <summary>Gets the minimum HDCP version, or null when the member is not present.</summary>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeyspolicy-minhdcpversion"/>
    [IdlMember("minHdcpVersion", "https://w3c.github.io/encrypted-media/#dom-mediakeyspolicy-minhdcpversion")]
    public string? MinHdcpVersion { get; init; }
}

/// <summary>The initialization dictionary of a <see cref="MediaKeyMessageEvent"/> (https://w3c.github.io/encrypted-media/#dom-mediakeymessageeventinit).</summary>
/// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeymessageeventinit"/>
[IdlInterface("MediaKeyMessageEventInit", "https://w3c.github.io/encrypted-media/#dom-mediakeymessageeventinit")]
public sealed class MediaKeyMessageEventInit : EventInit
{
    /// <summary>Gets the type of the message.</summary>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeymessageeventinit-messagetype"/>
    [IdlMember("messageType", "https://w3c.github.io/encrypted-media/#dom-mediakeymessageeventinit-messagetype")]
    public required MediaKeyMessageType MessageType { get; init; }

    /// <summary>Gets the message.</summary>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeymessageeventinit-message"/>
    [IdlMember("message", "https://w3c.github.io/encrypted-media/#dom-mediakeymessageeventinit-message")]
    public required ReadOnlyMemory<byte> Message { get; init; }
}

/// <summary>The event of a message of a key session to the license server (<c>message</c>, https://w3c.github.io/encrypted-media/#mediakeymessageevent).</summary>
/// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeymessageevent"/>
[IdlInterface("MediaKeyMessageEvent", "https://w3c.github.io/encrypted-media/#dom-mediakeymessageevent")]
public sealed class MediaKeyMessageEvent : Event
{
    private readonly byte[] _message;

    /// <summary>Creates an event.</summary>
    /// <param name="type">The type of the event.</param>
    /// <param name="eventInitDict">The initialization dictionary, with the message.</param>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeymessageevent-constructor"/>
    [IdlMember("constructor", "https://w3c.github.io/encrypted-media/#dom-mediakeymessageevent-constructor")]
    public MediaKeyMessageEvent(string type, MediaKeyMessageEventInit eventInitDict)
        : base(type, eventInitDict)
    {
        ArgumentNullException.ThrowIfNull(eventInitDict);
        MessageType = eventInitDict.MessageType;
        _message = eventInitDict.Message.ToArray();
    }

    /// <summary>Gets the type of the message.</summary>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeymessageevent-messagetype"/>
    [IdlMember("messageType", "https://w3c.github.io/encrypted-media/#dom-mediakeymessageevent-messagetype")]
    public MediaKeyMessageType MessageType { get; }

    /// <summary>Gets the message (for Clear Key, a JSON object encoded in UTF-8).</summary>
    [NotInIdl("The bytes for the .NET code: the message attribute is MessageBuffer")]
    public ReadOnlyMemory<byte> Message => _message;

    /// <summary>Gets the <c>ArrayBuffer</c> of the message for the scripts: use <see cref="Message"/> in .NET code.</summary>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediakeymessageevent-message"/>
    [IdlMember("message", "https://w3c.github.io/encrypted-media/#dom-mediakeymessageevent-message")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public object MessageBuffer => _message;
}

/// <summary>The initialization dictionary of a <see cref="MediaEncryptedEvent"/> (https://w3c.github.io/encrypted-media/#dom-mediaencryptedeventinit).</summary>
/// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediaencryptedeventinit"/>
[IdlInterface("MediaEncryptedEventInit", "https://w3c.github.io/encrypted-media/#dom-mediaencryptedeventinit")]
public sealed class MediaEncryptedEventInit : EventInit
{
    /// <summary>Gets the Initialization Data Type of <see cref="InitData"/>.</summary>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediaencryptedeventinit-initdatatype"/>
    [IdlMember("initDataType", "https://w3c.github.io/encrypted-media/#dom-mediaencryptedeventinit-initdatatype")]
    public string InitDataType { get; init; } = "";

    /// <summary>Gets the Initialization Data, or null.</summary>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediaencryptedeventinit-initdata"/>
    [IdlMember("initData", "https://w3c.github.io/encrypted-media/#dom-mediaencryptedeventinit-initdata")]
    public ReadOnlyMemory<byte>? InitData { get; init; }
}

/// <summary>
/// The event fired at a media element when its media data contains Initialization Data (<c>encrypted</c>,
/// https://w3c.github.io/encrypted-media/#mediaencryptedevent): the application creates a key session with it.
/// </summary>
/// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediaencryptedevent"/>
[IdlInterface("MediaEncryptedEvent", "https://w3c.github.io/encrypted-media/#dom-mediaencryptedevent")]
public sealed class MediaEncryptedEvent : Event
{
    private readonly byte[]? _initData;

    /// <summary>Creates an event.</summary>
    /// <param name="type">The type of the event.</param>
    /// <param name="eventInitDict">The initialization dictionary.</param>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediaencryptedevent-constructor"/>
    [IdlMember("constructor", "https://w3c.github.io/encrypted-media/#dom-mediaencryptedevent-constructor")]
    public MediaEncryptedEvent(string type, MediaEncryptedEventInit? eventInitDict = null)
        : base(type, eventInitDict)
    {
        InitDataType = eventInitDict?.InitDataType ?? "";
        _initData = eventInitDict?.InitData?.ToArray();
    }

    /// <summary>Gets the Initialization Data Type of <see cref="InitData"/> (<c>cenc</c>, <c>keyids</c>, <c>webm</c>), or the empty string.</summary>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediaencryptedevent-initdatatype"/>
    [IdlMember("initDataType", "https://w3c.github.io/encrypted-media/#dom-mediaencryptedevent-initdatatype")]
    public string InitDataType { get; }

    /// <summary>Gets the Initialization Data, or null (e.g. when the media data is not CORS-same-origin).</summary>
    [NotInIdl("The bytes for the .NET code: the initData attribute is InitDataBuffer")]
    public ReadOnlyMemory<byte>? InitData => _initData is { } initData ? initData : null;

    /// <summary>Gets the <c>ArrayBuffer</c> of the Initialization Data for the scripts, or null: use <see cref="InitData"/> in .NET code.</summary>
    /// <seealso href="https://w3c.github.io/encrypted-media/#dom-mediaencryptedevent-initdata"/>
    [IdlMember("initData", "https://w3c.github.io/encrypted-media/#dom-mediaencryptedevent-initdata")]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public object? InitDataBuffer => _initData;
}
