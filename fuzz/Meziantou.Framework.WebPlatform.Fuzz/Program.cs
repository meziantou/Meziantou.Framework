// The regular expressions of the URL patterns are user-supplied ECMAScript regular expressions, which have no timeout:
// as documented, an application that matches untrusted patterns sets a default timeout (catastrophic backtracking)
AppDomain.CurrentDomain.SetData("REGEX_DEFAULT_MATCH_TIMEOUT", TimeSpan.FromSeconds(1));

return Meziantou.Framework.Fuzzing.FuzzProgram.Run(args, () => Meziantou.Framework.WebPlatform.Fuzz.WebPlatformFuzzTargets.All);
