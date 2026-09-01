using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using Meziantou.Framework;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

#pragma warning disable CA1848 // Use the LoggerMessage delegates

namespace Meziantou.Extensions.Logging.FileLogger.Tests;

public sealed class FileLoggerProviderTests
{
    private static readonly DateTimeOffset StartDate = new(2024, 1, 2, 3, 4, 5, TimeSpan.Zero);

    [Fact]
    public async Task WritesLogToFile()
    {
        using var tempDirectory = TemporaryDirectory.Create();
        var provider = new FileLoggerProvider(tempDirectory);

        try
        {
            var logger = provider.CreateLogger("Test.Namespace.Category");

            logger.LogInformation("Hello from test");
            provider.Dispose();

            var logFilePath = provider.LogFilePath;
            Assert.True(File.Exists(logFilePath));

            var content = await File.ReadAllTextAsync(logFilePath);
            Assert.Contains("Hello from test", content);
            Assert.Contains("[INFO]", content);
            Assert.Contains("[Test.Namespace.Category]", content);
        }
        finally
        {
            provider.Dispose();
        }
    }

    [Fact]
    public async Task WritesExceptionToFile()
    {
        using var tempDirectory = TemporaryDirectory.Create();
        await using var provider = new FileLoggerProvider(tempDirectory);
        var logger = provider.CreateLogger("Test");

        logger.LogError(new InvalidOperationException("Sample exception"), "Something failed");
        await provider.FlushAsync(TestContext.Current.CancellationToken);

        var content = await ReadLogFileAsync(provider.LogFilePath);
        Assert.Contains("[FAIL]", content);
        Assert.Contains("Something failed", content);
        Assert.Contains(nameof(InvalidOperationException), content);
        Assert.Contains("Sample exception", content);
    }

    [Fact]
    public async Task UseShortCategoryName()
    {
        using var tempDirectory = TemporaryDirectory.Create();
        await using var provider = new FileLoggerProvider(new FileLoggerOptions { Directory = tempDirectory.FullPath, UseShortCategoryName = true });
        provider.CreateLogger("Test.Namespace.Category").LogInformation("Hello");
        await provider.FlushAsync(TestContext.Current.CancellationToken);

        var content = await ReadLogFileAsync(provider.LogFilePath);
        Assert.Contains("[Category]", content);
        Assert.DoesNotContain("Test.Namespace", content);
    }

    [Fact]
    public async Task MinLevelFiltersMessages()
    {
        using var tempDirectory = TemporaryDirectory.Create();
        await using var provider = new FileLoggerProvider(new FileLoggerOptions { Directory = tempDirectory.FullPath, MinLevel = LogLevel.Warning });
        var logger = provider.CreateLogger("Test");

        Assert.False(logger.IsEnabled(LogLevel.Information));
        Assert.True(logger.IsEnabled(LogLevel.Warning));

        logger.LogInformation("ignored message");
        logger.LogWarning("kept message");
        await provider.FlushAsync(TestContext.Current.CancellationToken);

        var content = await ReadLogFileAsync(provider.LogFilePath);
        Assert.DoesNotContain("ignored message", content);
        Assert.Contains("kept message", content);
    }

    [Fact]
    public async Task IncludeScopes()
    {
        using var tempDirectory = TemporaryDirectory.Create();
        await using var provider = new FileLoggerProvider(new FileLoggerOptions { Directory = tempDirectory.FullPath, IncludeScopes = true });
        using var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(provider));
        var logger = loggerFactory.CreateLogger("Test");

        using (logger.BeginScope("OuterScope"))
        using (logger.BeginScope("InnerScope"))
        {
            logger.LogInformation("Hello");
        }

        logger.LogInformation("Outside");
        await provider.FlushAsync(TestContext.Current.CancellationToken);

