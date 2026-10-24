using System.Text.Json.Serialization;

namespace Meziantou.Framework.Pdf.TestHarness.Fixtures;

/// <summary>The serialization of the manifest, generated: the fixture generator runs without reflection.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(PdfFixtureManifest))]
internal sealed partial class PdfFixtureManifestJsonContext : JsonSerializerContext;
