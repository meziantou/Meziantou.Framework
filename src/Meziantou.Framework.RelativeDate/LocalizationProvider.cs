using System.Globalization;

namespace Meziantou.Framework;

public sealed class LocalizationProvider : ILocalizationProvider
{
    private static ILocalizationProvider s_current = new LocalizationProvider();

    public static ILocalizationProvider Current
    {
        get => s_current;
        set => s_current = value ?? throw new ArgumentNullException(nameof(value));
    }

    private readonly Dictionary<CultureInfo, IReadOnlyDictionary<string, string>> _cultures;

    public LocalizationProvider()
    {
        var cultures = new Dictionary<CultureInfo, IReadOnlyDictionary<string, string>>
        {
            [CultureInfo.InvariantCulture] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "Now", "now" },
                { "OneSecondAgo", "one second ago" },
                { "ManySecondsAgo", "{0} seconds ago" },
                { "AMinuteAgo", "a minute ago" },
                { "ManyMinutesAgo", "{0} minutes ago" },
                { "AnHourAgo", "an hour ago" },
                { "ManyHoursAgo", "{0} hours ago" },
                { "Yesterday", "yesterday" },
                { "ManyDaysAgo", "{0} days ago" },
                { "OneMonthAgo", "one month ago" },
                { "ManyMonthsAgo", "{0} months ago" },
                { "OneYearAgo", "one year ago" },
                { "ManyYearsAgo", "{0} years ago" },

                { "InOneSecond", "in one second" },
                { "InManySeconds", "in {0} seconds" },
                { "InAMinute", "in a minute" },
                { "InManyMinutes", "in {0} minutes" },
                { "InAnHour", "in an hour" },
                { "InManyHours", "in {0} hours" },
                { "Tomorrow", "tomorrow" },
                { "InManyDays", "in {0} days" },
                { "InOneMonth", "in one month" },
                { "InManyMonths", "in {0} months" },
                { "InOneYear", "in one year" },
                { "InManyYears", "in {0} years" },
            },
        };

        var fr = GetCulture("fr");
        if (fr != null)
        {
            cultures.Add(fr, new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "Now", "maintenant" },
                { "OneSecondAgo", "il y a une seconde" },
                { "ManySecondsAgo", "il y a {0} secondes" },
                { "AMinuteAgo", "il y a une minute" },
                { "ManyMinutesAgo", "il y a {0} minutes" },
                { "AnHourAgo", "il y a une heure" },
                { "ManyHoursAgo", "il y a {0} heures" },
                { "Yesterday", "hier" },
                { "ManyDaysAgo", "il y a {0} jours" },
                { "OneMonthAgo", "il y a un mois" },
                { "ManyMonthsAgo", "il y a {0} mois" },
                { "OneYearAgo", "il y a un an" },
                { "ManyYearsAgo", "il y a {0} ans" },

                { "InOneSecond", "dans une seconde" },
                { "InManySeconds", "dans {0} secondes" },
                { "InAMinute", "dans une minute" },
                { "InManyMinutes", "dans {0} minutes" },
                { "InAnHour", "dans une heure" },
                { "InManyHours", "dans {0} heures" },
                { "Tomorrow", "demain" },
                { "InManyDays", "dans {0} jours" },
                { "InOneMonth", "dans un mois" },
                { "InManyMonths", "dans {0} mois" },
                { "InOneYear", "dans un an" },
                { "InManyYears", "dans {0} ans" },
            });
        }

        _cultures = cultures;

        static CultureInfo? GetCulture(string name)
        {
            try
            {
                return CultureInfo.GetCultureInfo(name);
            }
            catch
            {
                return null;
            }
        }
    }

    public string GetString(string name, CultureInfo? culture)
    {
        culture ??= CultureInfo.InvariantCulture;

        if (!_cultures.TryGetValue(culture, out var values))
        {
            if (culture == null || culture.IsNeutralCulture || culture == culture.Parent)
                return GetString(name, CultureInfo.InvariantCulture);

            return GetString(name, culture.Parent);
        }

        if (values.TryGetValue(name, out var value))
            return value;

        throw new ArgumentException($"'{name}' is not supported", nameof(name));
    }

    public void Set(CultureInfo culture, IReadOnlyDictionary<string, string> values)
    {
        if (culture is null)
            throw new ArgumentNullException(nameof(culture));

        if (values is null)
            throw new ArgumentNullException(nameof(values));

        _cultures[culture] = values;
    }

    public void Remove(CultureInfo culture)
    {
        _cultures.Remove(culture);
    }
}