        var lines = (await ReadLogFileAsync(provider.LogFilePath)).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("=> OuterScope => InnerScope Hello", lines[0]);
        Assert.DoesNotContain("=>", lines[1]);
    }

    [Fact]
    public async Task IncludeEventIdThreadIdAndActivity()
    {
        using var tempDirectory = TemporaryDirectory.Create();
        await using var provider = new FileLoggerProvider(new FileLoggerOptions
        {
            Directory = tempDirectory.FullPath,
            IncludeEventId = true,
            IncludeThreadId = true,
            IncludeActivityTracking = true,
        });

        using var activitySource = new ActivitySource("Test");
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name is "Test",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = activitySource.StartActivity("Test");
        Assert.NotNull(activity);

        var threadId = Environment.CurrentManagedThreadId;
        provider.CreateLogger("Test").LogInformation(new EventId(42, "SampleEvent"), "Hello");
        await provider.FlushAsync(TestContext.Current.CancellationToken);

        var content = await ReadLogFileAsync(provider.LogFilePath);
        Assert.Contains("[EventId:42:SampleEvent]", content);
        Assert.Contains($"[ThreadId:{threadId.ToString(CultureInfo.InvariantCulture)}]", content);
        Assert.Contains($"[TraceId:{activity.TraceId.ToHexString()}", content);
    }

    [Fact]
    public async Task JsonFormatter()
    {
        using var tempDirectory = TemporaryDirectory.Create();
        await using var provider = new FileLoggerProvider(new FileLoggerOptions
        {
            Directory = tempDirectory.FullPath,
            FormatterName = FileFormatterNames.Json,
            IncludeScopes = true,
        });

        using var loggerFactory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(provider));
        var logger = loggerFactory.CreateLogger("Test.Category");

        using (logger.BeginScope("Scope1"))
        {
            logger.LogWarning("Hello {Name}", "world");
        }

        await provider.FlushAsync(TestContext.Current.CancellationToken);

        var content = await ReadLogFileAsync(provider.LogFilePath);
        using var document = JsonDocument.Parse(content);
        var root = document.RootElement;
        Assert.Equal("WARN", root.GetProperty("LogLevel").GetString());
        Assert.Equal("Test.Category", root.GetProperty("Category").GetString());
        Assert.Equal("Hello world", root.GetProperty("Message").GetString());
        Assert.Equal("world", root.GetProperty("State").GetProperty("Name").GetString());
        Assert.Equal("Hello {Name}", root.GetProperty("State").GetProperty("{OriginalFormat}").GetString());
        Assert.Equal("Scope1", root.GetProperty("Scopes")[0].GetString());
    }

    [Fact]
    public async Task CustomFormatter()
    {
        using var tempDirectory = TemporaryDirectory.Create();
        await using var provider = new FileLoggerProvider(new FileLoggerOptions { Directory = tempDirectory.FullPath, Formatter = new UppercaseFileFormatter() });
        provider.CreateLogger("Test").LogInformation("Hello");
        await provider.FlushAsync(TestContext.Current.CancellationToken);

        var content = await ReadLogFileAsync(provider.LogFilePath);
        Assert.Equal("HELLO", content.Trim());
    }

    [Fact]
    public async Task RollFileOnMaxFileSize()
    {
        using var tempDirectory = TemporaryDirectory.Create();
        await using var provider = new FileLoggerProvider(new FileLoggerOptions
        {
            Directory = tempDirectory.FullPath,
            MaxFileSizeInBytes = 200,
            TimestampFormat = null,
            IncludeCategory = false,
            IncludeLogLevel = false,
        });

        var logger = provider.CreateLogger("Test");
        for (var i = 0; i < 20; i++)
        {
            logger.LogInformation("Message {Index} ..........", i);
        }

        await provider.FlushAsync(TestContext.Current.CancellationToken);

        var files = Directory.GetFiles(tempDirectory.FullPath);
        var fileCount = files.Length;
        Assert.True(fileCount > 1, $"Expected multiple log files, got {fileCount}");
        foreach (var file in files)
        {
            Assert.True(new FileInfo(file).Length <= 200, "A log file is bigger than the maximum size");
        }
    }

    [Fact]
    public async Task RollFileOnInterval()
    {
        using var tempDirectory = TemporaryDirectory.Create();
        var timeProvider = new FakeTimeProvider(StartDate);
        await using var provider = new FileLoggerProvider(new FileLoggerOptions { Directory = tempDirectory.FullPath, RollInterval = RollInterval.Daily }, timeProvider);
        var logger = provider.CreateLogger("Test");

        logger.LogInformation("First day");
        await provider.FlushAsync(TestContext.Current.CancellationToken);
        var firstFile = provider.LogFilePath;

        timeProvider.Advance(TimeSpan.FromDays(1));
        logger.LogInformation("Second day");
        await provider.FlushAsync(TestContext.Current.CancellationToken);
        var secondFile = provider.LogFilePath;

        Assert.NotEqual(firstFile, secondFile);
        var pid = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
        Assert.Equal($"2024-01-02-{pid}.log", Path.GetFileName(firstFile));
        Assert.Equal($"2024-01-03-{pid}.log", Path.GetFileName(secondFile));
        Assert.Contains("First day", await ReadLogFileAsync(firstFile));
        Assert.Contains("Second day", await ReadLogFileAsync(secondFile));
    }

    [Fact]
    public async Task MaxRetainedFiles()
    {
        using var tempDirectory = TemporaryDirectory.Create();
        var timeProvider = new FakeTimeProvider(StartDate);
        await using var provider = new FileLoggerProvider(new FileLoggerOptions
        {
            Directory = tempDirectory.FullPath,
            RollInterval = RollInterval.Daily,
            MaxRetainedFiles = 2,
        }, timeProvider);

        var logger = provider.CreateLogger("Test");
        for (var i = 0; i < 5; i++)
        {
            logger.LogInformation("Day {Index}", i);
            await provider.FlushAsync(TestContext.Current.CancellationToken);
            timeProvider.Advance(TimeSpan.FromDays(1));
        }

        var pid = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
        Assert.Equal([$"2024-01-05-{pid}.log", $"2024-01-06-{pid}.log"], GetFileNames(tempDirectory));
    }

    [Fact]
    public async Task RetentionPolicyKeepsTheFilesOfTheOtherProcesses()
    {
        using var tempDirectory = TemporaryDirectory.Create();
        var timeProvider = new FakeTimeProvider(StartDate);

        // Log files that another process is writing in the same directory. They are older than the
        // files of this process, so the retention policy would delete them first if it considered them
        var otherProcessId = (Environment.ProcessId + 1).ToString(CultureInfo.InvariantCulture);
        string[] otherProcessFiles = [$"2024-01-01-{otherProcessId}.log", $"2024-01-01-{otherProcessId}_001.log"];
        foreach (var fileName in otherProcessFiles)
        {
            await File.WriteAllTextAsync(Path.Combine(tempDirectory.FullPath, fileName), "other process", TestContext.Current.CancellationToken);
        }

        await using var provider = new FileLoggerProvider(new FileLoggerOptions
        {
            Directory = tempDirectory.FullPath,
            RollInterval = RollInterval.Daily,
            MaxRetainedFiles = 2,
        }, timeProvider);

        var logger = provider.CreateLogger("Test");
        for (var i = 0; i < 5; i++)
        {
            logger.LogInformation("Day {Index}", i);
            await provider.FlushAsync(TestContext.Current.CancellationToken);
            timeProvider.Advance(TimeSpan.FromDays(1));
        }

        var pid = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
        Assert.Equal([.. otherProcessFiles, $"2024-01-05-{pid}.log", $"2024-01-06-{pid}.log"], GetFileNames(tempDirectory));
    }

    [Fact]
    public async Task CompressOnRoll()
    {
        using var tempDirectory = TemporaryDirectory.Create();
        var timeProvider = new FakeTimeProvider(StartDate);
        await using var provider = new FileLoggerProvider(new FileLoggerOptions
        {
            Directory = tempDirectory.FullPath,
            RollInterval = RollInterval.Daily,
            Compression = LogFileCompression.GZip,
            CompressionMode = LogFileCompressionMode.OnRoll,
        }, timeProvider);

        var logger = provider.CreateLogger("Test");
        logger.LogInformation("First day");
        await provider.FlushAsync(TestContext.Current.CancellationToken);

        timeProvider.Advance(TimeSpan.FromDays(1));
        logger.LogInformation("Second day");
        await provider.FlushAsync(TestContext.Current.CancellationToken);

        var pid = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
        var files = GetFileNames(tempDirectory);
        Assert.Equal([$"2024-01-02-{pid}.log.gz", $"2024-01-03-{pid}.log"], files);

        // The current file is not compressed
        Assert.Contains("Second day", await ReadLogFileAsync(provider.LogFilePath));
        Assert.Contains("First day", await ReadCompressedFileAsync(Path.Combine(tempDirectory.FullPath, files[0]), LogFileCompression.GZip));
    }

    [Theory]
    [InlineData(LogFileCompression.GZip, ".gz")]
    [InlineData(LogFileCompression.Brotli, ".br")]
