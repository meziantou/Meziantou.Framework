namespace Meziantou.Framework.JavaScriptEngine.Test262.Runner;

/// <summary>A test file of test262 (<see cref="Id"/> is its path relative to the <c>test</c> directory, with '/').</summary>
internal sealed class Test262Test(string id, string fullPath, Test262Metadata metadata)
{
    public string Id { get; } = id;

    public string FullPath { get; } = fullPath;

    public Test262Metadata Metadata { get; } = metadata;

    /// <summary>The modes the test runs in: <c>module</c>, or <c>strict</c> and/or <c>sloppy</c>.</summary>
    public IReadOnlyList<Test262Mode> Modes
    {
        get
        {
            if (Metadata.IsModule)
                return [Test262Mode.Module];

            if (Metadata.IsRaw || Metadata.HasFlag("noStrict"))
                return [Test262Mode.Sloppy];

            if (Metadata.HasFlag("onlyStrict"))
                return [Test262Mode.Strict];

            return [Test262Mode.Sloppy, Test262Mode.Strict];
        }
    }

    public override string ToString() => Id;
}
