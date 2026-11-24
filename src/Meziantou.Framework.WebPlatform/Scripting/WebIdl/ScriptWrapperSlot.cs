namespace Meziantou.Framework.WebPlatform.Scripting;

/// <summary>A script object of a platform object, and the agent that created it (see <see cref="IScriptWrapperHolder"/>).</summary>
internal sealed class ScriptWrapperSlot(object agent, object wrapper)
{
    public object Agent { get; } = agent;

    public object Wrapper { get; } = wrapper;
}
