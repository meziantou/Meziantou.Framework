// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Meziantou.Framework.Markdown.Extensions.Tables;
using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Parsers.Inlines;
using Meziantou.Framework.Markdown.Syntax;
using Meziantou.Framework.Markdown.Syntax.Inlines;

namespace Meziantou.Framework.Markdown.Parsers;

/// <summary>
/// A delegate called at inline processing stage.
/// </summary>
/// <param name="processor">The processor.</param>
/// <param name="inline">The inline being processed.</param>
public delegate void ProcessInlineDelegate(InlineProcessor processor, Inline? inline);
