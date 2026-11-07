// Luxon: dates in time zones (Intl.DateTimeFormat for the offsets), formatting with Intl, durations, intervals,
// parsing and relative times
import { DateTime, Duration, Interval, Info, Settings } from "luxon";

Settings.defaultZone = "utc";
Settings.defaultLocale = "en-US";
Settings.now = () => Date.UTC(2024, 2, 10, 12, 0, 0);

export default () => {
  const date = DateTime.fromISO("2024-03-10T01:30:00", { zone: "America/New_York" });
  const results = {
    iso: date.toISO(),
    plusHour: date.plus({ hours: 1 }).toISO(),
    offsets: ["America/New_York", "Europe/Paris", "Asia/Kolkata", "Australia/Lord_Howe", "Pacific/Chatham", "America/St_Johns", "Asia/Kathmandu"].map(zone => {
      const value = DateTime.fromISO("2024-07-01T12:00:00Z").setZone(zone);
      return [zone, value.toFormat("yyyy-MM-dd HH:mm ZZ"), value.offsetNameShort, value.isInDST];
    }),
    formats: ["en-US", "fr-FR", "de-DE", "ja-JP", "ar-EG", "es-ES"].map(locale => DateTime.fromISO("2024-01-15T09:05:03Z").setLocale(locale).toLocaleString(DateTime.DATETIME_FULL)),
    tokens: DateTime.fromISO("2024-02-29T23:59:59.123Z").toFormat("cccc, LLLL d, yyyy 'week' WW 'day' ooo, h:mm:ss.SSS a, kkkk-'W'WW-c, q"),
    parsed: [
      DateTime.fromFormat("15/01/2024 14:30", "dd/MM/yyyy HH:mm").toISO(),
      DateTime.fromFormat("janvier 15 2024", "LLLL d yyyy", { locale: "fr" }).toISODate(),
      DateTime.fromRFC2822("Tue, 01 Nov 2016 13:23:12 +0630").toISO(),
      DateTime.fromHTTP("Sun, 06 Nov 1994 08:49:37 GMT").toISO(),
      DateTime.fromSQL("2017-05-15 09:24:15").toISO(),
      DateTime.fromISO("2024-13-01").invalidReason,
    ],
    duration: Duration.fromObject({ days: 1, hours: 25, minutes: 70 }).normalize().shiftTo("days", "hours", "minutes").toObject(),
    durationHuman: Duration.fromISO("P1Y2M3DT4H5M6S").toHuman({ listStyle: "long" }),
    interval: Interval.fromDateTimes(DateTime.fromISO("2024-01-01"), DateTime.fromISO("2024-03-15")).splitBy({ weeks: 4 }).map(part => part.toISODate()),
    diff: DateTime.fromISO("2024-03-15").diff(DateTime.fromISO("2023-01-01"), ["years", "months", "days"]).toObject(),
    relative: [DateTime.fromISO("2024-03-09T12:00:00Z").toRelative(), DateTime.fromISO("2024-04-10T12:00:00Z").toRelative({ locale: "es" }), DateTime.fromISO("2024-03-10T11:59:00Z").toRelativeCalendar()],
    info: [Info.months("short", { locale: "de" }), Info.weekdays("narrow"), Info.isValidIANAZone("Mars/Base"), Info.features()],
    startOf: [date.startOf("week").toISO(), date.endOf("month").toISO(), date.startOf("quarter").toISO()],
  };
  return JSON.stringify(results, null, 1);
};
