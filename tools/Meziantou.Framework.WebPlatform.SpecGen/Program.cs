using SpecGen;

// Generates the C# code derived from machine-readable specification data (tests/Meziantou.Framework.WebPlatform.SpecData/).
// The output is checked in; CI verifies that running this tool produces no diff.
//
//   SpecGen            regenerate everything
//   SpecGen --check    fail if the generated files are out of date

var check = args.Contains("--check", StringComparer.Ordinal);
if (args.Contains("--dump-html", StringComparer.Ordinal))
{
    var root = RepositoryPaths.FindRoot();
    var idl = SpecGen.Idl.IdlModel.Load(Path.Combine(root, "tests", "Meziantou.Framework.WebPlatform.SpecData", "webref", "ed", "idlparsed"));
    var html = new HtmlSpecSource(File.ReadAllText(Path.Combine(root, "tests", "Meziantou.Framework.WebPlatform.SpecData", "html-source", "source")), idl.Get("ReferrerPolicy")!.EnumValues);
    foreach (var (key, definition) in html.EnumeratedAttributes.OrderBy(p => p.Key, StringComparer.Ordinal))
    {
        Console.WriteLine($"{key}: keywords=[{string.Join(", ", definition.Keywords.Select(k => k.Key + "->" + k.Value))}] missing={definition.MissingValueDefault} invalid={definition.InvalidValueDefault} empty={definition.EmptyValueDefault}");
    }

    foreach (var (key, content) in html.LimitedReflections.OrderBy(p => p.Key, StringComparer.Ordinal))
    {
        Console.WriteLine($"limited {key} -> {content}");
    }

    return 0;
}
var context = new GeneratorContext(RepositoryPaths.FindRoot(), check);

EncodingGenerator.Generate(context);
EncodingDetectorGenerator.Generate(context);
UnicodeGenerator.Generate(context);
ComplexShapingGenerator.Generate(context);
OpenTypeTagGenerator.Generate(context);
PublicSuffixListGenerator.Generate(context);
HyphenationGenerator.Generate(context);
SegmentationGenerator.Generate(context);
QuotesGenerator.Generate(context);
HtmlNamesGenerator.Generate(context);
ElementInterfacesGenerator.Generate(context);
CssPropertiesGenerator.Generate(context);
UserAgentStyleSheetGenerator.Generate(context);
SanitizerGenerator.Generate(context);
MathMLOperatorDictionaryGenerator.Generate(context);
WebIdlBindingsGenerator.Generate(context);

return context.Complete();
