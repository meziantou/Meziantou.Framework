# Meziantou.Framework.ValueStringBuilder

`Meziantou.Framework.ValueStringBuilder` provides a high-performance `ValueStringBuilder` as a `ref struct`, inspired by the .NET runtime implementation.

````c#
Span<char> initialBuffer = stackalloc char[64];
var sb = new ValueStringBuilder(initialBuffer);

sb.Append("Hello");
sb.Append(' ');
sb.Append("World");

string text = sb.ToString();
````
