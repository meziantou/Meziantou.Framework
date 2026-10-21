using Meziantou.Framework.Diagnostics.Samples;

// The examples are compiled in CI to keep the documentation in sync with the API.
// Running this program writes dumps of its own process in the current directory.
if (OperatingSystem.IsWindowsVersionAtLeast(8, 1) || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
{
    await MemoryDumpExamples.WriteDumps();
}
