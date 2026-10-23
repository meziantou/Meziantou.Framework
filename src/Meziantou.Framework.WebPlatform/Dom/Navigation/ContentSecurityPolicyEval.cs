using System.Diagnostics;
using System.Security.Cryptography;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The kind of code compiled from strings (the <c>compilationType</c> of
/// https://tc39.es/proposal-dynamic-code-brand-checks/#sec-hostensurecancompilestrings).
/// </summary>
internal enum StringCompilationType
{
    /// <summary><c>eval()</c> (direct or indirect).</summary>
    Eval,

    /// <summary>The <c>Function</c>, <c>AsyncFunction</c>, <c>GeneratorFunction</c> and <c>AsyncGeneratorFunction</c> constructors.</summary>
    Function,

    /// <summary>The string handlers of <c>setTimeout()</c> and <c>setInterval()</c>.</summary>
    Timer,
}

/// <summary>
/// The integration of the content security policies with ECMAScript and WebAssembly: the <c>'unsafe-eval'</c>,
/// <c>'trusted-types-eval'</c> and <c>'wasm-unsafe-eval'</c> source expressions of <c>script-src</c> (or
/// <c>default-src</c>) allow the compilation of strings (https://w3c.github.io/webappsec-csp/#can-compile-strings) and of
/// WebAssembly bytes (https://w3c.github.io/webappsec-csp/#can-compile-wasm-bytes). The script engines call
/// <see cref="EnsureCanCompileStrings"/> from their HostEnsureCanCompileStrings hook, the timers and the WebAssembly
/// namespace call the checks themselves. The global objects without CSP list pay a null check.
/// </summary>
internal static class ContentSecurityPolicyEval
{
    /// <summary>
    /// https://w3c.github.io/webappsec-csp/#can-compile-strings (EnsureCSPDoesNotBlockStringCompilation), with the Trusted
    /// Types checks of the code (https://w3c.github.io/trusted-types/dist/spec/#integration-with-ecmascript): the violations
    /// of the policies of the CSP list of <paramref name="global"/> are reported, and the compilation is blocked by the
    /// enforced ones.
    /// </summary>
    /// <param name="global">The global object of the realm that compiles the code (the realm of <c>eval</c> or of the <c>Function</c> constructor).</param>
    /// <param name="parameterStrings">The parameters of a <c>Function</c> constructor (the arguments converted to strings), else empty.</param>
    /// <param name="bodyString">The body of the function, or the code of <c>eval()</c> or of a timer.</param>
    /// <param name="codeString">
    /// The code to compile (for a <c>Function</c> constructor, the source text of the function built from the parameters and
    /// the body), or null when the engine cannot give it: the Trusted Types default policy cannot run, and the violations
    /// have no sample.
    /// </param>
    /// <param name="compilationType">The kind of compilation.</param>
    /// <param name="parameterArgs">
    /// The <see cref="TrustedScript"/> given as each parameter of a <c>Function</c> constructor, or null for the arguments that
    /// are not a <see cref="TrustedScript"/> (HostGetCodeForEval, https://html.spec.whatwg.org/multipage/webappapis.html#hostgetcodeforeval(argument)).
    /// </param>
    /// <param name="bodyArg">The <see cref="TrustedScript"/> given as the body or the code, or null.</param>
    /// <exception cref="EvalErrorException">The compilation is blocked.</exception>
    public static void EnsureCanCompileStrings(GlobalScope global, IReadOnlyList<string> parameterStrings, string bodyString, string? codeString, StringCompilationType compilationType, IReadOnlyList<TrustedScript?> parameterArgs, TrustedScript? bodyArg)
    {
        var document = global.EnvironmentDocument;
        if (document.ContentSecurityPoliciesOrNull is null && document.TrustedTypesDirectivesOrNull is null)
            return;

        // Step 1
        var sourceString = codeString;

        // Step 2: the Trusted Types checks of the code (not of the timers, whose handler is checked by the timer initialization steps)
        if (compilationType != StringCompilationType.Timer)
        {
            // Step 2.1
            var compilationSink = compilationType == StringCompilationType.Function ? "Function" : "eval";

            // Steps 2.2 and 2.3: the code is trusted when the body and all the parameters are TrustedScript objects (their
            // strings are their data, HostGetCodeForEval)
            var isTrusted = bodyArg is not null;
            if (isTrusted)
            {
                Debug.Assert(string.Equals(bodyString, bodyArg!.Data, StringComparison.Ordinal));
                Debug.Assert(parameterArgs.Count == parameterStrings.Count);
                for (var index = 0; index < parameterArgs.Count; index++)
                {
                    if (parameterArgs[index] is not { } arg)
                    {
                        isTrusted = false;
                        break;
                    }

                    Debug.Assert(string.Equals(parameterStrings[index], arg.Data, StringComparison.Ordinal));
                }
            }

            // Step 2.4
            if (!isTrusted)
            {
                try
                {
                    if (codeString is not null)
                    {
                        sourceString = TrustedTypesAlgorithms.GetCompliantString(TrustedTypeName.TrustedScript, document, codeString, compilationSink);
                    }
                    else if (TrustedTypesAlgorithms.IsSinkTypeMismatchBlocked(document, compilationSink, ""))
                    {
                        // The code compiled by the engine is unknown: it is blocked when Trusted Types are required, as if no
                        // default policy converted it
                        throw new TypeErrorException($"This document requires 'TrustedScript' assignment ({compilationSink})");
                    }
                }
#pragma warning disable CA1031 // Any error of the algorithm (including the one of the default policy) is an EvalError
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    throw new EvalErrorException($"The compilation of a string is blocked by Trusted Types ({compilationSink}): {ex.Message}", ex);
                }

                if (!string.Equals(sourceString, codeString, StringComparison.Ordinal))
                    throw new EvalErrorException($"The compilation of a string is blocked by Trusted Types ({compilationSink}): the default policy changed the code");
            }
        }

        if (document.ContentSecurityPoliciesOrNull is not { } policies)
            return;

        // Step 3
        var blocked = false;

        // Step 5
        foreach (var policy in policies)
        {
            // Steps 5.1 and 5.2
            if (GetScriptSourceList(policy) is not { } sourceList)
                continue;

            // Step 5.3.1 and 5.3.2: 'trusted-types-eval' allows the compilation when Trusted Types are enforced
            if (ContainsExpression(sourceList, "'trusted-types-eval'") && TrustedTypesAlgorithms.DoesSinkTypeRequireTrustedTypes(TrustedTypePolicyFactory.GetDirectives(document), includeReportOnlyPolicies: false))
                continue;

            // The tentative 'eval-sha256-…' expressions (https://github.com/w3c/webappsec-csp/issues/655): they allow the code whose
            // hash matches, and 'unsafe-eval' does not apply to a source list that has some, as 'unsafe-inline' with hashes
            var hasEvalHashes = false;
            var codeBytes = sourceString is null ? null : System.Text.Encoding.UTF8.GetBytes(sourceString);
            var matchesHash = false;
            foreach (var item in sourceList)
            {
                if (TryGetEvalHash(item, out var algorithm, out var expected))
                {
                    hasEvalHashes = true;
                    if (codeBytes is not null)
                    {
                        var actual = algorithm switch
                        {
                            "sha256" => SHA256.HashData(codeBytes),
                            "sha384" => SHA384.HashData(codeBytes),
                            _ => SHA512.HashData(codeBytes),
                        };
                        matchesHash |= Convert.ToBase64String(actual) == expected;
                    }
                }
            }

            if (matchesHash)
                continue;

            // Step 5.3.3
            if (!hasEvalHashes && ContainsExpression(sourceList, "'unsafe-eval'"))
                continue;

            // Steps 5.3.4 to 5.3.8
            var violation = ContentSecurityPolicyViolation.Create(document, policy, "script-src");
            violation.Resource = "eval";
            if (codeBytes is not null)
            {
                violation.EvalHash = "eval-sha256-" + Convert.ToBase64String(SHA256.HashData(codeBytes));
            }

            if (ContainsExpression(sourceList, "'report-sample'"))
            {
                violation.Sample = ContentSecurityPolicyViolation.ClipSample(sourceString ?? "");
            }

            violation.Report();
            if (!policy.ReportOnly)
            {
                blocked = true;
            }
        }

        // Step 6
        if (blocked)
            throw new EvalErrorException("The compilation of a string is blocked by the 'script-src' directive of the Content Security Policy ('unsafe-eval' is not allowed)");
    }

    /// <summary>
    /// https://w3c.github.io/webappsec-csp/#can-compile-wasm-bytes (EnsureCSPDoesNotBlockWasmByteCompilation, the
    /// HostEnsureCanCompileWasmBytes hook): whether the compilation of a WebAssembly module is blocked by an enforced policy
    /// of the CSP list of <paramref name="global"/> (the caller throws a <c>WebAssembly.CompileError</c>). The violations
    /// are reported.
    /// </summary>
    public static bool IsWasmByteCompilationBlocked(GlobalScope global)
    {
        if (global.EnvironmentDocument.ContentSecurityPoliciesOrNull is not { } policies)
            return false;

        var blocked = false;
        foreach (var policy in policies)
        {
            // Step 3.3
            if (GetScriptSourceList(policy) is not { } sourceList || ContainsExpression(sourceList, "'unsafe-eval'") || ContainsExpression(sourceList, "'wasm-unsafe-eval'"))
                continue;

            var violation = ContentSecurityPolicyViolation.Create(global.EnvironmentDocument, policy, "script-src");
            violation.Resource = "wasm-eval";
            violation.Report();
            if (!policy.ReportOnly)
            {
                blocked = true;
            }
        }

        return blocked;
    }

    private static bool TryGetEvalHash(string item, out string algorithm, out string value)
    {
        algorithm = "";
        value = "";
        if (item.Length < 2 || item[0] != '\'' || item[^1] != '\'')
            return false;

        var inner = item.AsSpan(1, item.Length - 2);
        foreach (var candidate in (ReadOnlySpan<string>)["sha256", "sha384", "sha512"])
        {
            if (inner.Length > candidate.Length + 6 && inner.StartsWith("eval-", StringComparison.OrdinalIgnoreCase) && inner[5..].StartsWith(candidate, StringComparison.OrdinalIgnoreCase) && inner[5 + candidate.Length] == '-')
            {
                algorithm = candidate;
                value = inner[(6 + candidate.Length)..].ToString();
                return true;
            }
        }

        return false;
    }

    /// <summary>The value of the <c>script-src</c> directive of the policy, else of its <c>default-src</c> directive, or null.</summary>
    private static List<string>? GetScriptSourceList(ContentSecurityPolicy policy) => policy.GetDirectiveValue("script-src") ?? policy.GetDirectiveValue("default-src");

    /// <summary>Whether the source list contains a source expression that is an ASCII case-insensitive match for <paramref name="expression"/>.</summary>
    private static bool ContainsExpression(List<string> sourceList, string expression)
    {
        foreach (var item in sourceList)
        {
            if (InfraAscii.EqualsIgnoreAsciiCase(item, expression))
                return true;
        }

        return false;
    }
}
