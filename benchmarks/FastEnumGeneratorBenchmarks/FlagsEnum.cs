namespace FastEnumGeneratorBenchmarks;

[Flags]
public enum FlagsEnum
{
    None = 0,
    Read = 1,
    Write = 2,
    Execute = 4,
    Delete = 8,
    ReadWrite = Read | Write,
}
