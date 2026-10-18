using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The Trusted Types directives of a content security policy
/// (https://w3c.github.io/trusted-types/dist/spec/#integration-with-content-security-policy): <c>require-trusted-types-for</c>
/// and <c>trusted-types</c>. Trusted Types are enforced for the documents whose <see cref="Document.TrustedTypesDirectives"/>
/// contain directives (there are none by default).
/// </summary>
/// <remarks>
/// The violations are reported as violations of the content security policy of the directives: the
/// <c>securitypolicyviolation</c> event and the <c>report-uri</c> reports of the policy delivered with the document (a
/// <c>Content-Security-Policy</c> header or a <c>meta</c> element).
/// </remarks>
public sealed class TrustedTypesDirectives
{
    private ContentSecurityPolicy? _policy;

    /// <summary>
    /// Whether the policy contains the <c>require-trusted-types-for 'script'</c> directive
    /// (https://w3c.github.io/trusted-types/dist/spec/#require-trusted-types-for-csp-directive): the DOM XSS injection
    /// sinks only accept trusted types (or the values of the default policy).
    /// </summary>
    public bool RequireTrustedTypesForScript { get; init; }

    /// <summary>
    /// The value of the <c>trusted-types</c> directive (https://w3c.github.io/trusted-types/dist/spec/#trusted-types-csp-directive):
    /// the policy names that can be created, <c>*</c> (any name), <c>'allow-duplicates'</c> and <c>'none'</c>. <see langword="null"/>
    /// when the policy has no <c>trusted-types</c> directive (any policy can be created).
    /// </summary>
    public IReadOnlyList<string>? TrustedTypes { get; init; }

    /// <summary>
    /// Whether the disposition of the policy is "report" (<c>Content-Security-Policy-Report-Only</c>): the directives do not
    /// block anything, but the default policy is still applied to the values of the sinks.
    /// </summary>
    public bool ReportOnly { get; init; }

    /// <summary>
    /// Returns the Trusted Types directives of a serialized content security policy (e.g. the value of a
    /// <c>Content-Security-Policy</c> header, https://w3c.github.io/webappsec-csp/#parse-serialized-policy). The other
    /// directives are ignored.
    /// </summary>
    /// <param name="serializedPolicy">The serialized policy, e.g. <c>require-trusted-types-for 'script'; trusted-types one two</c>.</param>
    /// <param name="reportOnly">Whether the disposition of the policy is "report" (<c>Content-Security-Policy-Report-Only</c>).</param>
    public static TrustedTypesDirectives Parse(string serializedPolicy, bool reportOnly = false)
    {
        ArgumentNullException.ThrowIfNull(serializedPolicy);
        var requireTrustedTypesForScript = false;
        List<string>? trustedTypes = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var token in serializedPolicy.Split(';'))
        {
            var strippedToken = InfraAscii.StripLeadingAndTrailingAsciiWhitespace(token).ToString();
            if (strippedToken.Length == 0)
                continue;

            var values = SplitOnAsciiWhitespace(strippedToken);
            var directiveName = InfraAscii.ToAsciiLowercase(values[0]);

            // The first directive with a given name is used, the duplicates are ignored
            if (!seen.Add(directiveName))
                continue;

            if (directiveName == "require-trusted-types-for")
            {
                // https://w3c.github.io/trusted-types/dist/spec/#does-sink-type-require-trusted-types: the value must contain the 'script' sink group
                requireTrustedTypesForScript = values.Skip(1).Any(v => InfraAscii.EqualsIgnoreAsciiCase(v, "'script'"));
            }
            else if (directiveName == "trusted-types")
            {
                trustedTypes = [.. values.Skip(1)];
            }
        }

        return new TrustedTypesDirectives
        {
            RequireTrustedTypesForScript = requireTrustedTypesForScript,
            TrustedTypes = trustedTypes,
            ReportOnly = reportOnly,
        };
    }

    /// <summary>
    /// The content security policy that contains the directives, whose violations are reported: the policy of the CSP list of
    /// the document, or a policy that only contains the directives when they were created by <see cref="Parse"/>.
    /// </summary>
    internal ContentSecurityPolicy Policy
    {
        get
        {
            if (_policy is null)
            {
                var serialized = RequireTrustedTypesForScript ? "require-trusted-types-for 'script'" : "";
                if (TrustedTypes is { } trustedTypes)
                {
                    serialized += (serialized.Length > 0 ? "; " : "") + "trusted-types" + string.Concat(trustedTypes.Select(value => " " + value));
                }

                _policy = ContentSecurityPolicy.Parse(serialized, ReportOnly, fromMeta: false);
            }

            return _policy;
        }

        set => _policy = value;
    }

    private static List<string> SplitOnAsciiWhitespace(string value)
    {
        var result = new List<string>();
        var start = -1;
        for (var i = 0; i <= value.Length; i++)
        {
            if (i == value.Length || InfraAscii.IsAsciiWhitespace(value[i]))
            {
                if (start >= 0)
                {
                    result.Add(value[start..i]);
                    start = -1;
                }
            }
            else if (start < 0)
            {
                start = i;
            }
        }

        return result;
    }

    /// <summary>https://w3c.github.io/trusted-types/dist/spec/#should-block-create-policy: whether the directives create a violation for the creation of a policy.</summary>
    internal bool IsPolicyCreationViolation(string policyName, IReadOnlyCollection<string> createdPolicyNames)
    {
        if (TrustedTypes is not { } expressions)
            return false;

        // 'none' is ignored when other keywords or policy names are present
        if (expressions.Count > 0 && expressions.All(e => InfraAscii.EqualsIgnoreAsciiCase(e, "'none'")))
            return true;

        var allowDuplicates = expressions.Any(e => InfraAscii.EqualsIgnoreAsciiCase(e, "'allow-duplicates'"));
        if (createdPolicyNames.Contains(policyName) && !allowDuplicates)
            return true;

        // An empty directive allows no policy
        return !expressions.Contains("*", StringComparer.Ordinal) && !(IsValidPolicyName(policyName) && expressions.Contains(policyName, StringComparer.Ordinal));
    }

    /// <summary>https://w3c.github.io/trusted-types/dist/spec/#tt-policy-name: 1*( ALPHA / DIGIT / "-" / "#" / "=" / "_" / "/" / "@" / "." / "%")</summary>
    private static bool IsValidPolicyName(string value)
    {
        if (value.Length == 0)
            return false;

        foreach (var c in value)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('-' or '#' or '=' or '_' or '/' or '@' or '.' or '%'))
                return false;
        }

        return true;
    }
}
