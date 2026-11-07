namespace Meziantou.Framework.Hashing;

/// <summary>
/// Computes the extendable-output functions SHAKE128 and SHAKE256 (FIPS 202), and cSHAKE128 and cSHAKE256 (NIST SP 800-185)
/// when a function name or a customization string is given.
/// </summary>
/// <remarks>
/// <para>
/// An extendable-output function produces an output of any length: a shorter output is a prefix of a longer one. Use the
/// static <see cref="HashData(int, ReadOnlySpan{byte}, int, ReadOnlySpan{byte}, ReadOnlySpan{byte})"/> methods to hash data in
/// one call, or an instance to append the data in several calls and read the output in several calls.
/// </para>
/// <para>
/// cSHAKE with an empty function name and an empty customization string is SHAKE. The implementation is fully managed: it
/// does not depend on the cryptographic library of the operating system, so it is available on every platform.
/// </para>
/// <para>Instances are not thread-safe.</para>
/// </remarks>
public sealed class Shake
{
    private readonly KeccakSponge _initialState;
    private KeccakSponge _sponge;

    /// <summary>Initializes a new instance of the <see cref="Shake"/> class.</summary>
    /// <param name="securityStrength">The security strength, in bits: 128 (SHAKE128, cSHAKE128) or 256 (SHAKE256, cSHAKE256).</param>
    /// <param name="functionName">The function name N of cSHAKE, reserved for the functions defined by NIST; usually empty.</param>
    /// <param name="customization">The customization string S of cSHAKE.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="securityStrength"/> is not 128 or 256.</exception>
    public Shake(int securityStrength, ReadOnlySpan<byte> functionName = default, ReadOnlySpan<byte> customization = default)
    {
        _initialState = Keccak.CreateCShake(securityStrength, functionName, customization);
        _sponge = _initialState;
        SecurityStrength = securityStrength;
    }

    /// <summary>Gets the security strength, in bits: 128 or 256.</summary>
    public int SecurityStrength { get; }

    /// <summary>Appends data to the input.</summary>
    /// <param name="data">The data to append.</param>
    /// <exception cref="InvalidOperationException">The output was read: call <see cref="Reset"/> to hash new data.</exception>
    public void AppendData(ReadOnlySpan<byte> data)
    {
        if (_sponge.IsSqueezing)
            throw new InvalidOperationException("Data cannot be appended once the output has been read. Call Reset to hash new data.");

        _sponge.Absorb(data);
    }

    /// <summary>Appends a byte to the input.</summary>
    /// <param name="value">The byte to append.</param>
    /// <exception cref="InvalidOperationException">The output was read: call <see cref="Reset"/> to hash new data.</exception>
    public void AppendData(byte value)
    {
        AppendData(new ReadOnlySpan<byte>(in value));
    }

    /// <summary>Reads the next bytes of the output. The first call ends the input: no more data can be appended.</summary>
    /// <param name="destination">The buffer to fill with the next bytes of the output.</param>
    public void Read(Span<byte> destination)
    {
        _sponge.Squeeze(destination);
    }

    /// <summary>Reads the next byte of the output. The first call ends the input: no more data can be appended.</summary>
    /// <returns>The next byte of the output.</returns>
    public byte ReadByte()
    {
        byte value = 0;
        Read(new Span<byte>(ref value));
        return value;
    }

    /// <summary>Discards the input and the output, which may depend on secret data, so that the instance can hash new data.</summary>
    public void Reset()
    {
        _sponge.Clear();
        _sponge = _initialState;
    }

    /// <summary>Computes the output of SHAKE or cSHAKE for data.</summary>
    /// <param name="securityStrength">The security strength, in bits: 128 (SHAKE128, cSHAKE128) or 256 (SHAKE256, cSHAKE256).</param>
    /// <param name="source">The data to hash.</param>
    /// <param name="outputLength">The length of the output, in bytes.</param>
    /// <param name="functionName">The function name N of cSHAKE, reserved for the functions defined by NIST; usually empty.</param>
    /// <param name="customization">The customization string S of cSHAKE.</param>
    /// <returns>The output, of <paramref name="outputLength"/> bytes.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="securityStrength"/> is not 128 or 256, or <paramref name="outputLength"/> is negative.</exception>
    public static byte[] HashData(int securityStrength, ReadOnlySpan<byte> source, int outputLength, ReadOnlySpan<byte> functionName = default, ReadOnlySpan<byte> customization = default)
    {
        var destination = Keccak.AllocateOutput(outputLength);
        Keccak.CShake(securityStrength, source, destination, functionName, customization);
        return destination;
    }

    /// <summary>Computes the output of SHAKE or cSHAKE for data into a buffer.</summary>
    /// <param name="securityStrength">The security strength, in bits: 128 (SHAKE128, cSHAKE128) or 256 (SHAKE256, cSHAKE256).</param>
    /// <param name="source">The data to hash.</param>
    /// <param name="destination">The buffer to fill with the output.</param>
    /// <param name="functionName">The function name N of cSHAKE, reserved for the functions defined by NIST; usually empty.</param>
    /// <param name="customization">The customization string S of cSHAKE.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="securityStrength"/> is not 128 or 256.</exception>
    public static void HashData(int securityStrength, ReadOnlySpan<byte> source, Span<byte> destination, ReadOnlySpan<byte> functionName = default, ReadOnlySpan<byte> customization = default)
    {
        Keccak.CShake(securityStrength, source, destination, functionName, customization);
    }
}
