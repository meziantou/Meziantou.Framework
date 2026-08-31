using System.IO.Compression;

namespace Meziantou.Extensions.Logging;

/// <summary>Writes the log messages to a file and handles the log file rolling and retention.</summary>
internal sealed class LogFileWriter : IDisposable
{
    private const int MaxFileNameAttempts = 1000;

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly FileLoggerOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly string _directory;
    private readonly int _newLineByteCount;

    // When the messages are compressed as they are written, the compression extension is part of the file name
    private readonly bool _compressWhileWriting;
    private readonly string _fileNameSuffix;

    private FileStream _fileStream;
    private StreamWriter _writer;
    private bool _canAppend;
    private string? _currentFilePath;
    private long _currentFileSize;
    private DateTimeOffset _currentPeriod;

    /// <summary>Gets the path of the file the messages are currently written to.</summary>
    public string? CurrentFilePath => Volatile.Read(ref _currentFilePath);

    public LogFileWriter(string directory, FileLoggerOptions options, TimeProvider timeProvider)
    {
        _directory = directory;
        _options = options;
        _timeProvider = timeProvider;
        _newLineByteCount = Environment.NewLine.Length;
        _compressWhileWriting = options.Compression is not LogFileCompression.None && options.CompressionMode is LogFileCompressionMode.Continuous;
        _fileNameSuffix = _compressWhileWriting ? GetCompressionExtension(options.Compression) : "";

        // Appending to a compressed file would create a file that most tools cannot read entirely
        _canAppend = options.Append && !_compressWhileWriting;
        Open(timeProvider.GetUtcNow());
    }

    public void WriteLine(string message)
    {
        var byteCount = Utf8NoBom.GetByteCount(message) + _newLineByteCount;
        RollIfNeeded(_timeProvider.GetUtcNow(), byteCount);
        _writer.WriteLine(message);

        // When the messages are compressed, this is an upper bound of the size of the file. The exact size is known when the data is flushed
        _currentFileSize += byteCount;
    }

    public void Flush()
    {
        _writer.Flush();
        _currentFileSize = _fileStream.Position;
    }

    public void Dispose()
    {
        // Keep CurrentFilePath, so the path of the last log file can be read after the provider is disposed.
        // Disposing the writer also finalizes the compressed stream and disposes the file stream
        _writer.Dispose();
        _fileStream.Dispose();
    }

    private void RollIfNeeded(DateTimeOffset now, int byteCount)
    {
        var rollPeriod = _options.RollInterval is not RollInterval.None && Truncate(now) != _currentPeriod;

        // Never roll on an empty file, otherwise a message bigger than the limit would roll indefinitely
        var rollSize = _options.MaxFileSizeInBytes is { } maxSize && _currentFileSize > 0 && (_currentFileSize + byteCount) > maxSize;
        if (!rollPeriod && !rollSize)
            return;

        var previousFilePath = _currentFilePath;
        _writer.Flush();
        _writer.Dispose();
        Open(now);

        if (_options.Compression is not LogFileCompression.None && _options.CompressionMode is LogFileCompressionMode.OnRoll && previousFilePath is not null)
        {
            Compress(previousFilePath);
        }

        ApplyRetentionPolicy();
    }

    [MemberNotNull(nameof(_writer), nameof(_fileStream))]
    private void Open(DateTimeOffset now)
    {
        Directory.CreateDirectory(_directory);

        IOException? lastException = null;
        for (var index = 0; index < MaxFileNameAttempts; index++)
        {
            var path = Path.Combine(_directory, GetFileName(now, index));

            // Only the first file can be appended to, the next ones are created by a roll and must be new
            var append = _canAppend;
            if (append && _options.MaxFileSizeInBytes is { } maxSize && File.Exists(path) && new FileInfo(path).Length >= maxSize)
                continue;

            FileStream? stream = null;
            try
            {
                // Opening the file with FileShare.Read allows other processes to read the log file while it is being written,
                // and prevents 2 providers from writing to the same file
                stream = new FileStream(path, CreateStreamOptions(append ? FileMode.Append : FileMode.CreateNew, FileShare.Read | FileShare.Delete));
                _writer = new StreamWriter(_compressWhileWriting ? CreateCompressionStream(stream) : stream, Utf8NoBom) { AutoFlush = false };
                _fileStream = stream;
                _currentFileSize = stream.Length;
                _currentPeriod = Truncate(now);
                _canAppend = false;
                Volatile.Write(ref _currentFilePath, path);
                return;
            }
            catch (IOException ex)
            {
                // The file already exists or is used by another process, try the next name
                stream?.Dispose();
                lastException = ex;
            }
        }

        throw lastException ?? new IOException("Cannot create a log file in " + _directory);
    }

