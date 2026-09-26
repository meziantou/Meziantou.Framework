// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Syntax;

namespace Meziantou.Framework.Markdown.Parsers;

/// <summary>
/// Delegates called when processing a document
/// </summary>
/// <param name="document">The markdown document.</param>
public delegate void ProcessDocumentDelegate(MarkdownDocument document);
