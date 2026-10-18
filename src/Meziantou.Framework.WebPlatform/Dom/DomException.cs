namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://webidl.spec.whatwg.org/#idl-DOMException</summary>
[IdlInterface("DOMException", "https://webidl.spec.whatwg.org/#idl-DOMException")]
public class DomException : Exception, Scripting.IStructuredSerializable, IRealmBoundPlatformObject
{
    /// <summary>
    /// The window of the realm the exception is created in when the specification gives it (e.g. "a new "AbortError" DOMException
    /// created in navigation's relevant realm"), or null: the exception is then created in the realm that converts it first.
    /// </summary>
    internal Window? RealmWindow { get; init; }

    Window? IRealmBoundPlatformObject.RealmWindow => RealmWindow;

    /// <summary>The serialization steps of DOMException (https://webidl.spec.whatwg.org/#idl-DOMException): the name and the message.</summary>
    object Scripting.IStructuredSerializable.CloneForStructuredSerialization() => new DomException(Message, Name);

    /// <summary>The legacy error code of <c>IndexSizeError</c>: the index is not in the allowed range.</summary>
    [IdlMember("INDEX_SIZE_ERR", "https://webidl.spec.whatwg.org/#dom-domexception-index_size_err")]
    public const ushort IndexSizeErr = 1;

    /// <summary>
    /// The legacy error code of <c>DOMStringSizeError</c>: the specified range of text does not fit in a string
    /// (legacy).
    /// </summary>
    [IdlMember("DOMSTRING_SIZE_ERR", "https://webidl.spec.whatwg.org/#dom-domexception-domstring_size_err")]
    public const ushort DomstringSizeErr = 2;

    /// <summary>
    /// The legacy error code of <c>HierarchyRequestError</c>: the operation would yield an incorrect node tree.
    /// </summary>
    [IdlMember("HIERARCHY_REQUEST_ERR", "https://webidl.spec.whatwg.org/#dom-domexception-hierarchy_request_err")]
    public const ushort HierarchyRequestErr = 3;

    /// <summary>The legacy error code of <c>WrongDocumentError</c>: the object is in the wrong document.</summary>
    [IdlMember("WRONG_DOCUMENT_ERR", "https://webidl.spec.whatwg.org/#dom-domexception-wrong_document_err")]
    public const ushort WrongDocumentErr = 4;

    /// <summary>
    /// The legacy error code of <c>InvalidCharacterError</c>: a string contains an invalid character.
    /// </summary>
    [IdlMember("INVALID_CHARACTER_ERR", "https://webidl.spec.whatwg.org/#dom-domexception-invalid_character_err")]
    public const ushort InvalidCharacterErr = 5;

    /// <summary>
    /// The legacy error code of <c>NoDataAllowedError</c>: data is specified for a node that does not support data
    /// (legacy).
    /// </summary>
    [IdlMember("NO_DATA_ALLOWED_ERR", "https://webidl.spec.whatwg.org/#dom-domexception-no_data_allowed_err")]
    public const ushort NoDataAllowedErr = 6;

    /// <summary>The legacy error code of <c>NoModificationAllowedError</c>: the object cannot be modified.</summary>
    [IdlMember("NO_MODIFICATION_ALLOWED_ERR", "https://webidl.spec.whatwg.org/#dom-domexception-no_modification_allowed_err")]
    public const ushort NoModificationAllowedErr = 7;

    /// <summary>The legacy error code of <c>NotFoundError</c>: the object cannot be found here.</summary>
    [IdlMember("NOT_FOUND_ERR", "https://webidl.spec.whatwg.org/#dom-domexception-not_found_err")]
    public const ushort NotFoundErr = 8;

    /// <summary>The legacy error code of <c>NotSupportedError</c>: the operation is not supported.</summary>
    [IdlMember("NOT_SUPPORTED_ERR", "https://webidl.spec.whatwg.org/#dom-domexception-not_supported_err")]
    public const ushort NotSupportedErr = 9;

    /// <summary>
    /// The legacy error code of <c>InUseAttributeError</c>: the attribute is already in use by another element.
    /// </summary>
    [IdlMember("INUSE_ATTRIBUTE_ERR", "https://webidl.spec.whatwg.org/#dom-domexception-inuse_attribute_err")]
    public const ushort InuseAttributeErr = 10;

