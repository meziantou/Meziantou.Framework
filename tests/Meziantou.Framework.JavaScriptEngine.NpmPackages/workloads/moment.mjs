// Moment.js with moment-timezone and its packed time zone data: parsing, formatting, locales, durations, calendars
// and conversions across DST transitions
import moment from "moment-timezone";
import "moment/locale/fr.js";
import "moment/locale/ru.js";

export default () => {
  const date = moment.utc("2024-02-29T13:45:30.123Z");
  return JSON.stringify({
    formats: [date.format(), date.format("dddd, MMMM Do YYYY, h:mm:ss a"), date.format("[Week] W GGGG, Q, DDDD, X, x"), date.clone().locale("fr").format("LLLL"), date.clone().locale("ru").format("LLLL, MMMM")],
    zones: ["America/New_York", "Europe/Paris", "Asia/Kolkata", "Pacific/Apia", "America/Sao_Paulo"].map(zone => date.clone().tz(zone).format("YYYY-MM-DD HH:mm Z z")),
    transitions: [
      moment.tz("2024-03-10 01:30", "America/New_York").add(1, "hour").format(),
      moment.tz("2024-03-10 02:30", "America/New_York").format(),
      moment.tz("2024-10-27 02:30", "Europe/Paris").format(),
      moment.tz("1970-01-01", "Europe/London").format(),
      moment.tz.zone("America/New_York").utcOffset(Date.UTC(2024, 6, 1)),
    ],
    guessFree: moment.tz.names().filter(name => name.startsWith("Europe/P")),
    parsed: [moment.utc("15/01/2024 14:30", "DD/MM/YYYY HH:mm").format(), moment.utc("2024-02-30", "YYYY-MM-DD", true).isValid(), moment.utc("2024-02-30", "YYYY-MM-DD", true).invalidAt(), moment.utc("mars 2024", "MMMM YYYY", "fr").format()],
    arithmetic: [date.clone().add(1, "M").format(), date.clone().subtract(1, "y").startOf("isoWeek").format(), date.clone().endOf("quarter").format(), date.isoWeekday(), date.dayOfYear(), date.weeksInYear()],
    durations: [moment.duration(90061000).humanize(), moment.duration({ months: 14, days: 3 }).toISOString(), moment.duration("P1Y2M3DT4H5M6S").asHours(), moment.duration(-3, "minutes").humanize(true)],
    calendar: date.calendar(date.clone().add(1, "day")),
    relative: [date.from(date.clone().add(3, "day")), date.clone().locale("fr").to(date.clone().subtract(11, "month"))],
    diff: [date.diff(moment.utc("2023-01-31"), "months", true), date.diff(moment.utc("2024-01-01"), "weeks")],
  }, null, 1);
};
