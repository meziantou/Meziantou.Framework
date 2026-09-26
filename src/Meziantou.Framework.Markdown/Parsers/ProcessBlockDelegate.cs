// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Syntax;

namespace Meziantou.Framework.Markdown.Parsers;

/// <summary>
/// Delegates called when processing a block
/// </summary>
public delegate void ProcessBlockDelegate(BlockProcessor processor, Block block);
