# Meziantou.Extensions.Logging.FileLogger

```c#
using Microsoft.Extensions.Logging;

var logsDirectory = Path.Combine(Path.GetTempPath(), "logs");
using var provider = new FileLoggerProvider(logsDirectory);

using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(provider));
var logger = loggerFactory.CreateLogger("Sample");

logger.LogInformation("Hello from file logger");
Console.WriteLine($"Log file: {provider.LogFilePath}");
```
