namespace Meziantou.Framework.InlineSnapshotTesting.Samples;

public sealed class HttpContent_format_json_response
{
    [Fact]
    public void Demo()
    {
        using var httpContent = new StringContent("""{"foo":"bar","answer":42}""", encoding: null, "application/json");

        // Json content is automatically indented
        InlineSnapshot.Validate(httpContent,
            """
            Headers:
              Content-Type: application/json; charset=utf-8
            Value:
              {
                "foo": "bar",
                "answer": 42
              }
            """);
    }
}