using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Imaging.Benchmarks.Infrastructure;
using Meziantou.Framework.Imaging.Benchmarks.Workloads;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Benchmarks;

/// <summary>
/// The same PNG decode and encode from/to memory and from/to the file system, to separate in-memory work from I/O
/// (path loads read through a file stream; path saves publish atomically through a temporary file).
/// </summary>
[Config(typeof(WorkloadConfig))]
public class FileIOBenchmarks : IDisposable
{
    private readonly MemoryStream _output = new();
    private FullPath _directory;
    private FullPath _input;
    private FullPath _target;
    private Image<Rgb24> _image = null!;

    [GlobalSetup]
    public void Setup()
    {
        _directory = FullPath.FromFileSystemInfo(Directory.CreateTempSubdirectory("mfi-bench-"));
        _input = _directory / "input.png";
        _target = _directory / "output.png";
        File.WriteAllBytes(_input, BenchmarkInputs.LargePng8);
        _image = Image.Load<Rgb24>(BenchmarkInputs.LargePng8);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _image?.Dispose();
        if (!_directory.IsEmpty && Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Benchmark(Baseline = true)]
    [Workload("2048x1536 RGB8 PNG from byte[]")]
    public int LoadFromMemory()
    {
        using var image = Image.Load<Rgb24>(BenchmarkInputs.LargePng8);
        return image.Width;
    }

    [Benchmark]
    [Workload("2048x1536 RGB8 PNG from file path")]
    public int LoadFromFile()
    {
        using var image = Image.Load<Rgb24>(_input);
        return image.Width;
    }

    [Benchmark]
    [Workload("2048x1536 Rgb24 -> PNG into MemoryStream (fastest zlib)", ReturnsOutputBytes = true)]
    public long SaveToMemory() => ImageWorkloads.Encode(_image, new PngEncoder { CompressionLevel = System.IO.Compression.CompressionLevel.Fastest }, _output);

    [Benchmark]
    [Workload("2048x1536 Rgb24 -> PNG file path, atomic publish (fastest zlib)")]
    public void SaveToFile() => _image.Save(_target, new PngEncoder { CompressionLevel = System.IO.Compression.CompressionLevel.Fastest });

    public void Dispose()
    {
        Cleanup();
        _output.Dispose();
        GC.SuppressFinalize(this);
    }
}
