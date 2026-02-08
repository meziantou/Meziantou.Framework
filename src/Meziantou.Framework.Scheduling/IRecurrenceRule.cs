namespace Meziantou.Framework.Scheduling;

public interface IRecurrenceRule
{
    IEnumerable<DateTime> GetNextOccurrences(DateTime startDate);
}
