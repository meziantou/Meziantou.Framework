using Meziantou.Framework.Email.Internals;

namespace Meziantou.Framework.Email.Mapi;

/// <summary>
/// A meeting message ([MS-OXOCAL] 2.2.6): an invitation, a cancellation or the response of an attendee. It has the properties of
/// the meeting it is about, as an appointment. <see cref="VCalendar.AddMeetingMessage"/> converts it to an iCalendar
/// invitation (iTIP), and <see cref="VCalendar.ToMapiMeetingMessageBuilder"/> converts an invitation back.
/// </summary>
public sealed class MapiMeetingMessage : MapiAppointment
{
    internal MapiMeetingMessage(MessageSource source, MapiPropertyCollection properties, int embeddingDepth, MapiMeetingMessageType meetingMessageType)
        : base(source, properties, embeddingDepth)
    {
        MeetingMessageType = meetingMessageType;
    }

    /// <summary>Gets the type of the message, from its class.</summary>
    public MapiMeetingMessageType MeetingMessageType { get; }

    internal static string GetMessageClass(MapiMeetingMessageType type) => type switch
    {
        MapiMeetingMessageType.Request => "IPM.Schedule.Meeting.Request",
        MapiMeetingMessageType.Cancellation => "IPM.Schedule.Meeting.Canceled",
        MapiMeetingMessageType.Accepted => "IPM.Schedule.Meeting.Resp.Pos",
        MapiMeetingMessageType.Tentative => "IPM.Schedule.Meeting.Resp.Tent",
        MapiMeetingMessageType.Declined => "IPM.Schedule.Meeting.Resp.Neg",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "The type of the meeting message is not valid."),
    };

    internal static MapiMeetingMessageType? GetMeetingMessageType(string messageClass)
    {
        foreach (var type in Enum.GetValues<MapiMeetingMessageType>())
        {
            if (IsClass(messageClass, GetMessageClass(type)))
            {
                return type;
            }
        }

        return null;
    }
}
