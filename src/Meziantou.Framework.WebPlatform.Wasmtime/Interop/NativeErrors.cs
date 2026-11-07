using System.Text;

namespace Meziantou.Framework.WebPlatform.Wasmtime.Interop;

/// <summary>Reads and creates the errors and the traps of Wasmtime.</summary>
internal static unsafe class NativeErrors
{
    /// <summary>Returns the message of an error and deletes the error.</summary>
    public static string TakeErrorMessage(nint error)
    {
        WasmByteVec message;
        NativeMethods.wasmtime_error_message(error, &message);
        var result = Encoding.UTF8.GetString(message.AsSpan());
        NativeMethods.wasm_byte_vec_delete(&message);
        NativeMethods.wasmtime_error_delete(error);
        return result;
    }

    /// <summary>Returns the message of a trap (the message ends with a NUL character).</summary>
    public static string GetTrapMessage(nint trap)
    {
        WasmByteVec message;
        NativeMethods.wasm_trap_message(trap, &message);
        var bytes = message.AsSpan();
        if (!bytes.IsEmpty && bytes[^1] == 0)
        {
            bytes = bytes[..^1];
        }

        var result = Encoding.UTF8.GetString(bytes);
        NativeMethods.wasm_byte_vec_delete(&message);
        return result;
    }

    /// <summary>Creates an error (the error returned by a callback is owned by Wasmtime).</summary>
    public static nint CreateError(string message)
    {
        var bytes = Encoding.UTF8.GetBytes(message + "\0");
        fixed (byte* data = bytes)
        {
            return NativeMethods.wasmtime_error_new(data);
        }
    }

    /// <summary>Creates a trap (the trap returned by a callback is owned by Wasmtime).</summary>
    public static nint CreateTrap(string message)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        fixed (byte* data = bytes)
        {
            return NativeMethods.wasmtime_trap_new(data, (nuint)bytes.Length);
        }
    }
}
