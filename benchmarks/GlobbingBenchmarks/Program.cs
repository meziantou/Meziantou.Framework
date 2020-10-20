using System.Globalization;
using System.IO;
using System.Linq;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Meziantou.Framework;
using Meziantou.Framework.Globbing;

namespace GlobbingBenchmarks
{
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

    //[MemoryDiagnoser]
    [ReturnValueValidator]
    [MarkdownExporterAttribute.Default]
    public class GlobIsMatchBenchmark
    {
        private Glob _meziantouGlob;
        private GlobExpressions.Glob _globExpressions;
        private GlobExpressions.Glob _globExpressionsCompiled;
        private DotNet.Globbing.Glob _dotnetGlob;

        [Params(new object[]{
            "*.txt",
            "**/*.txt",
            "file*.txt",
            "**/file*.txt",
            "folder[0-1]/**/f{ab,il}[aei]*.{txt,png,ico}",
        })]
        public string Pattern { get; set; }

        [Params(new object[]
        {
            "test.txt",
            "file0001.txt",
            "file00000000000001.txt",
            "test01/test02/test03/test04/file0001.txt",
        })]
        public string Path { get; set; }

        [GlobalSetup]
        public void Initialize()
        {
            _meziantouGlob = Glob.Parse(Pattern, GlobOptions.None);
            _globExpressions = new GlobExpressions.Glob(Pattern, GlobExpressions.GlobOptions.MatchFullPath);
            _globExpressionsCompiled = new GlobExpressions.Glob(Pattern, GlobExpressions.GlobOptions.MatchFullPath | GlobExpressions.GlobOptions.Compiled);
            _dotnetGlob = DotNet.Globbing.Glob.Parse(Pattern);
        }

        [Benchmark]
        public bool Meziantou_Globbing() => _meziantouGlob.IsMatch(Path);

        [Benchmark]
        public bool GlobExpressions_None() => _globExpressions.IsMatch(Path);

        [Benchmark]
        public bool GlobExpressions_Compiled() => _globExpressionsCompiled.IsMatch(Path);

        [Benchmark]
        public bool DotNetGlobbing() => _dotnetGlob.IsMatch(Path);
    }

    internal static class Program
    {
        private static void Main()
        {
            BenchmarkRunner.Run<GlobIsMatchBenchmark>();
            BenchmarkRunner.Run<EnumerateFilesBenchmark>();
        }
    }
}
