using System.Diagnostics;
using System.Reflection;

namespace Meziantou.Framework.InlineSnapshotTesting.SnapshotUpdateStrategies;

internal sealed record PromptContext(string FilePath, string? TestName, ProcessInfo? ParentProcessInfo)
{
    public string? ProcessName => ParentProcessInfo?.ProcessName;
    public int? ProcessId => ParentProcessInfo?.ProcessId;

    internal static PromptContext Get(string filePath)
    {
        var processInfo = ProcessInfo.GetContextProcess();
        var testName = GetTestName();
        return new PromptContext(filePath, testName, processInfo);
    }

    private static string? GetTestName()
    {
        return TestNameFromNunit()
            ?? TestNameFromXunit();

        static string? TestNameFromNunit()
        {
            var testContextType = Type.GetType("NUnit.Framework.TestContext, Nunit.Framework", throwOnError: false);
            if (testContextType != null)
            {
                var currentContextProperty = testContextType.GetProperty("CurrentContext", BindingFlags.Static | BindingFlags.Public);
                if (currentContextProperty != null)
                {
                    var context = currentContextProperty.GetValue(obj: null);
                    if (context is not null)
                    {
                        var testProperty = currentContextProperty.PropertyType.GetProperty("Test", BindingFlags.Public | BindingFlags.Instance);
                        if (testProperty != null)
                        {
                            var test = testProperty.GetValue(context);
                            if (test is not null)
                            {
                                var nameProperty = testProperty.PropertyType.GetProperty("FullName", BindingFlags.Public | BindingFlags.Instance);
                                if (nameProperty != null)
                                {
                                    if (nameProperty.GetValue(test) is string name)
                                        return name;
                                }
                            }
                        }
                    }
                }
            }

            return null;
        }

        static string? TestNameFromXunit()
        {
            var factType = Type.GetType("Xunit.FactAttribute, xunit.core", throwOnError: false);
            var theoryType = Type.GetType("Xunit.TheoryAttribute, xunit.core", throwOnError: false);
            if (factType == null && theoryType == null)
                return null;

            var stackTrace = new StackTrace(fNeedFileInfo: true);
            for (var i = 0; i < stackTrace.FrameCount; i++)
            {
                var frame = stackTrace.GetFrame(i);
                if (frame is null)
                    continue;

                var method = frame.GetMethod();
                if (method == null)
                    continue;

                if (factType != null && method.GetCustomAttribute(factType, inherit: true) is not null)
                    return method.Name;

                if (theoryType != null && method.GetCustomAttribute(theoryType, inherit: true) is not null)
                    return method.Name;
            }

            return null;
        }
    }
}
