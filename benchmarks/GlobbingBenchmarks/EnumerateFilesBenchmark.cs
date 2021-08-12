using System.Globalization;
using System.IO;
using System.Linq;
using BenchmarkDotNet.Attributes;
using Meziantou.Framework;
using Meziantou.Framework.Globbing;

namespace GlobbingBenchmarks;

[MemoryDiagnoser]
[ReturnValueValidator]
[MarkdownExporterAttribute.Default]
public class EnumerateFilesBenchmark
{
    private const int N = 100_000;
    private static readonly FullPath s_directory = FullPath.GetTempPath() / "meziantou.framework" / "benchmarks" / "glob_flat";
    private static readonly FullPath s_directoryHierarchy = FullPath.GetTempPath() / "meziantou.framework" / "benchmarks" / "glob_hierarchy";

    private FullPath GetPath() => FullPath.GetTempPath() / "meziantou.framework" / "benchmarks" / Folder;

    [Params("glob_flat", "glob_hierarchy")]
    public string Folder { get; set; }

    [Params("*.txt", "file*.txt", "**/file*.txt", "folder[0-1]/**/f{ab,il}[aei]*.{txt,png,ico}")]
    public string Pattern { get; set; }

    [GlobalSetup]
    public static void Initialize()
    {
        InitFlat();
        InitHierarchy();

        void InitFlat()
        {
            Directory.CreateDirectory(s_directory);
            var existingFiles = Directory.GetFiles(s_directory);
            if (existingFiles.Length == N)
                return;

            foreach (var file in existingFiles)
            {
                File.Delete(file);
            }

            for (var i = 0; i < N; i++)
            {
                var extension = i switch
                {
                    < 1000 => ".cs",
                    < 2000 => ".md",
                    < 3000 => ".vb",
                    < 4000 => ".js",
                    < 5000 => ".ts",
                    < 6000 => ".json",
                    < 7000 => ".csproj",
                    < 8000 => ".bin",
                    < 9000 => ".sln",
                    _ => ".txt",
                };

                using var stream = File.Create(s_directory / ("file" + i.ToString("00000", CultureInfo.InvariantCulture) + extension));
            }
        }

        void InitHierarchy()
        {
            if (Directory.Exists(s_directoryHierarchy))
                return;

            Directory.CreateDirectory(s_directoryHierarchy);
            InitFolder(4, s_directoryHierarchy);

            void InitFolder(int level, FullPath root)
            {
                for (var i = 0; i < 4; i++)
                {
                    var folder = root / $"folder{i.ToString(CultureInfo.InvariantCulture)}";
                    Directory.CreateDirectory(folder);
                    if (level > 0)
                    {
                        InitFolder(level - 1, folder);
                    }
                }

                for (var i = 0; i < 20; i++)
                {
                    var extension = i switch
                    {
                        < 4 => ".cs",
                        < 8 => ".md",
                        < 12 => ".vb",
                        < 16 => ".js",
                        _ => ".txt",
                    };

                    using var stream = File.Create(root / ("file" + i.ToString("00000", CultureInfo.InvariantCulture) + extension));
                }
            }
        }
    }

    [Benchmark]
    public int Meziantou_Globbing()
    {
        var glob = Glob.Parse(Pattern, GlobOptions.None);
        return glob.EnumerateFiles(GetPath()).Count();
    }

    [Benchmark]
    public int Meziantou_Globbing_IgnoreCase()
    {
        var glob = Glob.Parse(Pattern, GlobOptions.IgnoreCase);
        return glob.EnumerateFiles(GetPath()).Count();
    }

    [Benchmark]
    public int GlobExpressions() => global::GlobExpressions.Glob.Files(GetPath(), Pattern).Count();

    [Benchmark]
    public int GlobExpressions_Compiled() => global::GlobExpressions.Glob.Files(GetPath(), Pattern, global::GlobExpressions.GlobOptions.Compiled).Count();
}
