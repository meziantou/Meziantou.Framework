namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>
/// The base64 and hex methods of Uint8Array (https://tc39.es/ecma262/#sec-uint8array-objects, ES2026):
/// Uint8Array.fromBase64, Uint8Array.fromHex, Uint8Array.prototype.toBase64, toHex, setFromBase64 and setFromHex.
/// </summary>
internal static class Uint8ArrayBase64Builtins
{
    private static readonly PropertyKey AlphabetKey = new("alphabet");
    private static readonly PropertyKey LastChunkHandlingKey = new("lastChunkHandling");
    private static readonly PropertyKey OmitPaddingKey = new("omitPadding");
    private static readonly PropertyKey ReadKey = new("read");
    private static readonly PropertyKey WrittenKey = new("written");

    private enum LastChunkHandling
    {
        Loose,
        Strict,
        StopBeforePartial,
    }

    private static readonly BuiltinMethodTable ConstructorMethods =
    [
        new("fromBase64", 1, FromBase64),
        new("fromHex", 1, FromHex),
    ];

    private static readonly BuiltinMethodTable PrototypeMethods =
    [
        new("setFromBase64", 1, SetFromBase64),
        new("setFromHex", 1, SetFromHex),
        new("toBase64", 0, ToBase64),
        new("toHex", 0, ToHex),
    ];

    public static void Initialize(Intrinsics intrinsics)
    {
        var realm = intrinsics.Realm;
        var constructor = intrinsics.GetTypedArrayConstructor(TypedArrayElementType.Uint8);
        var prototype = intrinsics.GetTypedArrayPrototype(TypedArrayElementType.Uint8);
        realm.DefineMethods(constructor, ConstructorMethods);
        realm.DefineMethods(prototype, PrototypeMethods);
    }

    private static JsValue Arg(ReadOnlySpan<JsValue> arguments, int index) => arguments.Length > index ? arguments[index] : default;

    /// <summary>ValidateUint8Array(ta, accessMode): <paramref name="write"/> is the write access mode (a TypeError when the buffer is immutable).</summary>
    private static JsTypedArray ValidateUint8Array(JsValue value, string method, bool write = false)
    {
        if (value.Cell is not JsTypedArray { ElementType: TypedArrayElementType.Uint8 } typedArray)
            return ThrowHelper.ThrowTypeError<JsTypedArray>($"Method Uint8Array.prototype.{method} called on incompatible receiver {JsOperations.DescribeForError(value)}");

        if (write && typedArray.ViewedArrayBuffer.IsImmutable)
            return ThrowHelper.ThrowTypeError<JsTypedArray>($"Cannot perform Uint8Array.prototype.{method} on a Uint8Array backed by an immutable ArrayBuffer");

        return typedArray;
    }

    private static string RequireString(JsValue value)
    {
        if (value.Cell is not JsString str)
            return ThrowHelper.ThrowTypeError<string>($"{JsOperations.DescribeForError(value)} is not a string");

        return str.ToString();
    }

    /// <summary>GetOptionsObject(options)</summary>
    private static JsObject? GetOptionsObject(JsValue options)
    {
        if (options.IsUndefined)
            return null;

        if (options.Cell is not JsObject obj)
            return ThrowHelper.ThrowTypeError<JsObject>("The options argument must be an object");

        return obj;
    }

    /// <summary>Reads the alphabet option: true for "base64url".</summary>
    private static bool GetAlphabetOption(JsObject? options)
    {
        var alphabet = options is null ? JsValue.Undefined : options.Get(AlphabetKey);
        if (alphabet.IsUndefined)
            return false;

        if (alphabet.Cell is JsString str)
        {
            switch (str.ToString())
            {
                case "base64":
                    return false;
                case "base64url":
                    return true;
            }
        }

        return ThrowHelper.ThrowTypeError<bool>("The alphabet option must be \"base64\" or \"base64url\"");
    }

