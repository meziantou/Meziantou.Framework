using System;
using Xunit;

namespace TestUtilities
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class RunIfNotOnAzurePipelineFactAttribute : FactAttribute
    {
        public RunIfNotOnAzurePipelineFactAttribute()
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BUILD_DEFINITIONNAME")))
            {
                Skip = "Skip on Azure Pipeline";
            }
        }
    }
}
