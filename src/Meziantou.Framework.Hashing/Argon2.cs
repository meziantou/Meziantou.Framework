using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;
using System.Security.Cryptography;

namespace Meziantou.Framework.Hashing;

/// <summary>Computes the Argon2 memory-hard function (RFC 9106): Argon2d, Argon2i and Argon2id.</summary>
/// <remarks>
/// <para>
/// Argon2 derives a key from a password, or hashes a password to store it. RFC 9106 recommends Argon2id. The cost is set by
/// the memory size, the number of passes and the degree of parallelism; RFC 9106 section 4 gives recommended values, such as
/// 2 GiB of memory with 1 pass, or 64 MiB of memory with 3 passes, both with 4 lanes.
/// </para>
/// <para>The lanes are computed sequentially: the degree of parallelism changes the result, not the number of threads.</para>
/// </remarks>
public static class Argon2
{
    /// <summary>The maximum degree of parallelism p (RFC 9106 section 3.1: 2^24 - 1).</summary>
    public const int MaxParallelism = (1 << 24) - 1;

    /// <summary>The minimum length of the tag T, in bytes (RFC 9106 section 3.1).</summary>
    public const int MinOutputLength = 4;

    private const int SyncPoints = 4;
    private const int BlockSize = 1024;
    private const int QWordsInBlock = BlockSize / 8;

