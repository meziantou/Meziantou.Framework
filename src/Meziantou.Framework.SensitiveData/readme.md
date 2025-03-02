# Meziantou.Framework.SensitiveData

`Meziantou.Framework.SensitiveData` provides the `SensitiveData` class. This class represent sensitive data which should be difficult to accidentally disclose. But there's no effort to thwart *intentional* disclosure of these contents, such as through a debugger or memory dump utility.

````c#
using var secret = SensitiveData.Create<string>("secret");

// Reveal data
byte[] data = secret.RevealToArray();
string str = secret.RevealToString();

var buffer = new byte[10];
secret.RevealInto(buffer);
````

# Additional resources

- [Prevent accidental disclosure of configuration secrets](https://www.meziantou.net/prevent-accidental-disclosure-of-configuration-secrets.htm)
