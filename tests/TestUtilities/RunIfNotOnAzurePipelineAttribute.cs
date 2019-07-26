using System;
using Xunit;

namespace TestUtilities
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class RunIfNotOnAzurePipelineAttribute : FactAttribute
    {
        public RunIfNotOnAzurePipelineAttribute()
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BUILD_DEFINITIONNAME")))
            {
                Skip = "Skip on Azure Pipeline";
            }
        }
    }
}
