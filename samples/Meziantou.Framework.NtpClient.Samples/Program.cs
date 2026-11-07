using Meziantou.Framework.Ntp.Samples;

// The examples of src/Meziantou.Framework.NtpClient/readme.md query public NTP servers, so CI compiles them but does
// not run them. Run the first one with: dotnet run --project samples/Meziantou.Framework.NtpClient.Samples
await ReadmeExamples.QueryAsync();
