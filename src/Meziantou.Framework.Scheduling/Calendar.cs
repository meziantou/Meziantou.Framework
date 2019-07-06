using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Meziantou.Framework.Scheduling
{
    public class Calendar
    {
        public IDictionary<string, string> AdditionalProperties { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public IList<Event> Events { get; } = new List<Event>();
        public string Version { get; set; } = "2.0";

        public void ToIcs(Stream stream)
        {
            var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            using TextWriter writer = new StreamWriter(stream, encoding, bufferSize: 1024, leaveOpen: true);
            ToIcs(writer);
        }

        public void ToIcs(TextWriter writer)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));
            /*
            BEGIN:VCALENDAR

            VERSION:2.0
            PRODID:LUCCA.FIGGO
            METHOD:REQUEST

            BEGIN:VEVENT
            STATUS:CONFIRMED
            ORGANIZER:MAILTO:sflogshosting@softfluent.com
            ATTENDEE:MAILTO:gba@softfluent.com
            CREATED:20141208T100900Z
            DTSTAMP:20141208T100900Z
            LAST-MODIFIED:19960817T133000Z
            SUMMARY:Absent
            DESCRIPTION:\n
            DTSTART:20150102T080000
            DTEND:20150109T200000
            UID:-softfluent-ilucca-net-figgo_26_42006-0800_42013-2000
            X-MICROSOFT-CDO-BUSYSTATUS:OOF
            X-MICROSOFT-CDO-ALLDAYEVENT:1
            END:VEVENT

            END:VCALENDAR
            */

            writer.WriteLine("BEGIN:VCALENDAR");
            if (!string.IsNullOrEmpty(Version))
                writer.WriteLine("VERSION:" + Version);

            foreach (var additionalProperty in AdditionalProperties)
            {
                if (string.IsNullOrEmpty(additionalProperty.Key))
                    continue;

                writer.Write(additionalProperty.Key);
                writer.Write(":");
                writer.WriteLine(additionalProperty.Value);
            }

            foreach (var @event in Events)
            {
                writer.WriteLine("BEGIN:VEVENT");
                if (!string.IsNullOrEmpty(@event.Id))
                    writer.WriteLine("UID:" + @event.Id);

                writer.WriteLine("STATUS:" + Utilities.StatusToString(@event.Status));
                if (@event.Organizer.Address != null)
                    writer.WriteLine("ORGANIZER:" + @event.Organizer.Address);

                foreach (var attendee in @event.Attendees)
                {
                    if (attendee == null)
                        continue;

                    writer.WriteLine("ATTENDEE:" + attendee.Address);
                }

                writer.WriteLine("CREATED:" + Utilities.DateTimeToString(@event.Created));
                writer.WriteLine("LAST-MODIFIED:" + Utilities.DateTimeToString(@event.LastModified));
                writer.WriteLine("DTSTAMP:" + Utilities.DateTimeToString(@event.DateTimeStamp));
                writer.WriteLine("DTSTART:" + Utilities.DateTimeToString(@event.Start));
                writer.WriteLine("DTEND:" + Utilities.DateTimeToString(@event.End));
                if (@event.RecurrenceRule != null)
                    writer.WriteLine("RRULE:" + @event.RecurrenceRule.Text);

                if (!string.IsNullOrEmpty(@event.Summary))
                    writer.WriteLine("SUMMARY:" + @event.Summary);

                foreach (var additionalProperty in @event.AdditionalProperties)
                {
                    if (string.IsNullOrEmpty(additionalProperty.Key))
                        continue;

                    writer.Write(additionalProperty.Key);
                    writer.Write(":");
                    writer.WriteLine(additionalProperty.Value);
                }

                writer.WriteLine("DESCRIPTION:\\n");
                writer.WriteLine("END:VEVENT");
            }

            writer.WriteLine("END:VCALENDAR");
        }

        public string ToIcs()
        {
            using var writer = new StringWriter();
            ToIcs(writer);
            return writer.ToString();
        }
    }
}
