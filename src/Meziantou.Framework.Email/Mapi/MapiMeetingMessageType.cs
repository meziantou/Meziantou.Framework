namespace Meziantou.Framework.Email.Mapi;

/// <summary>The type of a meeting message ([MS-OXOCAL] 2.2.6), and the <c>METHOD</c> of iCalendar it converts to.</summary>
public enum MapiMeetingMessageType
{
    /// <summary>An invitation or an update sent by the organizer (<c>IPM.Schedule.Meeting.Request</c>, <c>METHOD:REQUEST</c>).</summary>
    Request,

    /// <summary>A cancellation sent by the organizer (<c>IPM.Schedule.Meeting.Canceled</c>, <c>METHOD:CANCEL</c>).</summary>
    Cancellation,

    /// <summary>An attendee accepts (<c>IPM.Schedule.Meeting.Resp.Pos</c>, <c>METHOD:REPLY</c> with <c>PARTSTAT=ACCEPTED</c>).</summary>
    Accepted,

    /// <summary>An attendee tentatively accepts (<c>IPM.Schedule.Meeting.Resp.Tent</c>, <c>METHOD:REPLY</c> with <c>PARTSTAT=TENTATIVE</c>).</summary>
    Tentative,

    /// <summary>An attendee declines (<c>IPM.Schedule.Meeting.Resp.Neg</c>, <c>METHOD:REPLY</c> with <c>PARTSTAT=DECLINED</c>).</summary>
    Declined,
}
