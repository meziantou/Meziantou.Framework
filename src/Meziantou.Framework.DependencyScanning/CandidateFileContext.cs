using System;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.DependencyScanning;

[StructLayout(LayoutKind.Auto)]
public readonly ref struct CandidateFileContext
{
    public CandidateFileContext(ReadOnlySpan<char> directory, ReadOnlySpan<char> fileName)
    {
        Directory = directory;
        FileName = fileName;
    }

    public ReadOnlySpan<char> Directory { get; }
    public ReadOnlySpan<char> FileName { get; }
}
