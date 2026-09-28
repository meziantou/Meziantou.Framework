// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Renderers;
using Meziantou.Framework.Markdown.Renderers.Roundtrip;

namespace Meziantou.Framework.Markdown.Extensions.Yaml;

/// <summary>
/// Represents the YamlFrontMatterRoundtripRenderer type.
/// </summary>
public class YamlFrontMatterRoundtripRenderer : MarkdownObjectRenderer<RoundtripRenderer, YamlFrontMatterBlock>
{
    private readonly CodeBlockRenderer _codeBlockRenderer;

    /// <summary>
    /// Initializes a new instance of the YamlFrontMatterRoundtripRenderer class.
    /// </summary>
    public YamlFrontMatterRoundtripRenderer()
    {
        _codeBlockRenderer = new CodeBlockRenderer();
    }

    /// <summary>
    /// Writes the object to the specified renderer.
    /// </summary>
    protected override void Write(RoundtripRenderer renderer, YamlFrontMatterBlock obj)
    {
        WriteFence(renderer, obj.OpeningFence);
        _codeBlockRenderer.Write(renderer, obj);
        WriteFence(renderer, obj.ClosingFence);
    }

    // A block that was not parsed has no fence: it is written with the default one
    private static void WriteFence(RoundtripRenderer renderer, StringSlice fence)
    {
        if (fence.Text is null)
        {
            renderer.Writer.WriteLine("---");
            return;
        }

        renderer.Write(fence);
        renderer.WriteLine(fence.NewLine);
    }
}
