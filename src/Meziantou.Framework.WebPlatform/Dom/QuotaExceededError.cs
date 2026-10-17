namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://webidl.spec.whatwg.org/#dictdef-quotaexceedederroroptions</summary>
[IdlInterface("QuotaExceededErrorOptions", "https://webidl.spec.whatwg.org/#dictdef-quotaexceedederroroptions")]
public sealed class QuotaExceededErrorOptions
{
    /// <summary>Gets the quota that was exceeded, when known.</summary>
    [IdlMember("quota", "https://webidl.spec.whatwg.org/#dom-quotaexceedederroroptions-quota")]
    public double? Quota { get; init; }

    /// <summary>Gets the amount that was requested, when known.</summary>
    [IdlMember("requested", "https://webidl.spec.whatwg.org/#dom-quotaexceedederroroptions-requested")]
    public double? Requested { get; init; }
}

/// <summary>
/// The exception thrown when a quota is exceeded (https://webidl.spec.whatwg.org/#quotaexceedederror): a
/// <see cref="DomException"/> named <c>QuotaExceededError</c>, with the quota and the requested amount when they are known.
/// </summary>
[IdlInterface("QuotaExceededError", "https://webidl.spec.whatwg.org/#quotaexceedederror")]
public sealed class QuotaExceededError : DomException, Scripting.IStructuredSerializable
{
    /// <summary>https://webidl.spec.whatwg.org/#dom-quotaexceedederror-quotaexceedederror</summary>
    [IdlMember("constructor", "https://webidl.spec.whatwg.org/#dom-quotaexceedederror-quotaexceedederror")]
    public QuotaExceededError()
        : this("", quota: null, requested: null)
    {
    }

    /// <summary>https://webidl.spec.whatwg.org/#dom-quotaexceedederror-quotaexceedederror</summary>
    [IdlMember("constructor", "https://webidl.spec.whatwg.org/#dom-quotaexceedederror-quotaexceedederror")]
    public QuotaExceededError(string? message)
        : this(message ?? "", quota: null, requested: null)
    {
    }

    /// <summary>https://webidl.spec.whatwg.org/#dom-quotaexceedederror-quotaexceedederror</summary>
    [IdlMember("constructor", "https://webidl.spec.whatwg.org/#dom-quotaexceedederror-quotaexceedederror")]
    public QuotaExceededError(string message, QuotaExceededErrorOptions? options)
        : this(message, options?.Quota, options?.Requested)
    {
        if (Quota < 0)
            throw new RangeErrorException("The quota must not be negative");

        if (Requested < 0)
            throw new RangeErrorException("The requested amount must not be negative");

        if (Quota is { } quota && Requested is { } requested && requested < quota)
            throw new RangeErrorException("The requested amount must not be less than the quota");
    }

    /// <summary>Creates an exception with the given quota and requested amount (null when unknown).</summary>
    [NotInIdl]
    public QuotaExceededError(string message, double? quota, double? requested)
        : base(message ?? "", "QuotaExceededError")
    {
        Quota = quota;
        Requested = requested;
    }

    /// <summary>Creates a <c>QuotaExceededError</c> with a message and an inner exception.</summary>
    [NotInIdl]
    public QuotaExceededError(string? message, Exception? innerException)
        : base(message, innerException)
    {
        Name = "QuotaExceededError";
    }

    /// <summary>The serialization steps (https://webidl.spec.whatwg.org/#quotaexceedederror): the name, the message, the quota and the requested amount.</summary>
    object Scripting.IStructuredSerializable.CloneForStructuredSerialization() => new QuotaExceededError(Message, Quota, Requested);

    /// <summary>Gets the quota, or null when it is unknown.</summary>
    [IdlMember("quota", "https://webidl.spec.whatwg.org/#dom-quotaexceedederror-quota")]
    public double? Quota { get; }

    /// <summary>Gets the requested amount, or null when it is unknown.</summary>
    [IdlMember("requested", "https://webidl.spec.whatwg.org/#dom-quotaexceedederror-requested")]
    public double? Requested { get; }
}
