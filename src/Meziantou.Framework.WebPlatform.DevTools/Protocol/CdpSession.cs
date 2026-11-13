using System.Text.Json;
using System.Text.Json.Nodes;

namespace Meziantou.Framework.WebPlatform.DevTools.Protocol;

/// <summary>
/// A session of a connection: the browser session (the messages without <c>sessionId</c>), or a session attached to a target
/// (<c>Target.attachToTarget</c> or the auto-attachment of the targets, with flattened sessions).
/// </summary>
internal abstract class CdpSession(CdpConnection connection, string? id)
{
    public CdpConnection Connection { get; } = connection;

    /// <summary>Gets the identifier of the session (the <c>sessionId</c> of its messages), or null for the browser session.</summary>
    public string? Id { get; } = id;

    public bool IsDetached { get; private set; }

    /// <summary>
    /// Runs a command. The implementations start the work synchronously (e.g. they post it to the thread of the browser context)
    /// before they yield: the commands of a session start in the order the client sent them.
    /// </summary>
    public abstract Task<JsonObject?> InvokeAsync(string method, JsonElement parameters);

    /// <summary>
    /// Runs a command and sends its response. The response of a command must not be sent after the events that the next
    /// operations of its thread send: the sessions that run their commands on another thread send the responses from it.
    /// </summary>
    public virtual async Task DispatchAsync(CdpMessage message)
    {
        JsonObject? result;
        try
        {
            result = await InvokeAsync(message.Method, message.Parameters).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // The errors of the commands are reported to the client
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Connection.Fail(message, ex);
            return;
        }

        Connection.Complete(message, result);
    }

    public void SendEvent(string method, JsonObject parameters)
    {
        if (!IsDetached)
        {
            Connection.SendEvent(Id, method, parameters);
        }
    }

    public void MarkDetached() => IsDetached = true;
}
