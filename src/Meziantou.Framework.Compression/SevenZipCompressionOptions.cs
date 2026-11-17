namespace Meziantou.Framework.Compression;

/// <summary>Options to write a 7z archive. The defaults are the ones of 7-Zip: LZMA2 at level 5, solid, with compressed headers.</summary>
public sealed class SevenZipCompressionOptions
{
    private int _level = 5;
    private int? _dictionarySize;
    private long _solidBlockSize;
    private int _solidFileCount;

    /// <summary>Gets or sets the compression method. The default is <see cref="SevenZipCompressionMethod.Lzma2"/>.</summary>
    public SevenZipCompressionMethod Method { get; set; } = SevenZipCompressionMethod.Lzma2;

    /// <summary>
    /// Gets or sets the compression level, from 0 to 9, as the <c>-mx</c> switch of 7-Zip. The default is 5. Level 0 stores the
    /// data (<see cref="SevenZipCompressionMethod.Copy"/>) whatever the method.
    /// </summary>
    public int Level
    {
        get => _level;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 9);
            _level = value;
        }
    }

    /// <summary>
    /// Gets or sets the size of the dictionary of LZMA and LZMA2, the memory of the PPMd model, or the block size of BZip2 (in
    /// bytes, rounded to a multiple of 100,000 from 100,000 to 900,000). <see langword="null"/> uses the size of the level.
    /// </summary>
    public int? DictionarySize
    {
        get => _dictionarySize;
        set
        {
            if (value is < 4096)
                throw new ArgumentOutOfRangeException(nameof(value), value, "The dictionary size must be at least 4096 bytes.");

            _dictionarySize = value;
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the content of several files is compressed together, in one folder (a solid
    /// block). The default is <see langword="true"/>. A solid archive is smaller, but reading a file decodes the files before
    /// it in its folder.
    /// </summary>
    public bool Solid { get; set; } = true;

    /// <summary>Gets or sets the number of bytes of content after which a solid folder is closed, or 0 for no limit (the default).</summary>
    public long SolidBlockSize
    {
        get => _solidBlockSize;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _solidBlockSize = value;
        }
    }

    /// <summary>Gets or sets the number of files after which a solid folder is closed, or 0 for no limit (the default).</summary>
    public int SolidFileCount
    {
        get => _solidFileCount;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _solidFileCount = value;
        }
    }

    /// <summary>
    /// Gets or sets the filter applied before the compression. The default, <see cref="SevenZipFilter.Auto"/>, applies the x86
    /// filter to the executables (by their extension), and writes them to their own folders.
    /// </summary>
    public SevenZipFilter Filter { get; set; } = SevenZipFilter.Auto;

    /// <summary>
    /// Gets or sets the password that encrypts the content with AES-256, or <see langword="null"/> to not encrypt it. The names
    /// of the entries are encrypted too only with <see cref="EncryptHeaders"/>.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the headers (the names, sizes and times of the entries) are encrypted, as the
    /// <c>-mhe=on</c> switch of 7-Zip. It requires a <see cref="Password"/>.
    /// </summary>
    public bool EncryptHeaders { get; set; }

    /// <summary>Gets or sets a value indicating whether the headers are compressed with LZMA. The default is <see langword="true"/>.</summary>
    public bool CompressHeaders { get; set; } = true;

    internal SevenZipCompressionOptions Clone() => (SevenZipCompressionOptions)MemberwiseClone();
}
