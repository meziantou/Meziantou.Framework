using BenchmarkDotNet.Attributes;
using Meziantou.Framework.Imaging.Benchmarks.Infrastructure;
using Meziantou.Framework.Imaging.Benchmarks.Workloads;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Benchmarks;

/// <summary>
/// The same PNG decode and encode from/to memory and from/to the file system, to separate in-memory work from I/O
/// (path loads read through a file stream; path saves publish atomically through a temporary file). The binary PPM load
/// from a file is the same comparison for an uncompressed input, whose decode is mostly reading (its in-memory
/// counterpart is <c>PnmBenchmarks.DecodePnmRgb</c>).
/// </summary>
[Config(typeof(WorkloadConfig))]
public class FileIOBenchmarks : IDisposable
{
    private readonly MemoryStream _output = new();
    private FullPath _directory;
    private FullPath _input;
    private FullPath _pnmInput;
    private FullPath _target;
    private FullPath _bmpTarget;
    private Image<Rgb24> _image = null!;

    [GlobalSetup]
    public void Setup()
    {
        _directory = FullPath.FromFileSystemInfo(Directory.CreateTempSubdirectory("mfi-bench-"));
        _input = _directory / "input.png";
        _target = _directory / "output.png";
        _bmpTarget = _directory / "output.bmp";
        File.WriteAllBytes(_input, BenchmarkInputs.LargePng8);
        _pnmInput = _directory / "input.ppm";
        File.WriteAllBytes(_pnmInput, BenchmarkInputs.PnmRgb);
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
    [Workload("2048x1536 binary PPM from file path")]
    public int LoadPnmFromFile()
    {
        using var image = Image.Load<Rgb24>(_pnmInput);
        return image.Width;
    }

    [Benchmark]
    [Workload("2048x1536 Rgb24 -> PNG into MemoryStream (fastest zlib)", ReturnsOutputBytes = true)]
    public long SaveToMemory() => ImageWorkloads.Encode(_image, new PngEncoder { CompressionLevel = System.IO.Compression.CompressionLevel.Fastest }, _output);

    [Benchmark]
    [Workload("2048x1536 Rgb24 -> PNG file path, atomic publish (fastest zlib)")]
    public void SaveToFile() => _image.Save(_target, new PngEncoder { CompressionLevel = System.IO.Compression.CompressionLevel.Fastest });

    // The file mode of a sequential writer used synchronously: Image.CreateWriter(path) opens its temporary file with
    // FileOptions.Asynchronous, since the writer has both synchronous and asynchronous methods. An uncompressed BMP makes
    // the writes (64 KB flushes) the main cost; the two stream cases isolate the file mode from the atomic publication.
    [Benchmark]
    [Workload("2048x1536 Rgb24 -> BMP, sequential writer to a file path (asynchronous handle, atomic publish)")]
    public void WriterToFilePath()
    {
        using var writer = Image.CreateWriter<Rgb24>(_bmpTarget, CreateBmpWriterOptions());
        writer.WriteFrame(_image.Frames[0]);
        writer.Complete();
    }

    [Benchmark]
    [Workload("2048x1536 Rgb24 -> BMP, sequential writer to a synchronous FileStream")]
    public void WriterToSynchronousFileStream() => WriteBmpToFileStream(FileOptions.None);

    [Benchmark]
    [Workload("2048x1536 Rgb24 -> BMP, sequential writer to an asynchronous FileStream")]
    public void WriterToAsynchronousFileStream() => WriteBmpToFileStream(FileOptions.Asynchronous);

    private ImageWriterOptions CreateBmpWriterOptions() => new(_image.Size) { Encoder = new BmpEncoder(), ExpectedFrameCount = 1 };

    private void WriteBmpToFileStream(FileOptions options)
    {
        using var stream = new FileStream(_bmpTarget, new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, Share = FileShare.None, BufferSize = 0, Options = options });
        using var writer = Image.CreateWriter<Rgb24>(stream, CreateBmpWriterOptions());
        writer.WriteFrame(_image.Frames[0]);
        writer.Complete();
    }

    public void Dispose()
    {
        Cleanup();
        _output.Dispose();
        GC.SuppressFinalize(this);
    }
}
