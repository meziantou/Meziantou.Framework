// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Tomlyn.Helpers;
using Tomlyn.Model;
using Tomlyn.Syntax;
using Tomlyn.Text;

namespace Tomlyn.Parsing;


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
