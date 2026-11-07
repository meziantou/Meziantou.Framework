// date-fns 4: hundreds of small ES modules (the index imports all of them), formatting and parsing with locales,
// arithmetic, intervals and ISO weeks
import { format, parse, addDays, addMonths, subYears, differenceInCalendarDays, differenceInBusinessDays, eachDayOfInterval, eachWeekOfInterval, startOfWeek, endOfMonth, isLeapYear, getISOWeek, getISOWeekYear, formatDistance, formatDistanceStrict, formatRelative, intervalToDuration, formatDuration, parseISO, formatISO, isValid, closestTo, max, min, nextFriday, setDay, lastDayOfQuarter, roundToNearestMinutes, areIntervalsOverlapping, getOverlappingDaysInIntervals, eachMonthOfInterval } from "date-fns";
import { fr, ja, de, ar } from "date-fns/locale";

export default () => {
  const date = new Date(2024, 1, 29, 13, 45, 30, 123);
  const base = new Date(2024, 2, 10, 12, 0, 0);
  return JSON.stringify({
    formats: [
      format(date, "yyyy-MM-dd'T'HH:mm:ss.SSSxxx"),
      format(date, "EEEE, MMMM do, yyyy 'at' h:mm a"),
      format(date, "PPPPpppp"),
      format(date, "PPPP", { locale: fr }),
      format(date, "PPPP", { locale: ja }),
      format(date, "EEEE d MMMM yyyy", { locale: de }),
      format(date, "PPP", { locale: ar }),
      format(date, "RRRR-'W'II-i, Q, DDD, t, T", { useAdditionalDayOfYearTokens: true }),
    ],
    parsed: [
      parse("29/02/2024 13:45", "dd/MM/yyyy HH:mm", base).toISOString(),
      parse("3 mars 2024", "d MMMM yyyy", base, { locale: fr }).toISOString(),
      parse("2024-W10-3", "RRRR-'W'II-i", base).toISOString(),
      isValid(parse("31/02/2024", "dd/MM/yyyy", base)),
      parseISO("2024-02-29T13:45:30.123+05:30").toISOString(),
      formatISO(date, { representation: "date" }),
    ],
    arithmetic: [addDays(date, 1), addMonths(date, 12), subYears(date, 1), endOfMonth(date), startOfWeek(date, { weekStartsOn: 1 }), nextFriday(date), setDay(date, 0), lastDayOfQuarter(date), roundToNearestMinutes(date, { nearestTo: 15 })].map(value => value.toISOString()),
    differences: [differenceInCalendarDays(base, date), differenceInBusinessDays(base, date), isLeapYear(date), getISOWeek(new Date(2021, 0, 3)), getISOWeekYear(new Date(2021, 0, 3))],
    intervals: [
      eachDayOfInterval({ start: date, end: base }).length,
      eachWeekOfInterval({ start: date, end: base }, { weekStartsOn: 1 }).map(value => value.toISOString()),
      eachMonthOfInterval({ start: new Date(2023, 10, 15), end: base }).map(value => format(value, "MMM yyyy")),
      areIntervalsOverlapping({ start: date, end: base }, { start: base, end: addDays(base, 1) }, { inclusive: true }),
      getOverlappingDaysInIntervals({ start: date, end: base }, { start: addDays(date, 3), end: addDays(base, 10) }),
    ],
    distances: [
      formatDistance(date, base, { addSuffix: true }),
      formatDistance(date, base, { addSuffix: true, locale: fr }),
      formatDistanceStrict(date, base, { unit: "hour" }),
      formatRelative(addDays(base, -1), base),
      formatDuration(intervalToDuration({ start: date, end: addMonths(base, 14) })),
      formatDuration({ years: 2, days: 3 }, { locale: de, delimiter: ", " }),
    ],
    closest: [closestTo(base, [date, addDays(base, 2), subYears(base, 1)]), max([date, base]), min([date, base])].map(value => value.toISOString()),
  }, null, 1);
};