    /// <summary>The legacy error code of <c>InvalidStateError</c>: the object is in an invalid state.</summary>
    [IdlMember("INVALID_STATE_ERR", "https://webidl.spec.whatwg.org/#dom-domexception-invalid_state_err")]
    public const ushort InvalidStateErr = 11;

    /// <summary>The legacy error code of <c>SyntaxError</c>: the string does not match the expected pattern.</summary>
    [IdlMember("SYNTAX_ERR", "https://webidl.spec.whatwg.org/#dom-domexception-syntax_err")]
    public const ushort SyntaxErr = 12;

    /// <summary>
    /// The legacy error code of <c>InvalidModificationError</c>: the object cannot be modified in this way.
    /// </summary>
    [IdlMember("INVALID_MODIFICATION_ERR", "https://webidl.spec.whatwg.org/#dom-domexception-invalid_modification_err")]
    public const ushort InvalidModificationErr = 13;

    /// <summary>
    /// The legacy error code of <c>NamespaceError</c>: the operation is not allowed by the namespaces rules.
    /// </summary>
    [IdlMember("NAMESPACE_ERR", "https://webidl.spec.whatwg.org/#dom-domexception-namespace_err")]
    public const ushort NamespaceErr = 14;

    /// <summary>
    /// The legacy error code of <c>InvalidAccessError</c>: the object does not support the operation or argument.
    /// </summary>
    [IdlMember("INVALID_ACCESS_ERR", "https://webidl.spec.whatwg.org/#dom-domexception-invalid_access_err")]
    public const ushort InvalidAccessErr = 15;

    /// <summary>The legacy error code of <c>ValidationError</c>: a validation error occurred (legacy).</summary>
    [IdlMember("VALIDATION_ERR", "https://webidl.spec.whatwg.org/#dom-domexception-validation_err")]
    public const ushort ValidationErr = 16;

    /// <summary>
    /// The legacy error code of <c>TypeMismatchError</c>: the type of the object does not match the expected type
    /// (legacy).
    /// </summary>
    [IdlMember("TYPE_MISMATCH_ERR", "https://webidl.spec.whatwg.org/#dom-domexception-type_mismatch_err")]
    public const ushort TypeMismatchErr = 17;

    /// <summary>The legacy error code of <c>SecurityError</c>: the operation is insecure.</summary>
    [IdlMember("SECURITY_ERR", "https://webidl.spec.whatwg.org/#dom-domexception-security_err")]
    public const ushort SecurityErr = 18;

    /// <summary>The legacy error code of <c>NetworkError</c>: a network error occurred.</summary>
    [IdlMember("NETWORK_ERR", "https://webidl.spec.whatwg.org/#dom-domexception-network_err")]
    public const ushort NetworkErr = 19;

    /// <summary>The legacy error code of <c>AbortError</c>: the operation was aborted.</summary>
    [IdlMember("ABORT_ERR", "https://webidl.spec.whatwg.org/#dom-domexception-abort_err")]
    public const ushort AbortErr = 20;

    /// <summary>The legacy error code of <c>URLMismatchError</c>: the given URL does not match another URL.</summary>
    [IdlMember("URL_MISMATCH_ERR", "https://webidl.spec.whatwg.org/#dom-domexception-url_mismatch_err")]
    public const ushort UrlMismatchErr = 21;

    /// <summary>The legacy error code of <c>QuotaExceededError</c>: the quota has been exceeded.</summary>
    [IdlMember("QUOTA_EXCEEDED_ERR", "https://webidl.spec.whatwg.org/#dom-domexception-quota_exceeded_err")]
    public const ushort QuotaExceededErr = 22;

    /// <summary>The legacy error code of <c>TimeoutError</c>: the operation timed out.</summary>
    [IdlMember("TIMEOUT_ERR", "https://webidl.spec.whatwg.org/#dom-domexception-timeout_err")]
    public const ushort TimeoutErr = 23;

    /// <summary>
    /// The legacy error code of <c>InvalidNodeTypeError</c>: the supplied node is incorrect or has an incorrect
    /// ancestor for this operation.
    /// </summary>
    [IdlMember("INVALID_NODE_TYPE_ERR", "https://webidl.spec.whatwg.org/#dom-domexception-invalid_node_type_err")]
    public const ushort InvalidNodeTypeErr = 24;

