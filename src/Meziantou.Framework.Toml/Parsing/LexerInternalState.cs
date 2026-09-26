using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Meziantou.Framework.Toml.Helpers;
using Meziantou.Framework.Toml.Model;
using Meziantou.Framework.Toml.Syntax;
using Meziantou.Framework.Toml.Text;

namespace Meziantou.Framework.Toml.Parsing;


[DebuggerDisplay("{Position} {CurrentChar}")]
[StructLayout(LayoutKind.Auto)]
internal struct LexerInternalState
{
    public LexerInternalState(TextPosition nextPosition, TextPosition position, Char32 c)
    {
        NextPosition = nextPosition;
        Position = position;
        CurrentChar = c;
    }

    public TextPosition NextPosition;

    public TextPosition Position;

    public Char32 CurrentChar;
}
