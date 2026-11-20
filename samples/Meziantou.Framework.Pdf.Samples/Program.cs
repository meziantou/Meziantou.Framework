// The examples of docs/Pdf that download files, so CI compiles them but does not run them.
//   dotnet run --project samples/Meziantou.Framework.Pdf.Samples -- https://example.com/report.pdf
using Meziantou.Framework.Pdf;

var uri = new Uri(args.Length > 0 ? args[0] : "https://example.com/report.pdf");

#region Pdf_OpenRemote
// The client sends the range requests: it must stay usable while the document is read
using var httpClient = new HttpClient();

// Only the parts of the file the document reads are downloaded
var document = await PdfDocument.OpenAsync(httpClient, uri);

// Download what a page needs before reading it, so that reading it sends no request
await document.PrefetchPageAsync(0);
var text = document.Pages[0].ExtractText();
#endregion

Console.WriteLine(text);
