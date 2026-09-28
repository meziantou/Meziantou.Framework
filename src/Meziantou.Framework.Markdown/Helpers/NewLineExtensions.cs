// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

namespace Meziantou.Framework.Markdown.Helpers;

/// <summary>
/// Represents the NewLineExtensions type.
/// </summary>
public static class NewLineExtensions
{
    /// <summary>
    /// Performs the as string operation.
    /// </summary>
    public static string AsString(this NewLine newLine) => newLine switch
    {
        NewLine.CarriageReturnLineFeed => "\r\n",
        NewLine.LineFeed => "\n",
        NewLine.CarriageReturn => "\r",
        _ => string.Empty,
    };

    /// <summary>
    /// Performs the length operation.
    /// </summary>
    public static int Length(this NewLine newLine) => (int)newLine & 3;
}