    /// <summary>The legacy error code of <c>DataCloneError</c>: the object cannot be cloned.</summary>
    [IdlMember("DATA_CLONE_ERR", "https://webidl.spec.whatwg.org/#dom-domexception-data_clone_err")]
    public const ushort DataCloneErr = 25;

    /// <summary>Creates an exception with an empty message and the <c>Error</c> name.</summary>
    [IdlMember("constructor", "https://webidl.spec.whatwg.org/#dom-domexception-domexception")]
    public DomException()
        : this("", "Error")
    {
    }

    /// <summary>Creates an exception with the given message and the <c>Error</c> name.</summary>
    [IdlMember("constructor", "https://webidl.spec.whatwg.org/#dom-domexception-domexception")]
    public DomException(string? message)
        : this(message ?? "", "Error")
    {
    }

    /// <summary>Creates an exception with the given message and name.</summary>
    [IdlMember("constructor", "https://webidl.spec.whatwg.org/#dom-domexception-domexception")]
    public DomException(string message, string name)
        : base(message)
    {
        Name = name;
    }

    /// <summary>
    /// Creates an exception with the given message, the <c>Error</c> name and the given inner exception.
    /// </summary>
    [NotInIdl]
    public DomException(string? message, Exception? innerException)
        : base(message, innerException)
    {
        Name = "Error";
    }

    /// <summary>Gets the message describing the error.</summary>
    [IdlMember("message", "https://webidl.spec.whatwg.org/#dom-domexception-message")]
    public override string Message => base.Message;

    /// <summary>Gets the name of the error, such as <c>NotFoundError</c>.</summary>
    [IdlMember("name", "https://webidl.spec.whatwg.org/#dom-domexception-name")]
    public string Name { get; private protected init; }

    /// <summary>
    /// Gets the legacy numeric error code associated with the name, or 0 when the name has no legacy code.
    /// </summary>
    [IdlMember("code", "https://webidl.spec.whatwg.org/#dom-domexception-code")]
    public ushort Code => Name switch
    {
        "IndexSizeError" => IndexSizeErr,
        "HierarchyRequestError" => HierarchyRequestErr,
        "WrongDocumentError" => WrongDocumentErr,
        "InvalidCharacterError" => InvalidCharacterErr,
        "NoModificationAllowedError" => NoModificationAllowedErr,
        "NotFoundError" => NotFoundErr,
        "NotSupportedError" => NotSupportedErr,
        "InUseAttributeError" => InuseAttributeErr,
        "InvalidStateError" => InvalidStateErr,
        "SyntaxError" => SyntaxErr,
        "InvalidModificationError" => InvalidModificationErr,
        "NamespaceError" => NamespaceErr,
        "InvalidAccessError" => InvalidAccessErr,
        "TypeMismatchError" => TypeMismatchErr,
        "SecurityError" => SecurityErr,
        "NetworkError" => NetworkErr,
        "AbortError" => AbortErr,
        "URLMismatchError" => UrlMismatchErr,
        "QuotaExceededError" => QuotaExceededErr,
        "TimeoutError" => TimeoutErr,
        "InvalidNodeTypeError" => InvalidNodeTypeErr,
        "DataCloneError" => DataCloneErr,
        _ => 0,
    };

    internal static DomException HierarchyRequest(string message) => new(message, "HierarchyRequestError");

    internal static DomException NotFound(string message) => new(message, "NotFoundError");

    internal static DomException InvalidCharacter(string message) => new(message, "InvalidCharacterError");

    internal static DomException Namespace(string message) => new(message, "NamespaceError");

    internal static DomException NotSupported(string message) => new(message, "NotSupportedError");

    internal static DomException NoModificationAllowed(string message) => new(message, "NoModificationAllowedError");

    internal static DomException InvalidState(string message) => new(message, "InvalidStateError");

    internal static DomException Syntax(string message) => new(message, "SyntaxError");

    internal static DomException InUseAttribute(string message) => new(message, "InUseAttributeError");

    internal static DomException IndexSize(string message) => new(message, "IndexSizeError");

    internal static DomException NotAllowed(string message) => new(message, "NotAllowedError");
}