    /// <summary>Reads the lastChunkHandling option.</summary>
    private static LastChunkHandling GetLastChunkHandlingOption(JsObject? options)
    {
        var value = options is null ? JsValue.Undefined : options.Get(LastChunkHandlingKey);
        if (value.IsUndefined)
            return LastChunkHandling.Loose;

        if (value.Cell is JsString str)
        {
            switch (str.ToString())
            {
                case "loose":
                    return LastChunkHandling.Loose;
                case "strict":
                    return LastChunkHandling.Strict;
                case "stop-before-partial":
                    return LastChunkHandling.StopBeforePartial;
            }
        }

        return ThrowHelper.ThrowTypeError<LastChunkHandling>("The lastChunkHandling option must be \"loose\", \"strict\" or \"stop-before-partial\"");
    }

    /// <summary>Uint8Array.fromBase64(string [, options])</summary>
    private static JsValue FromBase64(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var str = RequireString(Arg(arguments, 0));
        var options = GetOptionsObject(Arg(arguments, 1));
        var base64Url = GetAlphabetOption(options);
        var lastChunkHandling = GetLastChunkHandlingOption(options);
        var bytes = new List<byte>(str.Length / 4 * 3);
        if (!DecodeBase64(str, base64Url, lastChunkHandling, long.MaxValue, bytes, out _))
            return ThrowInvalidBase64();

        var result = TypedArrayBuiltins.CreateTypedArray(function.Realm, TypedArrayElementType.Uint8, bytes.Count);
        System.Runtime.InteropServices.CollectionsMarshal.AsSpan(bytes).CopyTo(result.GetSpan());
        return result;
    }

    /// <summary>Uint8Array.fromHex(string)</summary>
    private static JsValue FromHex(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var str = RequireString(Arg(arguments, 0));
        var bytes = new List<byte>(str.Length / 2);
        if (!DecodeHex(str, long.MaxValue, bytes, out _))
            return ThrowInvalidHex();

        var result = TypedArrayBuiltins.CreateTypedArray(function.Realm, TypedArrayElementType.Uint8, bytes.Count);
        System.Runtime.InteropServices.CollectionsMarshal.AsSpan(bytes).CopyTo(result.GetSpan());
        return result;
    }

    /// <summary>Uint8Array.prototype.setFromBase64(string [, options])</summary>
    private static JsValue SetFromBase64(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var into = ValidateUint8Array(thisValue, "setFromBase64", write: true);
        var str = RequireString(Arg(arguments, 0));
        var options = GetOptionsObject(Arg(arguments, 1));
        var base64Url = GetAlphabetOption(options);
        var lastChunkHandling = GetLastChunkHandlingOption(options);
        if (!into.TryGetLength(out var byteLength))
            return ThrowHelper.ThrowTypeError<JsValue>("Cannot perform Uint8Array.prototype.setFromBase64 on a detached or out-of-bounds TypedArray");

        var bytes = new List<byte>(Math.Min(byteLength, (str.Length / 4 * 3) + 3));
        var success = DecodeBase64(str, base64Url, lastChunkHandling, byteLength, bytes, out var read);
        System.Runtime.InteropServices.CollectionsMarshal.AsSpan(bytes).CopyTo(into.GetSpan());
        if (!success)
            return ThrowInvalidBase64();

        return CreateResult(function.Realm, read, bytes.Count);
    }

    /// <summary>Uint8Array.prototype.setFromHex(string)</summary>
    private static JsValue SetFromHex(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var into = ValidateUint8Array(thisValue, "setFromHex", write: true);
        var str = RequireString(Arg(arguments, 0));
        if (!into.TryGetLength(out var byteLength))
            return ThrowHelper.ThrowTypeError<JsValue>("Cannot perform Uint8Array.prototype.setFromHex on a detached or out-of-bounds TypedArray");

        var bytes = new List<byte>(Math.Min(byteLength, str.Length / 2));
        var success = DecodeHex(str, byteLength, bytes, out var read);
        System.Runtime.InteropServices.CollectionsMarshal.AsSpan(bytes).CopyTo(into.GetSpan());
        if (!success)
            return ThrowInvalidHex();

        return CreateResult(function.Realm, read, bytes.Count);
    }

