using Meziantou.Framework;
using Meziantou.Framework.Fuzzing;

namespace UriFuzz;

public static partial class UriFuzzTargets
{
    /// <summary>
    /// Decomposes a domain with the Public Suffix List. It never throws, and the string, span, and single-value overloads agree.
    /// The public suffix and the registrable domain are suffixes of the normalized domain on a label boundary, the registrable
    /// domain is one label followed by the public suffix, and the subdomain followed by the registrable domain is the domain.
    /// The decomposition of the normalized domain, with or without a trailing dot or in upper case, is the same.
    /// </summary>
    private static void PublicSuffixTarget(ReadOnlySpan<byte> data)
    {
        var sources = (PublicSuffixRuleSources)(FuzzInput.ReadByte(ref data) & (byte)PublicSuffixRuleSources.All);
        var domain = FuzzText.Decode(data);

        var isValid = PublicSuffixList.TryGetDomainInfo(domain, out var info, sources);
        FuzzAssert.Equal(isValid, PublicSuffixList.TryGetDomainInfo(domain.AsSpan(), out var spanInfo, sources), "The string and span overloads disagree on the validity");
        FuzzAssert.Equal(info, spanInfo, "The string and span overloads give different decompositions");
        FuzzAssert.Equal(isValid ? info.PublicSuffix : null, PublicSuffixList.GetPublicSuffix(domain, sources), "GetPublicSuffix disagrees with TryGetDomainInfo");
        FuzzAssert.Equal(isValid ? info.RegistrableDomain : null, PublicSuffixList.GetRegistrableDomain(domain, sources), "GetRegistrableDomain disagrees with TryGetDomainInfo");
        FuzzAssert.Equal(isValid && info.IsKnownPublicSuffix && info.RegistrableDomain is null, PublicSuffixList.IsPublicSuffix(domain, sources), "IsPublicSuffix disagrees with TryGetDomainInfo");
        if (!isValid)
        {
            FuzzAssert.Equal(default, info, "An invalid domain has a decomposition");
            return;
        }

        FuzzAssert.True(sources is not PublicSuffixRuleSources.None, "A domain is valid without any rule source");
        var expectedDomain = (domain.EndsWith('.', StringComparison.Ordinal) ? domain[..^1] : domain).ToLowerInvariant();
        FuzzAssert.Equal(expectedDomain, info.Domain, "The domain is not normalized");
        FuzzAssert.Equal(info.IsKnownPublicSuffix, info.Source is not PublicSuffixRuleSources.None, "IsKnownPublicSuffix disagrees with Source");
        FuzzAssert.True((info.Source & ~sources) is PublicSuffixRuleSources.None && info.Source is not PublicSuffixRuleSources.All, $"The source {info.Source} is not one of {sources}");
        FuzzAssert.True(IsLabelSuffix(info.Domain, info.PublicSuffix), $"The public suffix {FuzzAssert.Format(info.PublicSuffix)} is not a suffix of {FuzzAssert.Format(info.Domain)}");
        if (info.RegistrableDomain is null)
        {
            FuzzAssert.Equal(info.Domain, info.PublicSuffix, "A domain without a registrable domain is not a public suffix");
            FuzzAssert.Equal(null, info.Subdomain, "A domain without a registrable domain has a subdomain");
        }
        else
        {
            FuzzAssert.True(IsLabelSuffix(info.Domain, info.RegistrableDomain), $"The registrable domain {FuzzAssert.Format(info.RegistrableDomain)} is not a suffix of {FuzzAssert.Format(info.Domain)}");
            FuzzAssert.True(info.RegistrableDomain.Length > info.PublicSuffix.Length && IsLabelSuffix(info.RegistrableDomain, info.PublicSuffix), $"The registrable domain {FuzzAssert.Format(info.RegistrableDomain)} does not end with the public suffix");
            FuzzAssert.False(info.RegistrableDomain[..^(info.PublicSuffix.Length + 1)].Contains('.', StringComparison.Ordinal), $"The registrable domain {FuzzAssert.Format(info.RegistrableDomain)} has more than one label before the public suffix");
            var expectedSubdomain = info.Domain.Length == info.RegistrableDomain.Length ? null : info.Domain[..^(info.RegistrableDomain.Length + 1)];
            FuzzAssert.Equal(expectedSubdomain, info.Subdomain, "The subdomain is not the labels before the registrable domain");
        }

        FuzzAssert.True(PublicSuffixList.TryGetDomainInfo(info.Domain, out var normalized, sources), $"The normalized domain {FuzzAssert.Format(info.Domain)} is not valid");
        FuzzAssert.Equal(info, normalized, "The normalized domain has another decomposition");
        FuzzAssert.True(PublicSuffixList.TryGetDomainInfo(info.Domain + ".", out var fullyQualified, sources), "The fully-qualified domain is not valid");
        FuzzAssert.Equal(info, fullyQualified, "The fully-qualified domain has another decomposition");

        var upper = string.Create(info.Domain.Length, info.Domain, (span, value) =>
        {
            for (var i = 0; i < value.Length; i++)
            {
                span[i] = char.IsAsciiLetterLower(value[i]) ? (char)(value[i] - 'a' + 'A') : value[i];
            }
        });
        FuzzAssert.True(PublicSuffixList.TryGetDomainInfo(upper, out var upperInfo, sources), "The upper-case domain is not valid");
        FuzzAssert.Equal(info, upperInfo, "The upper-case domain has another decomposition");
    }

    private static bool IsLabelSuffix(string domain, string suffix)
        => suffix.Length > 0 && domain.EndsWith(suffix, StringComparison.Ordinal) && (domain.Length == suffix.Length || domain[^(suffix.Length + 1)] is '.');

    private static IEnumerable<FuzzSeed> GetPublicSuffixSeeds()
    {
        yield return new FuzzSeed("empty", Array.Empty<byte>());
        var source = FuzzResources.ReadText(typeof(UriFuzzTargets).Assembly, "public_suffix_tests.txt");
        foreach (var line in source.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length is 0 || trimmed.StartsWith("//", StringComparison.Ordinal))
                continue;

            var domain = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
            if (domain is "null")
                continue;

            for (var sources = 1; sources <= 3; sources++)
            {
                yield return FuzzSeed.FromText("psl-" + sources, [(byte)sources], domain);
            }
        }

        foreach (var literal in GetStringLiterals("PublicSuffixListTests.cs"))
        {
            yield return FuzzSeed.FromText("literal", [(byte)PublicSuffixRuleSources.All], literal);
        }
    }
}
