// Copyright (c) Alexandre Mutel. All rights reserved.
// This file is licensed under the BSD-Clause 2 license.
// See the license.txt file in the project root for more information.

using System.Globalization;

using Meziantou.Framework.Markdown.Helpers;
using Meziantou.Framework.Markdown.Syntax;

namespace Meziantou.Framework.Markdown.Renderers.Html;

/// <summary>
/// Attached HTML attributes to a <see cref="MarkdownObject"/>.
/// </summary>
public class HtmlAttributes : MarkdownObject
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HtmlAttributes"/> class.
    /// </summary>
    public HtmlAttributes()
    {
    }

    /// <summary>
    /// Gets or sets the HTML id/identifier. May be null.
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// Gets or sets the CSS classes attached. May be null.
    /// </summary>
    public List<string>? Classes { get; set; }

    /// <summary>
    /// Gets or sets the additional properties. May be null.
    /// </summary>
    public List<KeyValuePair<string, string?>>? Properties { get; set; }

    /// <summary>
    /// Adds a CSS class.
    /// </summary>
    /// <param name="name">The css class name.</param>
    public void AddClass(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        Classes ??= new (2);// Use half list compare to default capacity (4), as we don't expect lots of classes

        if (!Classes.Contains(name))
        {
            Classes.Add(name);
        }
    }

    /// <summary>
    /// Adds a property.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="value">The value.</param>
    public void AddProperty(string name, string value)
    {
        ArgumentNullException.ThrowIfNull(name);

        Properties ??= new (2); // Use half list compare to default capacity (4), as we don't expect lots of classes

        Properties.Add(new KeyValuePair<string, string?>(name, value));
    }

    /// <summary>
    /// Adds the specified property only if it does not already exist.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="value">The value.</param>
    public void AddPropertyIfNotExist(string name, object? value)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (Properties is null)
        {
            Properties = new (4);
        }
        else
        {
            for (int i = 0; i < Properties.Count; i++)
            {
                if (Properties[i].Key.Equals(name, StringComparison.Ordinal))
                {
                    return;
                }
            }
        }

        Properties.Add(new KeyValuePair<string, string?>(name, value is null ? null : Convert.ToString(value, CultureInfo.InvariantCulture)));
    }

    /// <summary>
    /// Copies/merge the values from this instance to the specified <see cref="HtmlAttributes"/> instance.
    /// </summary>
    /// <param name="htmlAttributes">The HTML attributes.</param>
    /// <param name="mergeIdAndProperties">If set to <c>true</c> it will merge properties to the target htmlAttributes. Default is <c>false</c></param>
    /// <param name="shared">If set to <c>true</c> it will try to share Classes and Properties if destination don't have them, otherwise it will make a copy. Default is <c>true</c></param>
    /// <exception cref="ArgumentNullException"></exception>
    public void CopyTo(HtmlAttributes htmlAttributes, bool mergeIdAndProperties = false, bool shared = true)
    {
        ArgumentNullException.ThrowIfNull(htmlAttributes);
        // Add html htmlAttributes to the object
        if (!mergeIdAndProperties || Id != null)
        {
            htmlAttributes.Id = Id;
        }
        if (htmlAttributes.Classes is null)
        {
            htmlAttributes.Classes = shared ? Classes : Classes != null ? new (Classes) : null;
        }
        else if (Classes != null)
        {
            htmlAttributes.Classes.AddRange(Classes);
        }

        if (htmlAttributes.Properties is null)
        {
            htmlAttributes.Properties = shared ? Properties : Properties != null ? new (Properties) : null;
        }
        else if (Properties != null)
        {
            if (mergeIdAndProperties)
            {
                foreach (var prop in Properties)
                {
                    htmlAttributes.AddPropertyIfNotExist(prop.Key, prop.Value);
                }
            }
            else
            {
                htmlAttributes.Properties.AddRange(Properties);
            }
        }
    }
}
