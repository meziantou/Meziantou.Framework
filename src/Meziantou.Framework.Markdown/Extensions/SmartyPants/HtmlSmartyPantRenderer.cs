// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Renderers;
using Meziantou.Framework.Markdown.Renderers.Html;

namespace Meziantou.Framework.Markdown.Extensions.SmartyPants;

/// <summary>
/// A HTML renderer for a <see cref="SmartyPant"/>.
/// </summary>
/// <seealso cref="HtmlObjectRenderer{SmartyPant}" />
public class HtmlSmartyPantRenderer : HtmlObjectRenderer<SmartyPant>
{
    private static readonly SmartyPantOptions DefaultOptions = new SmartyPantOptions();

    private readonly SmartyPantOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="HtmlSmartyPantRenderer"/> class.
    /// </summary>
    /// <param name="options">The options.</param>
    /// <exception cref="ArgumentNullException"></exception>
    public HtmlSmartyPantRenderer(SmartyPantOptions? options)
    {
        this._options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Writes the object to the specified renderer.
    /// </summary>
    protected override void Write(HtmlRenderer renderer, SmartyPant obj)
    {
        if (!_options.Mapping.TryGetValue(obj.Type, out string? text))
        {
            DefaultOptions.Mapping.TryGetValue(obj.Type, out text);
        }
        renderer.Write(text);
    }
}
