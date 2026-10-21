# Meziantou.Framework.Win32.Amsi

`Meziantou.Framework.Win32.Amsi` is a .NET wrapper for the [Antimalware Scan Interface (AMSI)](https://learn.microsoft.com/en-us/windows/win32/amsi/antimalware-scan-interface-portal?WT.mc_id=DT-MVP-5003978). AMSI lets an application submit content to the antimalware product installed on the machine before acting on it, which is useful when the application evaluates scripts, macros, or uploaded files.

## Requirements

- Windows 10 / Windows Server 2016 or later
- An antimalware provider registered with AMSI. When no provider is registered, scans succeed and report the content as not detected.

## Usage

Create a context per application and scan content with it (the types are in the `Meziantou.Framework.Win32`
namespace). The examples scan the EICAR test string, which antimalware products
detect as malware:

<!-- ref:../../tests/Meziantou.Framework.Win32.AmsiTests/AmsiContextTests.cs;region=AmsiContext_IsMalwareString;format=md-fenced-code-block;dedent=true -->
```csharp
using var context = AmsiContext.Create("MyApplication");

var isMalware = context.IsMalware(@"X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*", "test.txt");
Assert.True(isMalware);
```
<!-- endref -->

Byte buffers are supported too, such as the content of an uploaded file:

<!-- ref:../../tests/Meziantou.Framework.Win32.AmsiTests/AmsiContextTests.cs;region=AmsiContext_IsMalwareBuffer;format=md-fenced-code-block;dedent=true -->
```csharp
var payload = Encoding.ASCII.GetBytes(@"X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*");
Assert.True(context.IsMalware(payload, "document.txt"));
Assert.False(context.IsMalware([0, 0, 0, 0], "document.bin"));
```
<!-- endref -->

When several scans belong together — the fragments of one script, or the parts of one upload — open a session so the provider can correlate them:

<!-- ref:../../tests/Meziantou.Framework.Win32.AmsiTests/AmsiContextTests.cs;region=AmsiContext_Session;format=md-fenced-code-block;dedent=true -->
```csharp
string[] fragments = ["Write-Host 'Hello'", @"X5O!P%@AP[4\PZX54(P^)7CC)7}$EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*"];

using var context = AmsiContext.Create("MyApplication");
using var session = context.CreateSession();

var detected = false;
foreach (var fragment in fragments)
{
    if (session.IsMalware(fragment, "script.ps1"))
    {
        detected = true;
        break;
    }
}

Assert.True(detected);
```
<!-- endref -->

Dispose the session and the context when you are done with them.

# Additional resources

- [Antimalware Scan Interface (AMSI)](https://learn.microsoft.com/en-us/windows/win32/amsi/antimalware-scan-interface-portal?WT.mc_id=DT-MVP-5003978)
- [Using Windows Antimalware Scan Interface in .NET](https://www.meziantou.net/using-windows-antimalware-scan-interface-in-dotnet.htm)
