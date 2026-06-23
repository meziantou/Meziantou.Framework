using Microsoft.AspNetCore.Mvc;

namespace Meziantou.AspNetCore.ServiceDefaults.Tests;

[ApiController]
[Route("json-tests")]
public sealed class JsonTestController : ControllerBase
{
    [HttpGet("payload")]
    public ActionResult<ServiceDefaultTests.JsonPayload> GetPayload()
    {
        return Ok(new ServiceDefaultTests.JsonPayload
        {
            SampleEnum = ServiceDefaultTests.Sample.Value1,
            SampleText = "test-value",
            Values = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["MyKey"] = "my-value",
            },
            NullableValue = null,
        });
    }
}
