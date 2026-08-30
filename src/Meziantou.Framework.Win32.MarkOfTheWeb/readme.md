# Mark of the Web

The Mark of the Web (MOTW) is a security feature in Windows that helps protect users from potentially unsafe content downloaded from the internet. Windows records the security zone a file came from in an NTFS alternate data stream named `Zone.Identifier`, alongside the URLs it was downloaded from.
When a user attempts to open a file with MOTW, Windows may display a warning message or restrict certain actions, such as running scripts or accessing certain features, to help prevent potential security risks.

## Usage

To add the Mark of the Web to a file, you can use the `MarkOfTheWeb` class provided in this library. Here's an example of how to use it:

```csharp
// Add the Mark of the Web to a file
MarkOfTheWeb.SetFileZone(path, UrlZone.Internet);

// Get the Mark of the Web zone of a file
var zone = MarkOfTheWeb.GetFileZone(path);

// Remove the Mark of the Web from a file
MarkOfTheWeb.RemoveFileZone(path);
```