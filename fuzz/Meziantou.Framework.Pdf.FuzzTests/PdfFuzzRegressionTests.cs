namespace Meziantou.Framework.Pdf.FuzzTests;

/// <summary>The inputs that fuzz campaigns found failing (<c>FuzzRegressions/</c>) satisfy the oracle.</summary>
public sealed class PdfFuzzRegressionTests
{
    public static TheoryData<string> Regressions
    {
        get
        {
            var directory = Path.Combine(AppContext.BaseDirectory, "FuzzRegressions");
            var data = new TheoryData<string>();
            if (Directory.Exists(directory))
            {
                foreach (var file in Directory.EnumerateFiles(directory).Where(f => !f.EndsWith(".md", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal))
                {
                    data.Add(Path.GetFileName(file));
                }
            }

            return data;
        }
    }

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(Regressions))]
    public void Replay(string name) => PdfFuzzOracle.Run(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "FuzzRegressions", name)), name);
}
