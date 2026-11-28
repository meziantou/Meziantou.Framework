namespace Meziantou.Framework.WasmEngine.Wasi;

/// <summary>
/// An error of a WASI operation: the function returns <see cref="Errno"/> to the module. A <see cref="WasiFileSystem"/>
/// throws it to report an error with a precise code.
/// </summary>
public sealed class WasiException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="WasiException"/> class, whose code is <see cref="WasiErrno.IOError"/>.</summary>
    public WasiException()
        : this(WasiErrno.IOError)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="WasiException"/> class, whose code is <see cref="WasiErrno.IOError"/>.</summary>
    /// <param name="message">The message of the exception.</param>
    public WasiException(string message)
        : this(WasiErrno.IOError, message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="WasiException"/> class, whose code is <see cref="WasiErrno.IOError"/>.</summary>
    /// <param name="message">The message of the exception.</param>
    /// <param name="innerException">The exception that caused the error.</param>
    public WasiException(string message, Exception? innerException)
        : base(message, innerException)
    {
        Errno = WasiErrno.IOError;
    }

    /// <summary>Initializes a new instance of the <see cref="WasiException"/> class.</summary>
    /// <param name="errno">The code returned to the module.</param>
    public WasiException(WasiErrno errno)
        : this(errno, "The WASI operation failed with the error " + errno.ToString())
    {
    }

    /// <summary>Initializes a new instance of the <see cref="WasiException"/> class.</summary>
    /// <param name="errno">The code returned to the module.</param>
    /// <param name="message">The message of the exception.</param>
    public WasiException(WasiErrno errno, string message)
        : base(message)
    {
        Errno = errno;
    }

    /// <summary>Gets the code returned to the module.</summary>
    public WasiErrno Errno { get; }
}

/// <summary>
/// Thrown by the <c>proc_exit</c> function of WASI: it ends the execution of the module, and is thrown by the call into
/// WebAssembly. <see cref="WasiContext.Run"/> catches it and returns <see cref="ExitCode"/>.
/// </summary>
public sealed class WasiExitException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="WasiExitException"/> class whose exit code is 0.</summary>
    public WasiExitException()
        : this(0)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="WasiExitException"/> class whose exit code is 1.</summary>
    /// <param name="message">The message of the exception.</param>
    public WasiExitException(string message)
        : base(message)
    {
        ExitCode = 1;
    }

    /// <summary>Initializes a new instance of the <see cref="WasiExitException"/> class whose exit code is 1.</summary>
    /// <param name="message">The message of the exception.</param>
    /// <param name="innerException">The exception that caused the exit.</param>
    public WasiExitException(string message, Exception? innerException)
        : base(message, innerException)
    {
        ExitCode = 1;
    }

    /// <summary>Initializes a new instance of the <see cref="WasiExitException"/> class.</summary>
    /// <param name="exitCode">The exit code of the module.</param>
    public WasiExitException(int exitCode)
        : base("The WebAssembly module exited with the code " + exitCode.ToString(System.Globalization.CultureInfo.InvariantCulture))
    {
        ExitCode = exitCode;
    }

    /// <summary>Gets the exit code of the module.</summary>
    public int ExitCode { get; }
}
