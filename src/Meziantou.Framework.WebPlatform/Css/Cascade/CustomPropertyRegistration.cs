namespace Meziantou.Framework.WebPlatform.Css.Cascade;

/// <summary>
/// A custom property registration (https://drafts.css-houdini.org/css-properties-values-api/#custom-property-registration),
/// from <c>CSS.registerProperty()</c> or from a <c>@property</c> rule.
/// </summary>
/// <param name="InitialValue">The specified initial value (computed for each element), or null for the guaranteed-invalid value.</param>
internal sealed record CustomPropertyRegistration(string Name, CustomPropertySyntax Syntax, bool Inherits, string? InitialValue);
