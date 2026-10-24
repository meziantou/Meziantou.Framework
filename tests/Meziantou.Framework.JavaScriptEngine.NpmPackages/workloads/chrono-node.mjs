// chrono: natural language date parsing (many locales, regular expressions with lookbehinds and Unicode classes)
import * as chrono from "chrono-node";

const reference = { instant: new Date(Date.UTC(2024, 2, 13, 10, 0, 0)), timezone: "UTC" };
const inputs = [
  "Let's meet next Friday at 5pm",
  "The deadline is March 25th, 2024 11:59 PM EST",
  "from 3 to 5pm tomorrow",
  "in 2 weeks",
  "5 days ago",
  "last month",
  "Sat Aug 17 2013 18:40:39 GMT+0900 (JST)",
  "2014-11-30T08:15:30-05:30",
  "12/31",
  "noon on the 4th of July",
  "the day after tomorrow at 9:30",
  "Christmas",
];

export default () => {
  const results = inputs.map(input => chrono.parse(input, reference, { forwardDate: true }).map(result => [result.text, result.index, result.start.date().toISOString(), result.end?.date().toISOString() ?? null, Object.keys(result.start.knownValues).sort().join(",")]));
  results.push(chrono.fr.parse("le 12 février 2024 à 14h30", reference).map(result => result.start.date().toISOString()));
  results.push(chrono.de.parse("morgen um 15 Uhr", reference).map(result => result.start.date().toISOString()));
  results.push(chrono.ja.parse("2024年3月15日 午後3時", reference).map(result => result.start.date().toISOString()));
  results.push(chrono.strict.parse("next friday", reference).length);
  return JSON.stringify(results, null, 1);
};
