namespace Meziantou.Framework.Mail;

/// <summary>The method that obfuscates the data blocks of an Outlook data file. None of them needs the password of the file.</summary>
public enum PstEncryption
{
    /// <summary>The blocks are stored as is.</summary>
    None = 0,

    /// <summary>Each byte is replaced through a substitution table ("compressible encryption", the default of Outlook).</summary>
    Permute = 1,

    /// <summary>A substitution keyed by the identifier of each block ("high encryption").</summary>
    Cyclic = 2,
}
