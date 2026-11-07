namespace Meziantou.Framework.Globbing.Internals;

/// <summary>POSIX character classes usable in a bracket expression, such as <c>[[:digit:]]</c>.</summary>
internal enum NamedCharacterClass
{
    Alnum,
    Alpha,
    Blank,
    Cntrl,
    Digit,
    Graph,
    Lower,
    Print,
    Punct,
    Space,
    Upper,
    XDigit,
}
