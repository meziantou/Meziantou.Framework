using System;
using Xunit;

namespace TestUtilities
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class RunIfNotOnCIFactAttribute : FactAttribute
    {
        public RunIfNotOnCIFactAttribute()
        {
            if (IsOnCI())
            {
                Skip = "Skip on CI";
            }
        }

        public static bool IsOnCI()
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BUILD_DEFINITIONNAME")))
                return true;

            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS")))
                return true;

            return false;
        }
    }
}
