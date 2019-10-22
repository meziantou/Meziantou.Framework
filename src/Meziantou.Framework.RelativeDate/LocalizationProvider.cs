﻿using System;
using System.Collections.Generic;
using System.Globalization;

namespace Meziantou.Framework
{
    public class LocalizationProvider : ILocalizationProvider
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
            _cultures = new Dictionary<CultureInfo, IReadOnlyDictionary<string, string>>
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
                },

                [CultureInfo.GetCultureInfo("fr")] = new Dictionary<string, string>(StringComparer.Ordinal)
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
                },
            };
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
            if (culture == null)
                throw new ArgumentNullException(nameof(culture));

            _cultures[culture] = values ?? throw new ArgumentNullException(nameof(values));
        }

        public void Remove(CultureInfo culture)
        {
            _cultures.Remove(culture);
        }
    }
}
