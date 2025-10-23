using Xunit.Abstractions;

namespace Meziantou.Extensions.Logging.Xunit.Tests;

internal sealed class InMemoryTestOutputHelper : ITestOutputHelper
{
    private readonly List<string> _logs = new();

    public IEnumerable<string> Logs => _logs;

    public void WriteLine(string message)
    {
        lock (_logs)
        {
            _logs.Add(message);
        }
    }

    public void WriteLine(string format, params object[] args)
    {
        lock (_logs)
        {
            _logs.Add(string.Format(CultureInfo.InvariantCulture, format, args));
        }
    }
}
