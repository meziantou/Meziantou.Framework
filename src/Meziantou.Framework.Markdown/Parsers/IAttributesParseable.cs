// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Syntax;

namespace Meziantou.Framework.Markdown.Parsers;

/// <summary>
/// An interface used to tag <see cref="BlockParser"/> that supports parsing <see cref="Renderers.Html.HtmlAttributes"/>
/// </summary>
public interface IAttributesParseable
{
    /// <summary>
    /// A delegates that allows to process attached attributes
    /// </summary>
    TryParseAttributesDelegate? TryParseAttributes { get; set; }
}