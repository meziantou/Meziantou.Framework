using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Meziantou.Framework.InlineSnapshotTesting;

internal record struct CallerContext(string FilePath, int LineNumber, int ColumnNumber, string MethodName, string? ParameterName, int ParameterIndex)
{
    /// <summary>
    /// Newer Roslyn versions use the format "&lt;callerName&gt;g__functionName|x_y".
    /// Older versions use "&lt;callerName&gt;g__functionNamex_y".
    /// </summary>
    /// <see href="https://github.com/dotnet/roslyn/blob/aecd49800750d64e08767836e2678ffa62a4647f/src/Compilers/CSharp/Portable/Symbols/Synthesized/GeneratedNames.cs#L109" />
    [SuppressMessage("Security", "MA0009:Add regex evaluation timeout")]
    private static readonly Regex FunctionNameRegex = new(@"^<(.*)>g__(?<name>[^\|]*)\|{0,1}\d+(_\d+)?$", RegexOptions.Compiled | RegexOptions.ExplicitCapture);

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static CallerContext Get(InlineSnapshotSettings settings, string? filePath, int lineNumber)
    {
        string? methodName = null;
        string? parameterName = null;
        var parameterIndex = -1;

        var stackTrace = new StackTrace(fNeedFileInfo: true);
        StackFrame? callerFrame = null;
        for (var i = stackTrace.FrameCount - 1; i >= 0; i--)
        {
            var frame = stackTrace.GetFrame(i);
            if (frame == null)
                continue;

            var methodInfo = frame.GetMethod();
            if (methodInfo == null)
                continue;

            var attribute = methodInfo.GetCustomAttribute<InlineSnapshotAssertionAttribute>();
            if (attribute == null)
                continue;

            methodName = methodInfo.Name;
            if (ParseLocalFunctionName(methodName, out var localFunctionName))
            {
                methodName = localFunctionName;
            }

            parameterName = attribute.ParameterName;
            if (parameterName != null)
            {
                var parameters = methodInfo.GetParameters();
                for (var j = 0; j < parameterName.Length; j++)
                {
                    if (parameters[j].Name == parameterName)
                    {
                        parameterIndex = j;
                        break;
                    }
                }
            }

            callerFrame = stackTrace.GetFrame(i + 1);
            break;
        }

        if (callerFrame == null)
            throw new InlineSnapshotException($"Cannot find the method to update in the call stack. Be sure at least one method from the stack is decorated with '{nameof(InlineSnapshotAssertionAttribute)}'.");

        var pdbFileName = callerFrame.GetFileName();
        if (settings.ValidateSourceFilePathUsingPdbInfoWhenAvailable && pdbFileName != null && filePath != null && pdbFileName != filePath)
            throw new InlineSnapshotException($"The call stack doesn't match the file to update. From call stack: {pdbFileName}; From CallerFilePath: {filePath}");

        var pdbLine = callerFrame.GetFileLineNumber();
        if (settings.ValidateLineNumberUsingPdbInfoWhenAvailable && pdbLine != 0 && pdbLine != lineNumber)
            throw new InlineSnapshotException($"The call stack does not match the line to update. From call stack: {pdbLine}; From CallerLineNumber: {lineNumber}");

        filePath ??= pdbFileName;
        var column = callerFrame.GetFileColumnNumber();

        if (filePath == null)
            throw new InlineSnapshotException("Cannot find the file to update from the call stack.");

        if (methodName == null)
            throw new InlineSnapshotException("Cannot find the method to update from the call stack.");

        return new CallerContext(filePath, lineNumber, column, methodName, parameterName, parameterIndex);
    }

    internal static bool ParseLocalFunctionName(string name, [NotNullWhen(true)] out string? functionName)
    {
        functionName = null;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        var match = FunctionNameRegex.Match(name);
        functionName = match.Groups["name"].Value;
        return match.Success;
    }
}

