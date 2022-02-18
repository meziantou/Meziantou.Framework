namespace Meziantou.Framework.Scheduling
{
    public sealed class Event
    {
        public string? Id { get; set; }
        public string? Summary { get; set; }
        public Organizer? Organizer { get; set; }
        public IList<Attendee> Attendees { get; } = new List<Attendee>();
        public DateTime Created { get; set; }
        public DateTime LastModified { get; set; }
        public DateTime DateTimeStamp { get; set; }
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public RecurrenceRule? RecurrenceRule { get; set; }
        public EventStatus Status { get; set; }
        public IDictionary<string, string> AdditionalProperties { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }
}
