using Microsoft.Extensions.Time.Testing;

namespace Meziantou.Framework.Tests;

[Collection("RelativeDateTests")] // because DateTimeService.Clock is static
public class RelativeDateTests
{
    [Fact]
    public void DefaultDate_ToString()
    {
        var timeProvider = new FakeTimeProvider();
        timeProvider.SetUtcNow(new DateTimeOffset(2018, 1, 1, 0, 0, 0, TimeSpan.Zero));
        Assert.Throws<ArgumentException>(() => new RelativeDate(default, timeProvider).ToString());
    }

    [Theory]
    [MemberData(nameof(RelativeDate_ToString_Data))]
    public void RelativeDate_ToString(string dateTimeStr, string nowStr, string expectedValueEn, string expectedValueFr)
    {
        var now = DateTimeOffset.Parse(nowStr, CultureInfo.InvariantCulture);
        var dateTime = DateTimeOffset.Parse(dateTimeStr, CultureInfo.InvariantCulture);

        var timeProvider = new FakeTimeProvider();
        timeProvider.SetUtcNow(now);
        var relativeDate = RelativeDate.Get(dateTime, timeProvider);
        var resultEn = relativeDate.ToString(format: null, CultureInfo.InvariantCulture);
        Assert.Equal(expectedValueEn, resultEn);
        Assert.NotEmpty(expectedValueFr);

#if !InvariantGlobalization
        var resultDe = relativeDate.ToString(format: null, CultureInfo.GetCultureInfo("de"));
        Assert.Equal(expectedValueEn, resultDe);

        var resultFr = relativeDate.ToString(format: null, CultureInfo.GetCultureInfo("fr"));
        Assert.Equal(expectedValueFr, resultFr);
#endif
    }

    public static IEnumerable<object[]> RelativeDate_ToString_Data
    {
        get
        {
            yield return new object[] { "2018/01/01 00:00:00Z", "2018/01/01 00:00:00Z", "now", "maintenant" };

            yield return new object[] { "2018/01/01 00:00:00Z", "2018/01/01 00:00:01Z", "one second ago", "il y a une seconde" };
            yield return new object[] { "2018/01/01 00:00:00Z", "2018/01/01 00:00:25Z", "25 seconds ago", "il y a 25 secondes" };
            yield return new object[] { "2018/01/01 00:00:00Z", "2018/01/01 00:01:00Z", "a minute ago", "il y a une minute" };
            yield return new object[] { "2018/01/01 00:00:00Z", "2018/01/01 00:10:00Z", "10 minutes ago", "il y a 10 minutes" };
            yield return new object[] { "2018/01/01 00:00:00Z", "2018/01/01 01:00:00Z", "an hour ago", "il y a une heure" };
            yield return new object[] { "2018/01/01 00:00:00Z", "2018/01/01 02:00:00Z", "2 hours ago", "il y a 2 heures" };
            yield return new object[] { "2018/01/01 00:00:00Z", "2018/01/02 00:00:00Z", "yesterday", "hier" };
            yield return new object[] { "2018/01/01 00:00:00Z", "2018/01/03 00:00:00Z", "2 days ago", "il y a 2 jours" };
            yield return new object[] { "2018/01/01 00:00:00Z", "2018/02/01 00:00:00Z", "one month ago", "il y a un mois" };
            yield return new object[] { "2018/01/01 00:00:00Z", "2018/04/01 00:00:00Z", "3 months ago", "il y a 3 mois" };
            yield return new object[] { "2018/01/01 00:00:00Z", "2019/01/01 00:00:00Z", "one year ago", "il y a un an" };
            yield return new object[] { "2018/01/01 00:00:00Z", "2021/01/01 00:00:00Z", "3 years ago", "il y a 3 ans" };

            yield return new object[] { "2018/01/01 00:00:01Z", "2018/01/01 00:00:00Z", "in one second", "dans une seconde" };
            yield return new object[] { "2018/01/01 00:00:25Z", "2018/01/01 00:00:00Z", "in 25 seconds", "dans 25 secondes" };
            yield return new object[] { "2018/01/01 00:10:00Z", "2018/01/01 00:00:00Z", "in 10 minutes", "dans 10 minutes" };
            yield return new object[] { "2018/01/01 01:00:00Z", "2018/01/01 00:00:00Z", "in an hour", "dans une heure" };
            yield return new object[] { "2018/01/01 00:01:00Z", "2018/01/01 00:00:00Z", "in a minute", "dans une minute" };
            yield return new object[] { "2018/01/01 02:00:00Z", "2018/01/01 00:00:00Z", "in 2 hours", "dans 2 heures" };
            yield return new object[] { "2018/01/02 00:00:00Z", "2018/01/01 00:00:00Z", "tomorrow", "demain" };
            yield return new object[] { "2018/01/03 00:00:00Z", "2018/01/01 00:00:00Z", "in 2 days", "dans 2 jours" };
            yield return new object[] { "2018/02/01 00:00:00Z", "2018/01/01 00:00:00Z", "in one month", "dans un mois" };
            yield return new object[] { "2018/04/01 00:00:00Z", "2018/01/01 00:00:00Z", "in 3 months", "dans 3 mois" };
            yield return new object[] { "2019/01/01 00:00:00Z", "2018/01/01 00:00:00Z", "in one year", "dans un an" };
            yield return new object[] { "2021/01/01 00:00:00Z", "2018/01/01 00:00:00Z", "in 3 years", "dans 3 ans" };
        }
    }
}