    private static JsObject CreateResult(JsRealm realm, int read, int written)
    {
        var result = new JsObject(realm.Intrinsics.ObjectPrototype);
        result.CreateDataPropertyOrThrow(ReadKey, read);
        result.CreateDataPropertyOrThrow(WrittenKey, written);
        return result;
    }

    /// <summary>Uint8Array.prototype.toBase64([options])</summary>
    private static JsValue ToBase64(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var typedArray = ValidateUint8Array(thisValue, "toBase64");
        var options = GetOptionsObject(Arg(arguments, 0));
        var base64Url = GetAlphabetOption(options);
        var omitPadding = options is not null && JsOperations.ToBoolean(options.Get(OmitPaddingKey));
        var bytes = GetUint8ArrayBytes(typedArray, "toBase64");
        if (((long)bytes.Length + 2) / 3 * 4 > JsString.MaxLength)
            return ThrowHelper.ThrowRangeError<JsValue>("Invalid string length");

        var encoded = Convert.ToBase64String(bytes);
        if (base64Url)
        {
            encoded = encoded.Replace('+', '-').Replace('/', '_');
        }

        if (omitPadding)
        {
            encoded = encoded.TrimEnd('=');
        }

        return encoded;
    }

    /// <summary>Uint8Array.prototype.toHex()</summary>
    private static JsValue ToHex(JsBuiltinFunction function, JsValue thisValue, ReadOnlySpan<JsValue> arguments, JsObject? newTarget)
    {
        var typedArray = ValidateUint8Array(thisValue, "toHex");
        var bytes = GetUint8ArrayBytes(typedArray, "toHex");
        if ((long)bytes.Length * 2 > JsString.MaxLength)
            return ThrowHelper.ThrowRangeError<JsValue>("Invalid string length");

        return Convert.ToHexStringLower(bytes);
    }

    /// <summary>GetUint8ArrayBytes(ta)</summary>
    private static ReadOnlySpan<byte> GetUint8ArrayBytes(JsTypedArray typedArray, string method)
    {
        if (typedArray.IsOutOfBounds)
            return ThrowHelper.ThrowTypeError<byte[]>($"Cannot perform Uint8Array.prototype.{method} on a detached or out-of-bounds TypedArray");

        return typedArray.GetSpan();
    }

    private static JsValue ThrowInvalidBase64() => throw ThrowHelper.CreateException(ErrorType.SyntaxError, "Invalid base64 string");

    private static JsValue ThrowInvalidHex() => throw ThrowHelper.CreateException(ErrorType.SyntaxError, "Invalid hex string");

    /// <summary>SkipAsciiWhitespace(string, index)</summary>
    private static int SkipAsciiWhitespace(string str, int index)
    {
        while (index < str.Length && str[index] is '\t' or '\n' or '\f' or '\r' or ' ')
        {
            index++;
        }

        return index;
    }

    private static int GetBase64Value(char c)
    {
        return c switch
        {
            >= 'A' and <= 'Z' => c - 'A',
            >= 'a' and <= 'z' => c - 'a' + 26,
            >= '0' and <= '9' => c - '0' + 52,
            '+' => 62,
            '/' => 63,
            _ => -1,
        };
    }

