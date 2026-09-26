// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using Meziantou.Framework.Markdown.Helpers;

namespace Meziantou.Framework.Markdown.Syntax;

/// <summary>
/// A typed key used to store data on <see cref="IMarkdownObject"/> instances.
/// </summary>
/// <typeparam name="T">The value type associated with this key.</typeparam>
public sealed class DataKey<T>
{
    /// <summary>
    /// Gets the opaque key object used for storage.
    /// </summary>
    public object Key { get; } = new object();
}
