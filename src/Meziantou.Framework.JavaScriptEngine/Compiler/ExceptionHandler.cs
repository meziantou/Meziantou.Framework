namespace Meziantou.Framework.JavaScriptEngine.Compiler;

/// <summary>
/// A try region of the bytecode: an exception thrown between <see cref="Start"/> and <see cref="End"/> jumps to <see cref="Target"/>.
/// <see cref="Rethrows"/> marks a handler of the compiler that rethrows the exception after cleaning up (it closes the iterator
/// of a for-of loop or of a destructuring): the exception keeps the location where it was thrown.
/// </summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
internal readonly record struct ExceptionHandler(int Start, int End, int Target, int StackDepth, int EnvironmentRegister, bool Rethrows = false);