    private FileStreamOptions CreateStreamOptions(FileMode mode, FileShare share)
    {
        var streamOptions = new FileStreamOptions
        {
            Mode = mode,
            Access = FileAccess.Write,
            Share = share,
        };

        // Setting UnixCreateMode throws on Windows, where the value has no meaning
        if (!OperatingSystem.IsWindows() && _options.UnixCreateMode is { } unixCreateMode)
        {
            streamOptions.UnixCreateMode = unixCreateMode;
        }

        return streamOptions;
    }

    private Stream CreateCompressionStream(Stream stream) => _options.Compression switch
    {
        LogFileCompression.GZip => new GZipStream(stream, _options.CompressionLevel),
        LogFileCompression.Brotli => new BrotliStream(stream, _options.CompressionLevel),
#if NET11_0_OR_GREATER
        LogFileCompression.Zstandard => new ZstandardStream(stream, _options.CompressionLevel),
#endif
        _ => stream,
    };

    private static string GetCompressionExtension(LogFileCompression compression) => compression switch
    {
        LogFileCompression.GZip => ".gz",
        LogFileCompression.Brotli => ".br",
#if NET11_0_OR_GREATER
        LogFileCompression.Zstandard => ".zst",
#endif
        _ => "",
    };

    private string GetFileName(DateTimeOffset now, int index)
    {
        var builder = new StringBuilder();
        builder.Append(_options.FileNamePrefix);

        // Timestamp first so the files sort chronologically by name
        builder.Append(now.ToString(GetTimestampFormat(_options.RollInterval), CultureInfo.InvariantCulture));

        if (_options.IncludeProcessIdInFileName)
        {
            builder.Append('-').Append(Environment.ProcessId);
        }

        if (index > 0)
        {
            // '_' sorts after '.', so the file names of a same period are ordered chronologically
            builder.Append('_').Append(index.ToString("000", CultureInfo.InvariantCulture));
        }

        builder.Append(_options.FileNameExtension);
        builder.Append(_fileNameSuffix);
        return builder.ToString();
    }

    private static string GetTimestampFormat(RollInterval rollInterval) => rollInterval switch
    {
        RollInterval.Hourly => "yyyy-MM-dd-HH",
        RollInterval.Daily => "yyyy-MM-dd",
        RollInterval.Monthly => "yyyy-MM",
        _ => "yyyy-MM-dd-HH-mm-ss",
    };

    private DateTimeOffset Truncate(DateTimeOffset value)
    {
        var utc = value.UtcDateTime;
        return _options.RollInterval switch
        {
            RollInterval.Hourly => new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, minute: 0, second: 0, TimeSpan.Zero),
            RollInterval.Daily => new DateTimeOffset(utc.Year, utc.Month, utc.Day, hour: 0, minute: 0, second: 0, TimeSpan.Zero),
            RollInterval.Monthly => new DateTimeOffset(utc.Year, utc.Month, day: 1, hour: 0, minute: 0, second: 0, TimeSpan.Zero),
            _ => DateTimeOffset.MinValue,
        };
    }

    private void Compress(string path)
    {
        try
        {
            using (var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            using (var destination = new FileStream(path + GetCompressionExtension(_options.Compression), CreateStreamOptions(FileMode.Create, FileShare.None)))
            using (var compressedStream = CreateCompressionStream(destination))
            {
                source.CopyTo(compressedStream);
            }

            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Keep the uncompressed file when it cannot be compressed
        }
    }

    private void ApplyRetentionPolicy()
    {
        if (_options.MaxRetainedFiles is not { } maxRetainedFiles)
            return;

        try
        {
            var directory = new DirectoryInfo(_directory);
            var pattern = _options.FileNamePrefix + "*" + _options.FileNameExtension;

            // The file names start with the timestamp, so ordering them by name orders them chronologically.
            // The second pattern matches the compressed files, whatever the algorithm they were compressed with
            var files = directory.GetFiles(pattern)
                .Concat(directory.GetFiles(pattern + ".*"))
                .DistinctBy(file => file.FullName, StringComparer.Ordinal)
                .OrderByDescending(file => file.Name, StringComparer.Ordinal)
                .Skip(maxRetainedFiles);

            foreach (var file in files)
            {
                if (string.Equals(file.FullName, _currentFilePath, StringComparison.Ordinal))
                    continue;

                try
                {
                    file.Delete();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // The file is in use by another process
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
        }
    }
}
