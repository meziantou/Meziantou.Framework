namespace Meziantou.Framework.Fuzzing;

/// <summary>Runs the code under test on one input. An exception that escapes it is a failure (libFuzzer saves the input as a crash).</summary>
/// <param name="data">The input.</param>
public delegate void FuzzTargetAction(ReadOnlySpan<byte> data);
