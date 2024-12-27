#pragma warning disable CA2000 // Dispose objects before losing scope
#pragma warning disable CA2234 // Pass system uri objects instead of strings
using System.Diagnostics;
using Meziantou.Framework.Http;

var loadingTime = Stopwatch.StartNew();
var policyCollection = new HstsDomainPolicyCollection();
Console.WriteLine("Data Loaded in " + loadingTime.ElapsedMilliseconds + "ms");

using var client = new HttpClient(new HstsClientHandler(new SocketsHttpHandler(), policyCollection), disposeHandler: true);
using var response = await client.GetAsync("http://apis.google.com").ConfigureAwait(false);

Console.WriteLine(response.RequestMessage.RequestUri);