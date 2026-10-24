// temporal-polyfill: an implementation of Temporal in JavaScript (BigInt nanoseconds, Intl.DateTimeFormat for the time
// zones and the calendars). Chromium runs the polyfill too, not its own Temporal: the workload imports it.
import { Temporal, Intl as TemporalIntl } from "temporal-polyfill/full";

export default () => {
  const zoned = Temporal.ZonedDateTime.from("2024-03-10T01:30:00-05:00[America/New_York]");
  const date = Temporal.PlainDate.from("2024-02-29");
  const results = {
    zoned: [zoned.toString(), zoned.add({ hours: 1 }).toString(), zoned.add({ days: 1 }).toString(), zoned.withTimeZone("Asia/Kathmandu").toString(), zoned.startOfDay().toString(), zoned.hoursInDay],
    disambiguation: ["compatible", "earlier", "later"].map(disambiguation => Temporal.ZonedDateTime.from({ year: 2024, month: 11, day: 3, hour: 1, minute: 30, timeZone: "America/New_York" }, { disambiguation }).toString()),
    transitions: [zoned.getTimeZoneTransition("next")?.toString(), zoned.getTimeZoneTransition("previous")?.toString()],
    date: [date.add({ years: 1 }).toString(), date.add({ months: 1 }).toString(), date.dayOfWeek, date.weekOfYear, date.yearOfWeek, date.daysInYear, date.inLeapYear, date.with({ day: 1 }).toString()],
    calendars: ["hebrew", "islamic-umalqura", "japanese", "chinese", "persian", "buddhist"].map(calendar => {
      const value = date.withCalendar(calendar);
      return [calendar, value.toString(), value.year, value.monthCode, value.day, value.era ?? null];
    }),
    durations: [
      Temporal.Duration.from({ hours: 25, minutes: 70 }).round({ largestUnit: "day" }).toString(),
      date.until("2025-03-01", { largestUnit: "month" }).toString(),
      date.since("2023-01-31", { largestUnit: "year", smallestUnit: "day" }).toString(),
      Temporal.Duration.from("P1Y2M3DT4H5M6.789S").total({ unit: "hour", relativeTo: date }),
      Temporal.Duration.compare({ days: 1 }, { hours: 24 }, { relativeTo: zoned }),
    ],
    instant: [Temporal.Instant.fromEpochMilliseconds(1700000000000).toString(), Temporal.Instant.from("2024-01-01T00:00:00.123456789Z").epochNanoseconds.toString(), Temporal.Instant.from("2024-01-01T00:00:00Z").round({ smallestUnit: "hour", roundingIncrement: 3 }).toString()],
    plain: [Temporal.PlainTime.from("23:59:59.999999999").add({ nanoseconds: 1 }).toString(), Temporal.PlainYearMonth.from("2024-02").daysInMonth, Temporal.PlainMonthDay.from("--02-29").toPlainDate({ year: 2023 }).toString(), Temporal.PlainDateTime.from("2024-01-31T12:00").add({ months: 1 }).toString()],
    format: new TemporalIntl.DateTimeFormat("en-US", { dateStyle: "full", timeStyle: "long", timeZone: "Europe/Paris" }).format(zoned.toInstant()),
  };
  return JSON.stringify(results, null, 1);
};
