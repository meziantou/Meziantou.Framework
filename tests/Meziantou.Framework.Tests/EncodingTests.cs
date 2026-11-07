namespace Meziantou.Framework.Tests;
public sealed class EncodingTests
{
    [Fact]
    public void Documentation_Utf8WithoutPreamble()
    {
        #region EncodingExtensions_UTF8WithoutPreamble
        var path = Path.GetTempFileName();

        // Encoding.UTF8 would write the byte order mark (EF BB BF) before the text
        File.WriteAllText(path, "hello", Encoding.UTF8WithoutPreamble);
        Assert.Equal("hello"u8.ToArray(), File.ReadAllBytes(path));

        File.Delete(path);
        #endregion
    }

    [Fact]
    public void Documentation_EnvironmentVariables()
    {
        #region EnvironmentExtensions_Usage
        // Static extension members of Environment
        var logLevel = Environment.GetEnvironmentVariableOrDefault("SAMPLE_APP_LOG_LEVEL", "Information");
        Assert.Equal("Information", logLevel);

        // Throws an InvalidOperationException that names the missing variable
        Assert.Throws<InvalidOperationException>(() => Environment.GetRequiredEnvironmentVariable("SAMPLE_APP_API_KEY"));
        #endregion
    }

    [Fact]
    public static void Utf8WithoutPreambleTest()
    {
        var encoding = Encoding.UTF8WithoutPreamble;
        Assert.Empty(encoding.Preamble);
    }
}
