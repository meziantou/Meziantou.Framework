namespace Meziantou.Framework.Mail;

/// <summary>The versions of the vCard format.</summary>
public enum VCardVersion
{
    /// <summary>vCard 2.1 (versit): types without names (<c>TEL;WORK</c>), quoted-printable values.</summary>
    V21,

    /// <summary>vCard 3.0 ([RFC 2426]).</summary>
    V30,

    /// <summary>vCard 4.0 ([RFC 6350]).</summary>
    V40,
}
