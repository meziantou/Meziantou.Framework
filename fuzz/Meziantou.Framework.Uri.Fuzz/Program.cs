// The differential targets match the same patterns with WebPlatform, whose regular expressions have no timeout: as
// documented, an application that matches untrusted patterns sets a default timeout (catastrophic backtracking)
AppDomain.CurrentDomain.SetData("REGEX_DEFAULT_MATCH_TIMEOUT", TimeSpan.FromSeconds(1));

return Meziantou.Framework.Fuzzing.FuzzProgram.Run(args, () => UriFuzz.UriFuzzTargets.All);
