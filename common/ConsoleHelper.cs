#nullable enable
#pragma warning disable CA1001
#pragma warning disable CA2213
#pragma warning disable CA2215
using System.CommandLine;
using System.Diagnostics;

namespace Meziantou.Framework;

internal sealed class ConsoleHelper
{
    private readonly StringWriter _outputWriter = new();
    private readonly StringWriter _errorWriter = new();

    private readonly TextWriter _teeOutputWriter;
    private readonly TextWriter _teeErrorWriter;

    public ConsoleHelper(ITestOutputHelper testOutputHelper)
    {
        _teeOutputWriter = CreateBroadcasting(new XunitTextWriter(testOutputHelper), _outputWriter);
        _teeErrorWriter = CreateBroadcasting(new XunitTextWriter(testOutputHelper), _errorWriter);
    }

    public string Output => _outputWriter.ToString();
    public string Error => _errorWriter.ToString();

    public void ConfigureConsole(InvocationConfiguration configuration)
    {
        configuration.Output = _teeOutputWriter;
        configuration.Error = _teeErrorWriter;
    }

    private static TextWriter CreateBroadcasting(params TextWriter[] writers)
    {
        ArgumentNullException.ThrowIfNull(writers);

        return writers.Length != 0 ?
            new BroadcastingTextWriter([.. writers]) :
            TextWriter.Null;
    }

    private sealed class XunitTextWriter : TextWriter
    {
        private readonly ITestOutputHelper _testOutputHelper;
        public XunitTextWriter(ITestOutputHelper testOutputHelper)
        {
            _testOutputHelper = testOutputHelper;
        }
        public override Encoding Encoding => Encoding.UTF8;
        public override void WriteLine(string? value)
        {
            if (value is not null)
            {
                _testOutputHelper.WriteLine(value);
            }
        }
        public override void Write(string? value)
        {
            if (value is not null)
            {
                _testOutputHelper.Write(value);
            }
        }
    }

    private sealed class BroadcastingTextWriter : TextWriter
    {
        private readonly TextWriter[] _writers;

        public BroadcastingTextWriter(TextWriter[] writers)
        {
            Debug.Assert(writers is { Length: > 0 });
            foreach (var writer in writers)
            {
                ArgumentNullException.ThrowIfNull(writer, nameof(writers));
            }

            _writers = writers;
        }

        public override Encoding Encoding => _writers[0].Encoding;

        public override IFormatProvider FormatProvider => _writers[0].FormatProvider;

        [AllowNull]
        public override string NewLine
        {
            get => base.NewLine;
            set
            {
                base.NewLine = value;
                foreach (var writer in _writers)
                {
                    writer.NewLine = value;
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var writer in _writers)
                {
                    writer.Dispose();
                }
            }
        }

        public override async ValueTask DisposeAsync()
        {
            foreach (var writer in _writers)
            {
                await writer.DisposeAsync().ConfigureAwait(false);
            }
        }

        public override void Flush()
        {
            foreach (var writer in _writers)
            {
                writer.Flush();
            }
        }

