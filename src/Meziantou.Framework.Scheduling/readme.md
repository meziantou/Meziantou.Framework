# Meziantou.Framework.Scheduling

Parse Recurrence Rule as defined in RFC5545 and RFC2445

````c#
var rrule = "FREQ=DAILY;UNTIL=20000131T140000Z;BYMONTH=1";
if (RecurrenceRule.TryParse(rrule, out var rule, out var error))
{
    var nextOccurrences = rule.GetNextOccurrences(DateTime.Now).Take(50).ToArray();
}
````

Also, it can convert a recurrence rule to a human readable text:

````c#
var culture = CultureInfo.GetCultureInfo("en-US");
RecurrenceRule.Parse("FREQ=DAILY").GetHumanText(culture); // every day
RecurrenceRule.Parse("FREQ=WEEKLY;INTERVAL=3;BYDAY=TU;UNTIL=20150101").GetHumanText(culture); // every 3 weeks on Tuesday until January 1, 2015
````
