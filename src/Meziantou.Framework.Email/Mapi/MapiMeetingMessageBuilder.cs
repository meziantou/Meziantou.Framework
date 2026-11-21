using Meziantou.Framework.Email.Internals;

namespace Meziantou.Framework.Email.Mapi;

/// <summary>
/// A meeting message ([MS-OXOCAL] 2.2.6) to add to a folder of a <see cref="PstWriter"/> or to save as an .msg file: an
/// invitation or a cancellation, whose recipients are the attendees, or the response of an attendee, whose recipient is the
/// organizer. The properties of the meeting are the ones of an appointment, and <see cref="MapiAppointmentBuilder.Uid"/>
/// identifies it.
/// </summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../../tests/Meziantou.Framework.Email.Tests/CalendarTests.cs" ref:region="MapiMeetingMessageBuilder_Usage" ref:dedent="true">
/// var invitation = VCalendar.Parse("""
///     BEGIN:VCALENDAR
///     VERSION:2.0
///     PRODID:-//Example//Example//EN
///     METHOD:REQUEST
///     BEGIN:VEVENT
///     UID:meeting-42@example.com
///     SEQUENCE:1
///     DTSTAMP:20260301T080000Z
///     DTSTART:20260302T090000Z
///     DTEND:20260302T100000Z
///     SUMMARY:Project review
///     LOCATION:Room 1
///     ORGANIZER;CN=Alice:mailto:alice@example.com
///     ATTENDEE;CN=Bob;ROLE=REQ-PARTICIPANT:mailto:bob@example.com
///     END:VEVENT
///     END:VCALENDAR
///     """)[0];
///
/// // The invitation as a meeting request of Outlook
/// var request = Assert.IsType&lt;MapiMeetingMessage&gt;(MsgFile.Load(MsgFile.ToByteArray(invitation.ToMapiMeetingMessageBuilder())));
/// Assert.Equal(MapiMeetingMessageType.Request, request.MeetingMessageType);
/// Assert.Equal("IPM.Schedule.Meeting.Request", request.MessageClass);
/// Assert.Equal("meeting-42@example.com", request.Uid);
/// Assert.Equal("alice@example.com", request.SenderEmailAddress);
/// Assert.Equal("bob@example.com", request.Recipients.Single().EmailAddress);
///
/// // And back as an invitation
/// var calendar = new VCalendar();
/// var meeting = calendar.AddMeetingMessage(request);
/// Assert.Equal("REQUEST", calendar.Method);
/// Assert.Equal("meeting-42@example.com", meeting.Uid);
/// Assert.Equal(1, meeting.Sequence);
/// </code>
/// </example>
public sealed class MapiMeetingMessageBuilder : MapiAppointmentBuilder
{
    // [MS-OXOCAL] 2.2.1.10: asfMeeting, asfReceived and asfCanceled
    private const int MeetingFlag = 0x1;
    private const int CanceledFlag = 0x4;

    /// <summary>Initializes a new instance of the <see cref="MapiMeetingMessageBuilder"/> class.</summary>
    /// <param name="meetingMessageType">The type of the message.</param>
    public MapiMeetingMessageBuilder(MapiMeetingMessageType meetingMessageType)
        : base(MapiMeetingMessage.GetMessageClass(meetingMessageType))
    {
        MeetingMessageType = meetingMessageType;
        Properties.SetValue(NamedProperties.AppointmentStateFlags, meetingMessageType == MapiMeetingMessageType.Cancellation ? MeetingFlag | CanceledFlag : MeetingFlag);
        switch (meetingMessageType)
        {
            case MapiMeetingMessageType.Request:
                // [MS-OXOCAL] 2.2.6.5: mtgRequest
                Properties.SetValue(NamedProperties.MeetingType, 0x1);
                break;
            case MapiMeetingMessageType.Cancellation:
                BusyStatus = MapiBusyStatus.Free;
                break;
            default:
                // [MS-OXOCAL] 2.2.1.11: respTentative, respAccepted, respDeclined
                Properties.SetValue(NamedProperties.ResponseStatus, meetingMessageType switch
                {
                    MapiMeetingMessageType.Tentative => 2,
                    MapiMeetingMessageType.Accepted => 3,
                    _ => 4,
                });
                break;
        }
    }

    /// <summary>Gets the type of the message.</summary>
    public MapiMeetingMessageType MeetingMessageType { get; }
}
