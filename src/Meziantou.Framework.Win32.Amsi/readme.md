# Meziantou.Framework.Win32.Amsi

`Meziantou.Framework.Win32.Amsi` is a .NET wrapper for the [Antimalware Scan Interface (AMSI)](https://learn.microsoft.com/en-us/windows/win32/amsi/antimalware-scan-interface-portal?WT.mc_id=DT-MVP-5003978). AMSI lets an application submit content to the antimalware product installed on the machine before acting on it, which is useful when the application evaluates scripts, macros, or uploaded files.

## Requirements

- Windows 10 / Windows Server 2016 or later
- An antimalware provider registered with AMSI. When no provider is registered, scans succeed and report the content as not detected.

## Usage

Create a context per application and scan content with it:

```c#
using Meziantou.Framework.Win32;

using var context = AmsiContext.Create("MyApplication");

if (context.IsMalware(@"X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*", "test.txt"))
{
    Console.WriteLine("Malware detected!");
}
```

Byte buffers are supported too:

```c#
var payload = File.ReadAllBytes("document.docm");
if (context.IsMalware(payload, "document.docm"))
{
    Console.WriteLine("Malware detected!");
}
```

When several scans belong together — the fragments of one script, or the parts of one upload — open a session so the provider can correlate them:

```c#
using var context = AmsiContext.Create("MyApplication");
using var session = context.CreateSession();

foreach (var fragment in fragments)
{
    if (session.IsMalware(fragment, "script.ps1"))
    {
        Console.WriteLine("Malware detected!");
        break;
    }
}
```

Dispose the session and the context when you are done with them.

# Additional resources

- [Antimalware Scan Interface (AMSI)](https://learn.microsoft.com/en-us/windows/win32/amsi/antimalware-scan-interface-portal?WT.mc_id=DT-MVP-5003978)
- [Using Windows Antimalware Scan Interface in .NET](https://www.meziantou.net/using-windows-antimalware-scan-interface-in-dotnet.htm)
