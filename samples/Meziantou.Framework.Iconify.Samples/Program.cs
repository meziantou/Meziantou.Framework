// The examples of src/Meziantou.Framework.Iconify/readme.md and docs/Iconify.
//   dotnet run --project samples/Meziantou.Framework.Iconify.Samples
using Meziantou.Framework;

#region Iconify_StaticIcon
IconifyIcon home = Iconify.Mdi.Icons.Home;
Console.WriteLine(home.Name);   // home
Console.WriteLine(home.Width);  // 24
Console.WriteLine(home.Body);   // <path fill="currentColor" d="..."/>
Console.WriteLine(home.Svg);    // <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24">...</svg>
#endregion

#region Iconify_IconSetInfo
var info = Iconify.Mdi.Info;
Console.WriteLine($"{info?.Name} by {info?.AuthorName}, {info?.LicenseSpdx}"); // Material Design Icons by Pictogrammers, Apache-2.0
#endregion
