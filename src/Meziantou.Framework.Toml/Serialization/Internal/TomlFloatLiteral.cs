namespace Meziantou.Framework.Toml.Serialization.Internal;

// A float written from its exact text (such as a Decimal128), which double cannot represent without losing digits
internal readonly record struct TomlFloatLiteral(string Text);