    /// <summary>Computes the Argon2 tag T (RFC 9106 section 3.2).</summary>
    /// <param name="type">The variant y: <see cref="Argon2Type.Argon2id"/> unless you have a reason to use another one.</param>
    /// <param name="password">The message P: the password.</param>
    /// <param name="salt">The nonce S: a salt, 16 bytes of random data are recommended.</param>
    /// <param name="parallelism">The degree of parallelism p, the number of lanes: 1 to <see cref="MaxParallelism"/>.</param>
    /// <param name="memorySizeInKiB">The memory size m, in kibibytes: at least 8 times <paramref name="parallelism"/>.</param>
    /// <param name="passes">The number of passes t over the memory: at least 1.</param>
    /// <param name="outputLength">The length of the tag T, in bytes: at least <see cref="MinOutputLength"/>.</param>
    /// <param name="secret">The secret value K (a pepper), usually empty.</param>
    /// <param name="associatedData">The associated data X, usually empty.</param>
    /// <param name="version">The version v.</param>
    /// <returns>The tag, of <paramref name="outputLength"/> bytes.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A parameter is out of the range allowed by RFC 9106.</exception>
    /// <exception cref="OutOfMemoryException">The memory cannot be allocated.</exception>
    public static byte[] DeriveBytes(Argon2Type type, ReadOnlySpan<byte> password, ReadOnlySpan<byte> salt, int parallelism, int memorySizeInKiB, int passes, int outputLength, ReadOnlySpan<byte> secret = default, ReadOnlySpan<byte> associatedData = default, Argon2Version version = Argon2Version.Version13)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(outputLength, MinOutputLength);
        var destination = new byte[outputLength];
        DeriveBytes(type, password, salt, parallelism, memorySizeInKiB, passes, destination, secret, associatedData, version);
        return destination;
    }

    /// <summary>Computes the Argon2 tag T (RFC 9106 section 3.2) into a buffer.</summary>
    /// <param name="type">The variant y: <see cref="Argon2Type.Argon2id"/> unless you have a reason to use another one.</param>
    /// <param name="password">The message P: the password.</param>
    /// <param name="salt">The nonce S: a salt, 16 bytes of random data are recommended.</param>
    /// <param name="parallelism">The degree of parallelism p, the number of lanes: 1 to <see cref="MaxParallelism"/>.</param>
    /// <param name="memorySizeInKiB">The memory size m, in kibibytes: at least 8 times <paramref name="parallelism"/>.</param>
    /// <param name="passes">The number of passes t over the memory: at least 1.</param>
    /// <param name="destination">The buffer that receives the tag: its length, at least <see cref="MinOutputLength"/> bytes, is the length of the tag.</param>
    /// <param name="secret">The secret value K (a pepper), usually empty.</param>
    /// <param name="associatedData">The associated data X, usually empty.</param>
    /// <param name="version">The version v.</param>
    /// <exception cref="ArgumentOutOfRangeException">A parameter is out of the range allowed by RFC 9106.</exception>
    /// <exception cref="OutOfMemoryException">The memory cannot be allocated.</exception>
    public static void DeriveBytes(Argon2Type type, ReadOnlySpan<byte> password, ReadOnlySpan<byte> salt, int parallelism, int memorySizeInKiB, int passes, Span<byte> destination, ReadOnlySpan<byte> secret = default, ReadOnlySpan<byte> associatedData = default, Argon2Version version = Argon2Version.Version13)
    {
        if (type is not (Argon2Type.Argon2d or Argon2Type.Argon2i or Argon2Type.Argon2id))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "The type of Argon2 is not supported.");
        }

        if (version is not (Argon2Version.Version10 or Argon2Version.Version13))
        {
            throw new ArgumentOutOfRangeException(nameof(version), version, "The version of Argon2 is not supported.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(parallelism, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(parallelism, MaxParallelism);
        ArgumentOutOfRangeException.ThrowIfLessThan(passes, 1);
        if (destination.Length < MinOutputLength)
        {
            throw new ArgumentOutOfRangeException(nameof(destination), destination.Length, $"The length of the destination must be at least {MinOutputLength}.");
        }

        var outputLength = destination.Length;
        if (memorySizeInKiB < 8L * parallelism)
        {
            throw new ArgumentOutOfRangeException(nameof(memorySizeInKiB), memorySizeInKiB, "The memory size must be at least 8 times the degree of parallelism.");
        }

        // https://www.rfc-editor.org/rfc/rfc9106#section-3.2: m' = 4 * p * floor(m / 4p) blocks, q = m' / p columns per lane
        var segmentLength = memorySizeInKiB / (SyncPoints * parallelism);
        var laneLength = segmentLength * SyncPoints;
        var blockCount = laneLength * parallelism;
        if ((long)blockCount * QWordsInBlock > Array.MaxLength)
        {
            throw new ArgumentOutOfRangeException(nameof(memorySizeInKiB), memorySizeInKiB, "The memory size is too large.");
        }

        var instance = new Instance(type, version, parallelism, passes, segmentLength, laneLength, blockCount);
        try
        {
            var hasher = new Blake2bHasher(Blake2b.MaxHashSize);

            // https://www.rfc-editor.org/rfc/rfc9106#section-3.2 step 1: H_0
            Span<byte> h0 = stackalloc byte[Blake2b.MaxHashSize + 8];
            hasher.AppendUInt32LittleEndian((uint)parallelism);
            hasher.AppendUInt32LittleEndian((uint)outputLength);
            hasher.AppendUInt32LittleEndian((uint)memorySizeInKiB);
            hasher.AppendUInt32LittleEndian((uint)passes);
            hasher.AppendUInt32LittleEndian((uint)version);
            hasher.AppendUInt32LittleEndian((uint)type);
            AppendWithLength(ref hasher, password);
            AppendWithLength(ref hasher, salt);
            AppendWithLength(ref hasher, secret);
            AppendWithLength(ref hasher, associatedData);
            hasher.GetHashAndReset(h0[..Blake2b.MaxHashSize]);

            // Steps 3 and 4: B[i][0] = H'^(1024)(H_0 || LE32(0) || LE32(i)) and B[i][1] = H'^(1024)(H_0 || LE32(1) || LE32(i))
            var blockBytes = new byte[BlockSize];
            for (var lane = 0; lane < parallelism; lane++)
            {
                for (var column = 0; column < 2; column++)
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(h0[Blake2b.MaxHashSize..], (uint)column);
                    BinaryPrimitives.WriteUInt32LittleEndian(h0[(Blake2b.MaxHashSize + 4)..], (uint)lane);
                    VariableLengthHash(ref hasher, h0, blockBytes);
                    LoadBlock(blockBytes, instance.GetBlock((lane * laneLength) + column));
                }
            }

            CryptographicOperations.ZeroMemory(h0);

            // Steps 5 and 6: the passes, slice by slice; the segments of a slice are independent
            for (var pass = 0; pass < passes; pass++)
            {
                for (var slice = 0; slice < SyncPoints; slice++)
                {
                    for (var lane = 0; lane < parallelism; lane++)
                    {
                        instance.FillSegment(pass, lane, slice);
                    }
                }
            }

            // Step 7: C = B[0][q-1] XOR B[1][q-1] XOR ... XOR B[p-1][q-1]
            var final = instance.GetBlock(laneLength - 1);
            for (var lane = 1; lane < parallelism; lane++)
            {
                Xor(final, instance.GetBlock((lane * laneLength) + laneLength - 1), final);
            }

            StoreBlock(final, blockBytes);

            // Step 8: Tag = H'^T(C)
            VariableLengthHash(ref hasher, blockBytes, destination);
            CryptographicOperations.ZeroMemory(blockBytes);
        }
        finally
        {
            instance.Clear();
        }
    }

    private static void AppendWithLength(ref Blake2bHasher hasher, ReadOnlySpan<byte> value)
    {
        hasher.AppendUInt32LittleEndian((uint)value.Length);
        hasher.AppendData(value);
    }

    /// <summary>The variable-length hash function H' (RFC 9106 section 3.3); the length T is the length of <paramref name="output"/>.</summary>
    private static void VariableLengthHash(ref Blake2bHasher hasher, ReadOnlySpan<byte> input, Span<byte> output)
    {
        var length = output.Length;
        if (length <= Blake2b.MaxHashSize)
        {
            hasher.Reset(length);
            hasher.AppendUInt32LittleEndian((uint)length);
            hasher.AppendData(input);
            hasher.GetHashAndReset(output);
            return;
        }

        // r = ceil(T / 32) - 2; V_1 = H^(64)(LE32(T) || A), V_i = H^(64)(V_(i-1)), V_(r+1) = H^(T-32*r)(V_r);
        // the output is the first 32 bytes of V_1 to V_r, followed by V_(r+1)
        var r = ((length + 31) / 32) - 2;
        Span<byte> v = stackalloc byte[Blake2b.MaxHashSize];
        hasher.Reset(Blake2b.MaxHashSize);
        hasher.AppendUInt32LittleEndian((uint)length);
        hasher.AppendData(input);
        hasher.GetHashAndReset(v);
        v[..32].CopyTo(output);
        for (var i = 2; i <= r; i++)
        {
            hasher.Reset(Blake2b.MaxHashSize);
            hasher.AppendData(v);
            hasher.GetHashAndReset(v);
            v[..32].CopyTo(output[((i - 1) * 32)..]);
        }

        hasher.Reset(length - (32 * r));
        hasher.AppendData(v);
        hasher.GetHashAndReset(output[(32 * r)..]);
        CryptographicOperations.ZeroMemory(v);
    }

    private static void LoadBlock(ReadOnlySpan<byte> source, Span<ulong> destination)
    {
        for (var i = 0; i < QWordsInBlock; i++)
        {
            destination[i] = BinaryPrimitives.ReadUInt64LittleEndian(source[(i * 8)..]);
        }
    }

    private static void StoreBlock(ReadOnlySpan<ulong> source, Span<byte> destination)
    {
        for (var i = 0; i < QWordsInBlock; i++)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(destination[(i * 8)..], source[i]);
        }
    }

    private static void Xor(ReadOnlySpan<ulong> left, ReadOnlySpan<ulong> right, Span<ulong> destination)
    {
        var length = destination.Length;
        var i = 0;
        if (Vector.IsHardwareAccelerated)
        {
            for (; i <= length - Vector<ulong>.Count; i += Vector<ulong>.Count)
            {
                (new Vector<ulong>(left[i..]) ^ new Vector<ulong>(right[i..])).CopyTo(destination[i..]);
            }
        }

        for (; i < length; i++)
        {
            destination[i] = left[i] ^ right[i];
        }
    }

    /// <summary>The compression function G (RFC 9106 section 3.5): next = G(previous, reference), XOR next when <paramref name="withXor"/> is set.</summary>
    /// <remarks><paramref name="next"/> may be the same block as <paramref name="reference"/>.</remarks>
    private static void FillBlock(ReadOnlySpan<ulong> previous, ReadOnlySpan<ulong> reference, Span<ulong> next, bool withXor, Span<ulong> r, Span<ulong> z)
    {
        if (IsVector128Supported)
        {
            FillBlockVector128(previous, reference, next, withXor, r);
        }
        else
        {
            FillBlockScalar(previous, reference, next, withXor, r, z);
        }
    }

    // The vector path needs the multiplication of the low 32-bit halves of the 64-bit lanes (pmuludq, umull)
    internal static bool IsVector128Supported => Vector128.IsHardwareAccelerated && (Sse2.IsSupported || AdvSimd.Arm64.IsSupported);

    /// <summary>
    /// <see cref="FillBlock"/> on vectors of two 64-bit words, as the optimized implementation of the reference: a row or a
    /// column of the block is 8 vectors (A0, A1, B0, B1, C0, C1, D0, D1), the permutation P computes GB on the columns of the
    /// 4x4 matrix of words two at a time, then on its diagonals once the vectors are rotated.
    /// </summary>
    internal static void FillBlockVector128(ReadOnlySpan<ulong> previous, ReadOnlySpan<ulong> reference, Span<ulong> next, bool withXor, Span<ulong> r)
    {
        var previousVectors = unsafe(MemoryMarshal.Cast<ulong, Vector128<ulong>>(previous))[..(QWordsInBlock / 2)];
        var referenceVectors = unsafe(MemoryMarshal.Cast<ulong, Vector128<ulong>>(reference))[..(QWordsInBlock / 2)];
        var nextVectors = unsafe(MemoryMarshal.Cast<ulong, Vector128<ulong>>(next))[..(QWordsInBlock / 2)];
        var state = unsafe(MemoryMarshal.Cast<ulong, Vector128<ulong>>(r))[..(QWordsInBlock / 2)];

        // R = X XOR Y in the state; next receives R (XOR its previous content), to which P(R) is XORed at the end
        for (var i = 0; i < state.Length; i++)
        {
            var value = referenceVectors[i] ^ previousVectors[i];
            state[i] = value;
            nextVectors[i] = withXor ? value ^ nextVectors[i] : value;
        }

        // The rows: 16 consecutive 64-bit words, 8 consecutive vectors
        for (var i = 0; i < 8; i++)
        {
            var row = state.Slice(i * 8, 8);
            Permute(ref row[0], ref row[1], ref row[2], ref row[3], ref row[4], ref row[5], ref row[6], ref row[7]);
        }

        // The columns: 8 pairs of 64-bit words, 16 words apart: 8 vectors, 8 vectors apart
        for (var i = 0; i < 8; i++)
        {
            var column = state.Slice(i, 57);
            Permute(ref column[0], ref column[8], ref column[16], ref column[24], ref column[32], ref column[40], ref column[48], ref column[56]);
        }

        for (var i = 0; i < state.Length; i++)
        {
            nextVectors[i] ^= state[i];
        }
    }

    // The permutation P on the vectors (v0, v1), (v2, v3), ..., (v14, v15)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Permute(
        ref Vector128<ulong> a0Ref, ref Vector128<ulong> a1Ref, ref Vector128<ulong> b0Ref, ref Vector128<ulong> b1Ref,
        ref Vector128<ulong> c0Ref, ref Vector128<ulong> c1Ref, ref Vector128<ulong> d0Ref, ref Vector128<ulong> d1Ref)
    {
        var a0 = a0Ref;
        var a1 = a1Ref;
        var b0 = b0Ref;
        var b1 = b1Ref;
        var c0 = c0Ref;
        var c1 = c1Ref;
        var d0 = d0Ref;
        var d1 = d1Ref;

        // The columns (v0, v4, v8, v12), (v1, v5, v9, v13), (v2, v6, v10, v14) and (v3, v7, v11, v15)
        Mix(ref a0, ref b0, ref c0, ref d0);
        Mix(ref a1, ref b1, ref c1, ref d1);

        // The diagonals (v0, v5, v10, v15), (v1, v6, v11, v12), (v2, v7, v8, v13) and (v3, v4, v9, v14): b becomes
        // (v5, v6), (v7, v4), c becomes (v10, v11), (v8, v9) and d becomes (v15, v12), (v13, v14)
        var b0Diagonal = Concatenate(b0, b1);
        var b1Diagonal = Concatenate(b1, b0);
        var d0Diagonal = Concatenate(d1, d0);
        var d1Diagonal = Concatenate(d0, d1);
        Mix(ref a0, ref b0Diagonal, ref c1, ref d0Diagonal);
        Mix(ref a1, ref b1Diagonal, ref c0, ref d1Diagonal);

        a0Ref = a0;
        a1Ref = a1;
        b0Ref = Concatenate(b1Diagonal, b0Diagonal);
        b1Ref = Concatenate(b0Diagonal, b1Diagonal);
        c0Ref = c0;
        c1Ref = c1;
        d0Ref = Concatenate(d0Diagonal, d1Diagonal);
        d1Ref = Concatenate(d1Diagonal, d0Diagonal);
    }

    // GB on two lanes
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Mix(ref Vector128<ulong> a, ref Vector128<ulong> b, ref Vector128<ulong> c, ref Vector128<ulong> d)
    {
        a = BlaMka(a, b);
        d = RotateRight32(d ^ a);
        c = BlaMka(c, d);
        b = RotateRight24(b ^ c);
        a = BlaMka(a, b);
        d = RotateRight16(d ^ a);
        c = BlaMka(c, d);
        b = RotateRight63(b ^ c);
    }

    // x + y + 2 * trunc(x) * trunc(y), trunc being the low 32 bits
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<ulong> BlaMka(Vector128<ulong> x, Vector128<ulong> y)
    {
        if (AdvSimd.Arm64.IsSupported)
        {
            // Two multiply-accumulates (umlal) instead of a multiplication and two additions
            var xLow = AdvSimd.ExtractNarrowingLower(x);
            var yLow = AdvSimd.ExtractNarrowingLower(y);
            return AdvSimd.MultiplyWideningLowerAndAdd(AdvSimd.MultiplyWideningLowerAndAdd(x + y, xLow, yLow), xLow, yLow);
        }

        var product = Sse2.Multiply(x.AsUInt32(), y.AsUInt32());
        return x + y + product + product;
    }

    // The high word of x, then the low word of y
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<ulong> Concatenate(Vector128<ulong> x, Vector128<ulong> y)
    {
        if (AdvSimd.Arm64.IsSupported)
            return AdvSimd.ExtractVector128(x, y, 1);

        return Sse2.Shuffle(x.AsDouble(), y.AsDouble(), 0b01).AsUInt64();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<ulong> RotateRight32(Vector128<ulong> value)
    {
        if (AdvSimd.Arm64.IsSupported)
            return AdvSimd.ReverseElement32(value);

        return Sse2.Shuffle(value.AsUInt32(), 0b10_11_00_01).AsUInt64();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<ulong> RotateRight24(Vector128<ulong> value)
    {
        if (AdvSimd.Arm64.IsSupported || Ssse3.IsSupported)
            return Vector128.Shuffle(value.AsByte(), Vector128.Create((byte)3, 4, 5, 6, 7, 0, 1, 2, 11, 12, 13, 14, 15, 8, 9, 10)).AsUInt64();

        return Vector128.ShiftRightLogical(value, 24) | Vector128.ShiftLeft(value, 40);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<ulong> RotateRight16(Vector128<ulong> value)
    {
        if (AdvSimd.Arm64.IsSupported || Ssse3.IsSupported)
            return Vector128.Shuffle(value.AsByte(), Vector128.Create((byte)2, 3, 4, 5, 6, 7, 0, 1, 10, 11, 12, 13, 14, 15, 8, 9)).AsUInt64();

        return Vector128.ShiftRightLogical(value, 16) | Vector128.ShiftLeft(value, 48);
    }

    // A rotation to the right by 63 bits is a rotation to the left by 1 bit
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<ulong> RotateRight63(Vector128<ulong> value)
    {
        // Arm64: the top bit shifted right and inserted into the low bit of the value shifted left (shl, sri)
        if (AdvSimd.Arm64.IsSupported)
            return AdvSimd.ShiftRightAndInsert(AdvSimd.ShiftLeftLogical(value, 1), value, 63);

        return Vector128.ShiftRightLogical(value, 63) ^ (value + value);
    }

    /// <summary><see cref="FillBlock"/> on 64-bit words, the reference of the vector path.</summary>
    internal static void FillBlockScalar(ReadOnlySpan<ulong> previous, ReadOnlySpan<ulong> reference, Span<ulong> next, bool withXor, Span<ulong> r, Span<ulong> z)
    {
        // R = X XOR Y; the result is P(R) XOR R (XOR the previous content of the block for the passes after the first one)
        Xor(reference, previous, r);
        if (withXor)
        {
            Xor(r, next, z);
        }
        else
        {
            r.CopyTo(z);
        }

        // The slices have a constant length, so the JIT removes the bounds checks of the constant indices: one check per slice.
        // The rows: (Q_0, ..., Q_7) <- P(R_0, ..., R_7), each R_i being 16 consecutive 64-bit words
        for (var i = 0; i < 8; i++)
        {
            var row = r.Slice(i * 16, 16);
            Permute(
                ref row[0], ref row[1], ref row[2], ref row[3], ref row[4], ref row[5], ref row[6], ref row[7],
                ref row[8], ref row[9], ref row[10], ref row[11], ref row[12], ref row[13], ref row[14], ref row[15]);
        }

        // The columns: (Z_i, Z_(8+i), ..., Z_(56+i)) <- P(Q_i, Q_(8+i), ..., Q_(56+i)), each Z_j being 2 consecutive 64-bit words
        for (var i = 0; i < 8; i++)
        {
            var column = r.Slice(i * 2, 114);
            Permute(
                ref column[0], ref column[1], ref column[16], ref column[17], ref column[32], ref column[33], ref column[48], ref column[49],
                ref column[64], ref column[65], ref column[80], ref column[81], ref column[96], ref column[97], ref column[112], ref column[113]);
        }

        Xor(z, r, next);
    }

    // https://www.rfc-editor.org/rfc/rfc9106#section-3.6: the permutation P, on 8 16-byte registers (v_0, ..., v_15)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Permute(
        ref ulong v0, ref ulong v1, ref ulong v2, ref ulong v3, ref ulong v4, ref ulong v5, ref ulong v6, ref ulong v7,
        ref ulong v8, ref ulong v9, ref ulong v10, ref ulong v11, ref ulong v12, ref ulong v13, ref ulong v14, ref ulong v15)
    {
        Mix(ref v0, ref v4, ref v8, ref v12);
        Mix(ref v1, ref v5, ref v9, ref v13);
        Mix(ref v2, ref v6, ref v10, ref v14);
        Mix(ref v3, ref v7, ref v11, ref v15);
        Mix(ref v0, ref v5, ref v10, ref v15);
        Mix(ref v1, ref v6, ref v11, ref v12);
        Mix(ref v2, ref v7, ref v8, ref v13);
        Mix(ref v3, ref v4, ref v9, ref v14);
    }

    // The function GB: the mixing function of BLAKE2b with the multiplications of the 32-bit halves
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Mix(ref ulong a, ref ulong b, ref ulong c, ref ulong d)
    {
        a = a + b + (2 * (ulong)(uint)a * (uint)b);
        d = BitOperations.RotateRight(d ^ a, 32);
        c = c + d + (2 * (ulong)(uint)c * (uint)d);
        b = BitOperations.RotateRight(b ^ c, 24);
        a = a + b + (2 * (ulong)(uint)a * (uint)b);
        d = BitOperations.RotateRight(d ^ a, 16);
        c = c + d + (2 * (ulong)(uint)c * (uint)d);
        b = BitOperations.RotateRight(b ^ c, 63);
    }

    /// <summary>The memory and the parameters of a computation.</summary>
    private sealed class Instance
    {
        private readonly Argon2Type _type;
        private readonly Argon2Version _version;
        private readonly int _lanes;
        private readonly int _passes;
        private readonly int _segmentLength;
        private readonly int _laneLength;
        private readonly int _blockCount;
        private readonly ulong[] _memory;

        // The scratch blocks of the compression function and of the generation of the addresses (Argon2i and Argon2id)
        private readonly ulong[] _r = new ulong[QWordsInBlock];
        private readonly ulong[] _z = new ulong[QWordsInBlock];
        private readonly ulong[] _zero = new ulong[QWordsInBlock];
        private readonly ulong[] _input = new ulong[QWordsInBlock];
        private readonly ulong[] _addresses = new ulong[QWordsInBlock];

        public Instance(Argon2Type type, Argon2Version version, int lanes, int passes, int segmentLength, int laneLength, int blockCount)
        {
            _type = type;
            _version = version;
            _lanes = lanes;
            _passes = passes;
            _segmentLength = segmentLength;
            _laneLength = laneLength;
            _blockCount = blockCount;
            _memory = new ulong[blockCount * QWordsInBlock];
        }

        public Span<ulong> GetBlock(int index) => _memory.AsSpan(index * QWordsInBlock, QWordsInBlock);

        public void Clear()
        {
            CryptographicOperations.ZeroMemory(unsafe(MemoryMarshal.AsBytes(_memory.AsSpan())));
            CryptographicOperations.ZeroMemory(unsafe(MemoryMarshal.AsBytes(_r.AsSpan())));
            CryptographicOperations.ZeroMemory(unsafe(MemoryMarshal.AsBytes(_z.AsSpan())));
            CryptographicOperations.ZeroMemory(unsafe(MemoryMarshal.AsBytes(_addresses.AsSpan())));
        }

        // https://www.rfc-editor.org/rfc/rfc9106#section-3.4: computes the blocks of a segment
        public void FillSegment(int pass, int lane, int slice)
        {
            var dataIndependent = _type == Argon2Type.Argon2i || (_type == Argon2Type.Argon2id && pass == 0 && slice < SyncPoints / 2);
            if (dataIndependent)
            {
                // Section 3.4.1.1: Z = LE64(r) || LE64(l) || LE64(sl) || LE64(m') || LE64(t) || LE64(y), followed by the counter
                Array.Clear(_input);
                _input[0] = (ulong)pass;
                _input[1] = (ulong)lane;
                _input[2] = (ulong)slice;
                _input[3] = (ulong)_blockCount;
                _input[4] = (ulong)_passes;
                _input[5] = (ulong)_type;
            }

            // The first two blocks of each lane are computed from H_0
            var startingIndex = 0;
            if (pass == 0 && slice == 0)
            {
                startingIndex = 2;
                if (dataIndependent)
                {
                    NextAddresses();
                }
            }

            var currentOffset = (lane * _laneLength) + (slice * _segmentLength) + startingIndex;
            var previousOffset = currentOffset % _laneLength == 0 ? currentOffset + _laneLength - 1 : currentOffset - 1;
            var withXor = _version != Argon2Version.Version10 && pass != 0;
            for (var index = startingIndex; index < _segmentLength; index++, currentOffset++, previousOffset++)
            {
                if (currentOffset % _laneLength == 1)
                {
                    previousOffset = currentOffset - 1;
                }

                // Section 3.4.1: J_1 is the low 32 bits of the pseudo-random value, J_2 the high 32 bits
                ulong pseudoRandom;
                if (dataIndependent)
                {
                    if (index % QWordsInBlock == 0)
                    {
                        NextAddresses();
                    }

                    pseudoRandom = _addresses[index % QWordsInBlock];
                }
                else
                {
                    pseudoRandom = _memory[previousOffset * QWordsInBlock];
                }

                // Section 3.4.1.2: l = J_2 mod p, except in the first slice of the first pass where it is the current lane
                var referenceLane = pass == 0 && slice == 0 ? lane : (int)((pseudoRandom >> 32) % (ulong)_lanes);
                var referenceIndex = GetReferenceIndex(pass, slice, index, (uint)pseudoRandom, referenceLane == lane);
                FillBlock(
                    GetBlock(previousOffset),
                    GetBlock((referenceLane * _laneLength) + referenceIndex),
                    GetBlock(currentOffset),
                    withXor,
                    _r,
                    _z);
            }
        }

        // Section 3.4.1.1: the next address block, G(ZERO, G(ZERO, Z)) with an incremented counter
        private void NextAddresses()
        {
            _input[6]++;
            FillBlock(_zero, _input, _addresses, withXor: false, _r, _z);
            FillBlock(_zero, _addresses, _addresses, withXor: false, _r, _z);
        }

        // https://www.rfc-editor.org/rfc/rfc9106#section-3.4.2: maps J_1 to the index z of the reference block in its lane
        private int GetReferenceIndex(int pass, int slice, int index, uint j1, bool sameLane)
        {
            // The reference set W: the blocks of the lane that are computed (of the current pass, or of the last 3 slices of the
            // previous pass), except the previous block; the blocks of the current slice are only included for the current lane
            long referenceAreaSize;
            if (pass == 0)
            {
                if (slice == 0)
                {
                    referenceAreaSize = index - 1;
                }
                else if (sameLane)
                {
                    referenceAreaSize = ((long)slice * _segmentLength) + index - 1;
                }
                else
                {
                    referenceAreaSize = ((long)slice * _segmentLength) + (index == 0 ? -1 : 0);
                }
            }
            else if (sameLane)
            {
                referenceAreaSize = _laneLength - _segmentLength + index - 1;
            }
            else
            {
                referenceAreaSize = _laneLength - _segmentLength + (index == 0 ? -1 : 0);
            }

            // x = J_1^2 / 2^32, y = (|W| * x) / 2^32, zz = |W| - 1 - y
            var size = (ulong)referenceAreaSize;
            var x = ((ulong)j1 * j1) >> 32;
            var relativePosition = size - 1 - ((size * x) >> 32);

            // The reference set starts after the current slice in the passes after the first one
            ulong startPosition = 0;
            if (pass != 0 && slice != SyncPoints - 1)
            {
                startPosition = (ulong)(slice + 1) * (ulong)_segmentLength;
            }

            return (int)((startPosition + relativePosition) % (ulong)_laneLength);
        }
    }
}
