namespace Meziantou.Framework.Compression.Internals.Rar;

/// <summary>The keys of RAR5 derived from a password and a salt.</summary>
internal sealed record Rar5Key(byte[] Key, byte[] HashKey, byte[] PasswordCheck);
