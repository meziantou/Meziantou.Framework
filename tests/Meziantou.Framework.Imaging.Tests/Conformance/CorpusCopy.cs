using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.Imaging.TestHarness.Fixtures;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>A disposable temporary copy of the committed corpus, used to prove that corruption is detected.</summary>
internal sealed class CorpusCopy : IDisposable
{
    public CorpusCopy()
    {
        Root = FullPath.GetTempPath() / ("mfi-corpus-" + Guid.NewGuid().ToString("N"));
        var source = FixtureRoot.GetDirectory();
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Root / FullPath.FromPath(file).MakePathRelativeTo(source);
            destination.CreateParentDirectory();
            File.Copy(file, destination);
        }
    }

    public FullPath Root { get; }

    public FullPath ManifestPath => Root / FixtureRoot.ManifestFileName;

    public FullPath GetPath(string relativePath) => Root / relativePath;

    public IReadOnlyList<string> Validate() => FixtureManifestValidator.Validate(FixtureManifestLoader.Load(ManifestPath), Root);

    /// <summary>Edits the manifest JSON of one fixture.</summary>
    public void EditFixture(string id, Action<JsonObject> edit)
    {
        var root = JsonNode.Parse(File.ReadAllText(ManifestPath))!.AsObject();
        var fixture = root["fixtures"]!.AsArray().Single(item => (string?)item!["id"] == id)!.AsObject();
        edit(fixture);
        File.WriteAllText(ManifestPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true }));
    }

    public void Dispose() => Directory.Delete(Root, recursive: true);
}
