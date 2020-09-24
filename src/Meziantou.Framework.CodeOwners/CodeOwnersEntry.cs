using System;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.CodeOwners
{
    [StructLayout(LayoutKind.Auto)]
    public readonly struct CodeOwnersEntry : IEquatable<CodeOwnersEntry>
    {
        private CodeOwnersEntry(string pattern, CodeOwnersEntryType entryType, string member)
        {
            Pattern = pattern ?? throw new ArgumentNullException(nameof(pattern));
            Member = member ?? throw new ArgumentNullException(nameof(member));
            EntryType = entryType;
        }

        public string Pattern { get; }

        public CodeOwnersEntryType EntryType { get; }

        public string Member { get; }

        internal static CodeOwnersEntry FromUsername(string pattern, string username)
        {
            return new CodeOwnersEntry(pattern, CodeOwnersEntryType.Username, username);
        }

        internal static CodeOwnersEntry FromEmailAddress(string pattern, string emailAddress)
        {
            return new CodeOwnersEntry(pattern, CodeOwnersEntryType.EmailAddress, emailAddress);
        }

        public override bool Equals(object? obj)
        {
            return obj is CodeOwnersEntry entry && Equals(entry);

        }

        public bool Equals(CodeOwnersEntry other)
        {
            return EntryType == other.EntryType &&
                   string.Equals(Pattern, other.Pattern, StringComparison.Ordinal) &&
                   string.Equals(Member, other.Member, StringComparison.Ordinal);
        }

        public override int GetHashCode()
        {
            var hashCode = 1707150943;
            hashCode = hashCode * -1521134295 + EntryType.GetHashCode();
            hashCode = hashCode * -1521134295 + StringComparer.Ordinal.GetHashCode(Pattern);
            hashCode = hashCode * -1521134295 + StringComparer.Ordinal.GetHashCode(Member);
            return hashCode;
        }

        public static bool operator ==(CodeOwnersEntry left, CodeOwnersEntry right) => left.Equals(right);
        public static bool operator !=(CodeOwnersEntry left, CodeOwnersEntry right) => !(left == right);
    }
}