#if NET11_0_OR_GREATER
    [InlineData(LogFileCompression.Zstandard, ".zst")]
#endif
    public async Task CompressWhileWriting(LogFileCompression compression, string extension)
    {
        using var tempDirectory = TemporaryDirectory.Create();
        var timeProvider = new FakeTimeProvider(StartDate);
        var pid = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);

        await using (var provider = new FileLoggerProvider(new FileLoggerOptions
        {
            Directory = tempDirectory.FullPath,
            RollInterval = RollInterval.Daily,
            Compression = compression,
        }, timeProvider))
        {
            var logger = provider.CreateLogger("Test");
            logger.LogInformation("First day");
            await provider.FlushAsync(TestContext.Current.CancellationToken);

            timeProvider.Advance(TimeSpan.FromDays(1));
            logger.LogInformation("Second day");
        }

        var files = GetFileNames(tempDirectory);
        Assert.Equal([$"2024-01-02-{pid}.log{extension}", $"2024-01-03-{pid}.log{extension}"], files);
        Assert.Contains("First day", await ReadCompressedFileAsync(Path.Combine(tempDirectory.FullPath, files[0]), compression));
        Assert.Contains("Second day", await ReadCompressedFileAsync(Path.Combine(tempDirectory.FullPath, files[1]), compression));
    }

    [Fact]
    public async Task CompressWhileWritingUsesTheCompressionLevel()
    {
        var uncompressedSize = await GetLogFileSizeAsync(CompressionLevel.NoCompression);
        var compressedSize = await GetLogFileSizeAsync(CompressionLevel.SmallestSize);
        Assert.True(compressedSize < uncompressedSize, $"The file compressed with SmallestSize ({compressedSize} bytes) is not smaller than the one written with NoCompression ({uncompressedSize} bytes)");

        static async Task<long> GetLogFileSizeAsync(CompressionLevel level)
        {
            using var tempDirectory = TemporaryDirectory.Create();
            await using (var provider = new FileLoggerProvider(new FileLoggerOptions
            {
                Directory = tempDirectory.FullPath,
                Compression = LogFileCompression.GZip,
                CompressionLevel = level,
            }))
            {
                var logger = provider.CreateLogger("Test");
                for (var i = 0; i < 200; i++)
                {
                    logger.LogInformation("A very repetitive message that should compress very well");
                }
            }

            return new FileInfo(Directory.GetFiles(tempDirectory.FullPath).Single()).Length;
        }
    }

    [Fact]
    public async Task CompressWhileWritingIgnoresAppend()
    {
        using var tempDirectory = TemporaryDirectory.Create();
        var timeProvider = new FakeTimeProvider(StartDate);
        var options = new FileLoggerOptions
        {
            Directory = tempDirectory.FullPath,
            RollInterval = RollInterval.Daily,
            Append = true,
            Compression = LogFileCompression.GZip,
        };

        for (var i = 0; i < 2; i++)
        {
            await using var provider = new FileLoggerProvider(options, timeProvider);
            provider.CreateLogger("Test").LogInformation("Run {Index}", i);
        }

        var pid = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
        Assert.Equal([$"2024-01-02-{pid}.log.gz", $"2024-01-02-{pid}_001.log.gz"], GetFileNames(tempDirectory));
    }

    [Fact]
    public async Task CompressedFilesAreDeletedByTheRetentionPolicy()
    {
        using var tempDirectory = TemporaryDirectory.Create();
        var timeProvider = new FakeTimeProvider(StartDate);
        await using var provider = new FileLoggerProvider(new FileLoggerOptions
        {
            Directory = tempDirectory.FullPath,
            RollInterval = RollInterval.Daily,
            Compression = LogFileCompression.GZip,
            MaxRetainedFiles = 2,
        }, timeProvider);

        var logger = provider.CreateLogger("Test");
        for (var i = 0; i < 5; i++)
        {
            logger.LogInformation("Day {Index}", i);
            await provider.FlushAsync(TestContext.Current.CancellationToken);
            timeProvider.Advance(TimeSpan.FromDays(1));
        }

        var pid = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
        Assert.Equal([$"2024-01-05-{pid}.log.gz", $"2024-01-06-{pid}.log.gz"], GetFileNames(tempDirectory));
    }

    [Fact]
    public async Task TwoProvidersUseDifferentFiles()
    {
        using var tempDirectory = TemporaryDirectory.Create();
        var timeProvider = new FakeTimeProvider(StartDate);
        await using var provider1 = new FileLoggerProvider(tempDirectory.FullPath, timeProvider);
        await using var provider2 = new FileLoggerProvider(tempDirectory.FullPath, timeProvider);

        provider1.CreateLogger("Test").LogInformation("From first provider");
        provider2.CreateLogger("Test").LogInformation("From second provider");
        await provider1.FlushAsync(TestContext.Current.CancellationToken);
        await provider2.FlushAsync(TestContext.Current.CancellationToken);

        Assert.NotEqual(provider1.LogFilePath, provider2.LogFilePath);
        Assert.Contains("From first provider", await ReadLogFileAsync(provider1.LogFilePath));
        Assert.Contains("From second provider", await ReadLogFileAsync(provider2.LogFilePath));
    }

    [Fact]
    public async Task AppendToExistingFile()
    {
        using var tempDirectory = TemporaryDirectory.Create();
        var timeProvider = new FakeTimeProvider(StartDate);
        var options = new FileLoggerOptions { Directory = tempDirectory.FullPath, Append = true, RollInterval = RollInterval.Daily };

        await using (var provider = new FileLoggerProvider(options, timeProvider))
        {
            provider.CreateLogger("Test").LogInformation("First run");
        }

        await using (var provider = new FileLoggerProvider(options, timeProvider))
        {
            provider.CreateLogger("Test").LogInformation("Second run");
        }

        var files = Directory.GetFiles(tempDirectory.FullPath);
        var file = Assert.Single(files);
        var content = await ReadLogFileAsync(file);
        Assert.Contains("First run", content);
        Assert.Contains("Second run", content);
    }

    [Fact]
    public async Task AppendDoesNotReuseAFullFile()
    {
        using var tempDirectory = TemporaryDirectory.Create();
        var timeProvider = new FakeTimeProvider(StartDate);
        var options = new FileLoggerOptions
        {
            Directory = tempDirectory.FullPath,
            Append = true,
            RollInterval = RollInterval.Daily,
            MaxFileSizeInBytes = 100,
            TimestampFormat = null,
            IncludeCategory = false,
            IncludeLogLevel = false,
        };

        for (var i = 0; i < 3; i++)
        {
            await using var provider = new FileLoggerProvider(options, timeProvider);
            provider.CreateLogger("Test").LogInformation("Message .............................................................................");
        }

        var pid = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
        Assert.Equal([$"2024-01-02-{pid}.log", $"2024-01-02-{pid}_001.log", $"2024-01-02-{pid}_002.log"], GetFileNames(tempDirectory));
    }

    [Fact]
    public async Task FlushAsyncWritesPendingMessages()
    {
        using var tempDirectory = TemporaryDirectory.Create();
        await using var provider = new FileLoggerProvider(new FileLoggerOptions { Directory = tempDirectory.FullPath, FlushInterval = TimeSpan.FromHours(1) });
        provider.CreateLogger("Test").LogInformation("Hello");

        await provider.FlushAsync(TestContext.Current.CancellationToken);
        Assert.Contains("Hello", await ReadLogFileAsync(provider.LogFilePath));
    }

    [Fact]
    public async Task WaitQueueFullModeDoesNotDropMessages()
    {
        using var tempDirectory = TemporaryDirectory.Create();
        await using var provider = new FileLoggerProvider(new FileLoggerOptions
        {
            Directory = tempDirectory.FullPath,
            MaxQueueLength = 1,
            QueueFullMode = FileLoggerQueueFullMode.Wait,
            TimestampFormat = null,
            IncludeLogLevel = false,
            IncludeCategory = false,
        });

        const int ThreadCount = 8;
        const int MessagesPerThread = 500;

        var logger = provider.CreateLogger("Test");

        // Dedicated threads, so the producers cannot starve the thread pool the writer runs on
        await Task.WhenAll(Enumerable.Range(0, ThreadCount).Select(_ => Task.Factory.StartNew(
            () =>
            {
                for (var i = 0; i < MessagesPerThread; i++)
                {
                    logger.LogInformation("Message");
                }
            }, TestContext.Current.CancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default)));

        await provider.DisposeAsync();

        var lines = await File.ReadAllLinesAsync(provider.LogFilePath!, TestContext.Current.CancellationToken);
        Assert.HasCount(ThreadCount * MessagesPerThread, lines);
    }

    [Fact]
    public async Task DropWriteDoesNotBlock()
    {
        using var tempDirectory = TemporaryDirectory.Create();
        await using var provider = new FileLoggerProvider(new FileLoggerOptions
        {
            Directory = tempDirectory.FullPath,
            MaxQueueLength = 1,
            QueueFullMode = FileLoggerQueueFullMode.DropWrite,
        });

        var logger = provider.CreateLogger("Test");
        for (var i = 0; i < 10_000; i++)
        {
            logger.LogInformation("Message {Index}", i);
        }

        await provider.FlushAsync(TestContext.Current.CancellationToken);
        Assert.True(File.Exists(provider.LogFilePath));
    }

    [Fact]
    public async Task DisposeWritesAllPendingMessages()
    {
        using var tempDirectory = TemporaryDirectory.Create();
        var provider = new FileLoggerProvider(new FileLoggerOptions { Directory = tempDirectory.FullPath, FlushInterval = TimeSpan.FromHours(1) });
        var logger = provider.CreateLogger("Test");
        for (var i = 0; i < 1000; i++)
        {
            logger.LogInformation("Message {Index}", i);
        }

        await provider.DisposeAsync();
        await provider.DisposeAsync();

        var lines = await File.ReadAllLinesAsync(provider.LogFilePath!, TestContext.Current.CancellationToken);
        Assert.HasCount(1000, lines);
    }

    [Fact]
    public void MissingDirectoryThrows()
    {
        Assert.Throws<InvalidOperationException>(() => new FileLoggerProvider(new FileLoggerOptions()));
    }

    [Fact]
    public async Task AddFileRegistersTheProvider()
    {
        using var tempDirectory = TemporaryDirectory.Create();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Trace).AddFile(tempDirectory.FullPath, options => options.UseShortCategoryName = true));

        await using (var serviceProvider = services.BuildServiceProvider())
        {
            var logger = serviceProvider.GetRequiredService<ILogger<FileLoggerProviderTests>>();
            using (logger.BeginScope("Scope"))
            {
                logger.LogInformation("Hello from DI");
            }
        }

        var file = Assert.Single(Directory.GetFiles(tempDirectory.FullPath));
        var content = await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken);
        Assert.Contains("[FileLoggerProviderTests] Hello from DI", content);
    }

    [Fact]
    public async Task AddFileReadsTheConfiguration()
    {
        using var tempDirectory = TemporaryDirectory.Create();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Logging:File:Directory"] = tempDirectory.FullPath,
                ["Logging:File:IncludeEventId"] = "true",
                ["Logging:File:LogLevel:Default"] = "Warning",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.AddConfiguration(configuration.GetSection("Logging"));
            builder.AddFile();
        });

        await using (var serviceProvider = services.BuildServiceProvider())
        {
            var logger = serviceProvider.GetRequiredService<ILogger<FileLoggerProviderTests>>();
            logger.LogInformation("Filtered out by the configuration");
            logger.LogWarning(new EventId(42), "Kept by the configuration");
        }

        var file = Assert.Single(Directory.GetFiles(tempDirectory.FullPath));
        var content = await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken);
        Assert.DoesNotContain("Filtered out", content);
        Assert.Contains("[EventId:42] Kept by the configuration", content);
    }

    [Fact]
    public async Task InvalidDirectoryDoesNotThrow()
    {
        using var tempDirectory = TemporaryDirectory.Create();
        var filePath = tempDirectory.CreateEmptyFile("file.txt");

        // The directory cannot be created because a file with the same name already exists
        await using var provider = new FileLoggerProvider(filePath.Value);
        provider.CreateLogger("Test").LogInformation("Hello");
        await provider.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Null(provider.LogFilePath);
    }

    private static string[] GetFileNames(TemporaryDirectory directory)
    {
        return new DirectoryInfo(directory.FullPath).GetFiles().Select(file => file.Name).Order(StringComparer.Ordinal).ToArray();
    }

    private static async Task<string> ReadCompressedFileAsync(string path, LogFileCompression compression)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        await using Stream decompressedStream = compression switch
        {
            LogFileCompression.GZip => new GZipStream(stream, CompressionMode.Decompress),
            LogFileCompression.Brotli => new BrotliStream(stream, CompressionMode.Decompress),
#if NET11_0_OR_GREATER
            LogFileCompression.Zstandard => new ZstandardStream(stream, CompressionMode.Decompress),
#endif
            _ => throw new ArgumentOutOfRangeException(nameof(compression)),
        };

        using var reader = new StreamReader(decompressedStream);
        return await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<string> ReadLogFileAsync(string? path)
    {
        Assert.NotNull(path);

        // The file is still opened by the provider
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
    }

    private sealed class UppercaseFileFormatter() : FileFormatter("uppercase")
    {
        public override void Write<TState>(in LogEntry<TState> logEntry, DateTimeOffset timestamp, FileLoggerOptions options, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
        {
            textWriter.Write(logEntry.Formatter(logEntry.State, logEntry.Exception).ToUpperInvariant());
        }
    }
}