    /// <summary>
    /// FromBase64(string, alphabet, lastChunkHandling, maxLength): appends the decoded bytes; false when the string is
    /// invalid ([[Error]] is a SyntaxError), the bytes decoded before the error are kept.
    /// </summary>
    private static bool DecodeBase64(string str, bool base64Url, LastChunkHandling lastChunkHandling, long maxLength, List<byte> bytes, out int read)
    {
        read = 0;
        if (maxLength == 0)
            return true;

        Span<int> chunk = stackalloc int[4];
        var chunkLength = 0;
        var index = 0;
        var length = str.Length;
        while (true)
        {
            index = SkipAsciiWhitespace(str, index);
            if (index == length)
            {
                if (chunkLength > 0)
                {
                    if (lastChunkHandling == LastChunkHandling.StopBeforePartial)
                        return true;

                    if (lastChunkHandling == LastChunkHandling.Strict || chunkLength == 1)
                        return false;

                    DecodeFinalChunk(chunk, chunkLength, throwOnExtraBits: false, bytes);
                }

                read = length;
                return true;
            }

            var c = str[index];
            index++;
            if (c == '=')
            {
                if (chunkLength < 2)
                    return false;

                index = SkipAsciiWhitespace(str, index);
                if (chunkLength == 2)
                {
                    if (index == length)
                        return lastChunkHandling == LastChunkHandling.StopBeforePartial;

                    if (str[index] == '=')
                    {
                        index = SkipAsciiWhitespace(str, index + 1);
                    }
                }

                if (index < length)
                    return false;

                if (!DecodeFinalChunk(chunk, chunkLength, throwOnExtraBits: lastChunkHandling == LastChunkHandling.Strict, bytes))
                    return false;

                read = length;
                return true;
            }

            if (base64Url)
            {
                if (c is '+' or '/')
                    return false;

                if (c == '-')
                {
                    c = '+';
                }
                else if (c == '_')
                {
                    c = '/';
                }
            }

            var value = GetBase64Value(c);
            if (value < 0)
                return false;

            var remaining = maxLength - bytes.Count;
            if ((remaining == 1 && chunkLength == 2) || (remaining == 2 && chunkLength == 3))
                return true;

            chunk[chunkLength++] = value;
            if (chunkLength == 4)
            {
                var bits = (chunk[0] << 18) | (chunk[1] << 12) | (chunk[2] << 6) | chunk[3];
                bytes.Add((byte)(bits >> 16));
                bytes.Add((byte)(bits >> 8));
                bytes.Add((byte)bits);
                chunkLength = 0;
                read = index;
                if (bytes.Count == maxLength)
                    return true;
            }
        }
    }

    /// <summary>DecodeFinalBase64Chunk(chunk, throwOnExtraBits) for a chunk of 2 or 3 characters: false when the extra bits are not zero and must be.</summary>
    private static bool DecodeFinalChunk(ReadOnlySpan<int> chunk, int chunkLength, bool throwOnExtraBits, List<byte> bytes)
    {
        if (chunkLength == 2)
        {
            var bits = (chunk[0] << 18) | (chunk[1] << 12);
            if (throwOnExtraBits && (byte)(bits >> 8) != 0)
                return false;

            bytes.Add((byte)(bits >> 16));
        }
        else
        {
            var bits = (chunk[0] << 18) | (chunk[1] << 12) | (chunk[2] << 6);
            if (throwOnExtraBits && (byte)bits != 0)
                return false;

            bytes.Add((byte)(bits >> 16));
            bytes.Add((byte)(bits >> 8));
        }

        return true;
    }

    /// <summary>FromHex(string, maxLength): appends the decoded bytes; false when the string is invalid, the bytes decoded before the error are kept.</summary>
    private static bool DecodeHex(string str, long maxLength, List<byte> bytes, out int read)
    {
        read = 0;
        var length = str.Length;
        if (length % 2 != 0)
            return false;

        while (read < length && bytes.Count < maxLength)
        {
            var high = NumberParsing.GetDigitValue(str[read]);
            var low = NumberParsing.GetDigitValue(str[read + 1]);
            if (high is < 0 or >= 16 || low is < 0 or >= 16)
                return false;

            read += 2;
            bytes.Add((byte)((high << 4) | low));
        }

        return true;
    }
}
