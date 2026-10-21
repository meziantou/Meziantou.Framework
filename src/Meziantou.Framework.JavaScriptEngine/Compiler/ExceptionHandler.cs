namespace Meziantou.Framework.JavaScriptEngine.Compiler;

/// <summary>A try region of the bytecode: an exception thrown between <see cref="Start"/> and <see cref="End"/> jumps to <see cref="Target"/>.</summary>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
internal readonly record struct ExceptionHandler(int Start, int End, int Target, int StackDepth, int EnvironmentRegister);