        public override async Task FlushAsync()
        {
            foreach (var writer in _writers)
            {
                await writer.FlushAsync().ConfigureAwait(false);
            }
        }

        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            foreach (var writer in _writers)
            {
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        public override void Write(bool value)
        {
            foreach (var writer in _writers)
            {
                writer.Write(value);
            }
        }

        public override void Write(char value)
        {
            foreach (var writer in _writers)
            {
                writer.Write(value);
            }
        }

        public override void Write(char[] buffer, int index, int count)
        {
            foreach (var writer in _writers)
            {
                writer.Write(buffer, index, count);
            }
        }

        public override void Write(char[]? buffer)
        {
            foreach (var writer in _writers)
            {
                writer.Write(buffer);
            }
        }

        public override void Write(decimal value)
        {
            foreach (var writer in _writers)
            {
                writer.Write(value);
            }
        }

        public override void Write(double value)
        {
            foreach (var writer in _writers)
            {
                writer.Write(value);
            }
        }

        public override void Write(int value)
        {
            foreach (var writer in _writers)
            {
                writer.Write(value);
            }
        }

        public override void Write(long value)
        {
            foreach (var writer in _writers)
            {
                writer.Write(value);
            }
        }

        public override void Write(ReadOnlySpan<char> buffer)
        {
            foreach (var writer in _writers)
            {
                writer.Write(buffer);
            }
        }

        public override void Write(uint value)
        {
            foreach (var writer in _writers)
            {
                writer.Write(value);
            }
        }

        public override void Write(ulong value)
        {
            foreach (var writer in _writers)
            {
                writer.Write(value);
            }
        }

        public override void Write(float value)
        {
            foreach (var writer in _writers)
            {
                writer.Write(value);
            }
        }

        public override void Write(string? value)
        {
            foreach (var writer in _writers)
            {
                writer.Write(value);
            }
        }

        public override void Write(object? value)
        {
            foreach (var writer in _writers)
            {
                writer.Write(value);
            }
        }

        public override void Write(StringBuilder? value)
        {
            foreach (var writer in _writers)
            {
                writer.Write(value);
            }
        }

        public override void Write([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, object? arg0)
        {
            foreach (var writer in _writers)
            {
                writer.Write(format, arg0);
            }
        }

        public override void Write([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, object? arg0, object? arg1)
        {
            foreach (var writer in _writers)
            {
                writer.Write(format, arg0, arg1);
            }
        }

        public override void Write([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, object? arg0, object? arg1, object? arg2)
        {
            foreach (var writer in _writers)
            {
                writer.Write(format, arg0, arg1, arg2);
            }
        }

        public override void Write([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, params object?[] arg)
        {
            foreach (var writer in _writers)
            {
                writer.Write(format, arg);
            }
        }

#if NET9_0_OR_GREATER
        public override void Write([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, params ReadOnlySpan<object?> arg)
        {
            foreach (var writer in _writers)
            {
                writer.Write(format, arg);
            }
        }
#endif

        public override void WriteLine()
        {
            foreach (var writer in _writers)
            {
                writer.WriteLine();
            }
        }

        public override void WriteLine(char value)
        {
            foreach (var writer in _writers)
            {
                writer.WriteLine(value);
            }
        }

        public override void WriteLine(char[]? buffer)
        {
            foreach (var writer in _writers)
            {
                writer.WriteLine(buffer);
            }
        }

        public override void WriteLine(char[] buffer, int index, int count)
        {
            foreach (var writer in _writers)
            {
                writer.WriteLine(buffer, index, count);
            }
        }

        public override void WriteLine(ReadOnlySpan<char> buffer)
        {
            foreach (var writer in _writers)
            {
                writer.WriteLine(buffer);
            }
        }

        public override void WriteLine(bool value)
        {
            foreach (var writer in _writers)
            {
                writer.WriteLine(value);
            }
        }

        public override void WriteLine(int value)
        {
            foreach (var writer in _writers)
            {
                writer.WriteLine(value);
            }
        }

        public override void WriteLine(uint value)
        {
            foreach (var writer in _writers)
            {
                writer.WriteLine(value);
            }
        }

        public override void WriteLine(long value)
        {
            foreach (var writer in _writers)
            {
                writer.WriteLine(value);
            }
        }

        public override void WriteLine(ulong value)
        {
            foreach (var writer in _writers)
            {
                writer.WriteLine(value);
            }
        }

        public override void WriteLine(float value)
        {
            foreach (var writer in _writers)
            {
                writer.WriteLine(value);
            }
        }

        public override void WriteLine(double value)
        {
            foreach (var writer in _writers)
            {
                writer.WriteLine(value);
            }
        }

        public override void WriteLine(decimal value)
        {
            foreach (var writer in _writers)
            {
                writer.WriteLine(value);
            }
        }

        public override void WriteLine(string? value)
        {
            foreach (var writer in _writers)
            {
                writer.WriteLine(value);
            }
        }

        public override void WriteLine(StringBuilder? value)
        {
            foreach (var writer in _writers)
            {
                writer.WriteLine(value);
            }
        }

        public override void WriteLine(object? value)
        {
            foreach (var writer in _writers)
            {
                writer.WriteLine(value);
            }
        }

        public override void WriteLine([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, object? arg0)
        {
            foreach (var writer in _writers)
            {
                writer.WriteLine(format, arg0);
            }
        }

        public override void WriteLine([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, object? arg0, object? arg1)
        {
            foreach (var writer in _writers)
            {
                writer.WriteLine(format, arg0, arg1);
            }
        }

        public override void WriteLine([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, object? arg0, object? arg1, object? arg2)
        {
            foreach (var writer in _writers)
            {
                writer.WriteLine(format, arg0, arg1, arg2);
            }
        }

        public override void WriteLine([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, params object?[] arg)
        {
            foreach (var writer in _writers)
            {
                writer.WriteLine(format, arg);
            }
        }

#if NET9_0_OR_GREATER
        public override void WriteLine([StringSyntax(StringSyntaxAttribute.CompositeFormat)] string format, params ReadOnlySpan<object?> arg)
        {
            foreach (var writer in _writers)
            {
                writer.WriteLine(format, arg);
            }
        }
#endif
        public override async Task WriteAsync(char value)
        {
            foreach (var writer in _writers)
            {
                await writer.WriteAsync(value).ConfigureAwait(false);
            }
        }

        public override async Task WriteAsync(string? value)
        {
            foreach (var writer in _writers)
            {
                await writer.WriteAsync(value).ConfigureAwait(false);
            }
        }

        public override async Task WriteAsync(StringBuilder? value, CancellationToken cancellationToken = default)
        {
            foreach (var writer in _writers)
            {
                await writer.WriteAsync(value, cancellationToken).ConfigureAwait(false);
            }
        }

        public override async Task WriteAsync(char[] buffer, int index, int count)
        {
            foreach (var writer in _writers)
            {
                await writer.WriteAsync(buffer, index, count).ConfigureAwait(false);
            }
        }

        public override async Task WriteAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
        {
            foreach (var writer in _writers)
            {
                await writer.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
            }
        }

        public override async Task WriteLineAsync(char value)
        {
            foreach (var writer in _writers)
            {
                await writer.WriteLineAsync(value).ConfigureAwait(false);
            }
        }

        public override async Task WriteLineAsync(string? value)
        {
            foreach (var writer in _writers)
            {
                await writer.WriteLineAsync(value).ConfigureAwait(false);
            }
        }

        public override async Task WriteLineAsync(StringBuilder? value, CancellationToken cancellationToken = default)
        {
            foreach (var writer in _writers)
            {
                await writer.WriteLineAsync(value, cancellationToken).ConfigureAwait(false);
            }
        }

        public override async Task WriteLineAsync(char[] buffer, int index, int count)
        {
            foreach (var writer in _writers)
            {
                await writer.WriteLineAsync(buffer, index, count).ConfigureAwait(false);
            }
        }

        public override async Task WriteLineAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
        {
            foreach (var writer in _writers)
            {
                await writer.WriteLineAsync(buffer, cancellationToken).ConfigureAwait(false);
            }
        }

        public override async Task WriteLineAsync()
        {
            foreach (var writer in _writers)
            {
                await writer.WriteLineAsync().ConfigureAwait(false);
            }
        }
    }
}