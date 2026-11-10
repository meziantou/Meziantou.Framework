namespace Meziantou.Framework.JavaScriptEngine.Samples;

#region JavaScriptEngine_CoverageDebugger
/// <summary>Records the lines that run: the code coverage of the scripts.</summary>
public sealed class CoverageDebugger : JsDebugger
{
    public HashSet<(string? Url, int Line)> CoveredLines { get; } = [];

    protected override void OnStatement(JsDebugEvent debugEvent)
    {
        var location = debugEvent.Location;
        CoveredLines.Add((location.Url, location.Line));
    }
}
#endregion
