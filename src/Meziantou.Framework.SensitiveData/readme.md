# Meziantou.Framework.SensitiveData

`Meziantou.Framework.SensitiveData` provides the `SensitiveData` class. This class represent sensitive data which should be difficult to accidentally disclose. But there's no effort to thwart *intentional* disclosure of these contents, such as through a debugger or memory dump utility.

<!-- ref:../../tests/Meziantou.Framework.SensitiveData.Tests/SensitiveDataTests.cs;region=SensitiveData_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
// Create sensitive data from a string
using var secret = SensitiveData.Create("secret");

// Reveal the data
string str = secret.RevealToString();
char[] chars = secret.RevealToArray();
Assert.Equal("secret", str);
Assert.Equal("secret".ToCharArray(), chars);

var buffer = new char[secret.GetLength()];
secret.RevealInto(buffer);

// Or use it without keeping a copy of your own
var lengths = new List<int>();
secret.RevealAndUse(arg: lengths, static (span, list) => list.Add(span.Length));
Assert.Equal([6], lengths);

// Create sensitive data from a buffer of any unmanaged type
using var key = SensitiveData.Create(new byte[] { 1, 2, 3, 4, 5 });
byte[] revealedKey = key.RevealToArray();
Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, revealedKey);
```
<!-- endref -->

# Additional resources

- [Prevent accidental disclosure of configuration secrets](https://www.meziantou.net/prevent-accidental-disclosure-of-configuration-secrets.htm)
