using System.ComponentModel.DataAnnotations;

namespace Meziantou.Framework.DataAnnotations;

/// <summary>Validates that a string property contains an absolute URI.</summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.Tests/DataAnnotations/AbsoluteUriAttributeTests.cs" ref:region="AbsoluteUriAttribute_Usage" ref:dedent="true">
/// public sealed class MyModel
/// {
///     [AbsoluteUri]
///     public string? Url { get; set; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property)]
public sealed class AbsoluteUriAttribute : ValidationAttribute
{
    private readonly bool _hasCustomErrorMessage;

    public AbsoluteUriAttribute()
    {
    }

    public AbsoluteUriAttribute(string errorMessage)
        : base(errorMessage)
    {
        _hasCustomErrorMessage = true;
    }

    public override bool IsValid(object? value)
    {
        if (value is string str)
            return IsAbsoluteUri(str);

        return true;
    }

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (IsValid(value))
            return ValidationResult.Success;

        // Keep the historical message unless the user provides one
        var message = !_hasCustomErrorMessage && ErrorMessage is null && ErrorMessageResourceName is null
            ? $"Uri '{value}' is not an absolute URI"
            : FormatErrorMessage(validationContext.DisplayName);

        return new ValidationResult(message, validationContext.MemberName is { } memberName ? [memberName] : null);
    }

    private static bool IsAbsoluteUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return false;

        // Uri accepts "/path" (Unix), "C:\path" and "\\server\share" as implicit file URIs: require an explicit scheme
        if (uri.IsFile && !value.AsSpan().TrimStart().StartsWith(Uri.UriSchemeFile + ":", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }
}
