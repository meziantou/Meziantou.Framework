# Meziantou.Framework.SensitiveData

`Meziantou.Framework.SensitiveData` provides the `SensitiveData` class. This class represent sensitive data which should be difficult to accidentally disclose. But there's no effort to thwart *intentional* disclosure of these contents, such as through a debugger or memory dump utility.

````c#
// Create sensitive data from a string
using var secret = SensitiveData.Create("secret");

// Reveal the data
string str = secret.RevealToString();
char[] chars = secret.RevealToArray();

var buffer = new char[secret.GetLength()];
secret.RevealInto(buffer);

// Or use it without keeping a copy of your own
secret.RevealAndUse(arg: Console.Out, static (span, output) => output.WriteLine(span.Length));

// Create sensitive data from a buffer of any unmanaged type
using var key = SensitiveData.Create(new byte[] { 1, 2, 3, 4, 5 });
byte[] revealedKey = key.RevealToArray();
````

# Additional resources

- [Prevent accidental disclosure of configuration secrets](https://www.meziantou.net/prevent-accidental-disclosure-of-configuration-secrets.htm)
