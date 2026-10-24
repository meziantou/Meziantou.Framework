// Day.js with its plugins: parsing with formats, time zones (Intl.DateTimeFormat), durations, relative times, locales,
// ISO weeks, quarters and business calculations
import dayjs from "dayjs";
import utc from "dayjs/plugin/utc.js";
import timezone from "dayjs/plugin/timezone.js";
import customParseFormat from "dayjs/plugin/customParseFormat.js";
import duration from "dayjs/plugin/duration.js";
import relativeTime from "dayjs/plugin/relativeTime.js";
import isoWeek from "dayjs/plugin/isoWeek.js";
import quarterOfYear from "dayjs/plugin/quarterOfYear.js";
import advancedFormat from "dayjs/plugin/advancedFormat.js";
import localizedFormat from "dayjs/plugin/localizedFormat.js";
import isBetween from "dayjs/plugin/isBetween.js";
import minMax from "dayjs/plugin/minMax.js";
import "dayjs/locale/fr.js";
import "dayjs/locale/ja.js";

for (const plugin of [utc, timezone, customParseFormat, duration, relativeTime, isoWeek, quarterOfYear, advancedFormat, localizedFormat, isBetween, minMax])
  dayjs.extend(plugin);

export default () => {
  const date = dayjs.utc("2024-02-29T13:45:30.123Z");
  return JSON.stringify({
    formats: [date.format(), date.format("dddd, MMMM D, YYYY h:mm A"), date.format("Q Do X x k kk"), date.locale("fr").format("LLLL"), date.locale("ja").format("LLLL dddd")],
    zones: ["America/New_York", "Asia/Tokyo", "Asia/Kolkata", "Australia/Adelaide"].map(zone => date.tz(zone).format("YYYY-MM-DD HH:mm Z z")),
    dst: [dayjs.tz("2024-03-10 01:30", "America/New_York").add(1, "hour").format(), dayjs.tz("2024-11-03 01:30", "America/New_York").format("Z")],
    parsed: [dayjs("15/01/2024 14:30", "DD/MM/YYYY HH:mm").format("YYYY-MM-DD HH:mm"), dayjs("2024-02-30", "YYYY-MM-DD", true).isValid(), dayjs("12-25-1995", ["YYYY", "MM-DD-YYYY"]).format("YYYY-MM-DD")],
    arithmetic: [date.add(1, "month").toISOString(), date.subtract(1, "year").toISOString(), date.startOf("isoWeek").toISOString(), date.endOf("quarter").toISOString(), date.isoWeek(), date.quarter(), date.daysInMonth()],
    durations: [dayjs.duration(90061000).format("D[d] HH:mm:ss"), dayjs.duration({ months: 14, days: 3 }).toISOString(), dayjs.duration(2, "hours").humanize(true), dayjs.duration(45, "days").locale("fr").humanize()],
    relative: [date.from(date.add(3, "day")), date.to(date.subtract(11, "month")), date.locale("ja").from(date.add(2, "year"))],
    comparison: [date.isBetween("2024-01-01", "2024-12-31", "day", "[]"), dayjs.max(date, date.add(1, "day")).toISOString(), date.diff(dayjs.utc("2023-01-01"), "month", true)],
  }, null, 1);
};
