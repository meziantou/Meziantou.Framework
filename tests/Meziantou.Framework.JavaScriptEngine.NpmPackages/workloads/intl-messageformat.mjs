// intl-messageformat (FormatJS): ICU MessageFormat parsing and formatting with plurals, selects, ordinals, numbers,
// dates and rich text tags (Intl.PluralRules, Intl.NumberFormat, Intl.DateTimeFormat)
import { IntlMessageFormat } from "intl-messageformat";

const messages = {
  en: "{name} has {count, plural, =0 {no messages} one {# message} other {# messages}} ({ratio, number, percent}), last on {date, date, long} at {date, time, short}. {gender, select, female {She} male {He} other {They}} finished {place, selectordinal, one {#st} two {#nd} few {#rd} other {#th}}. Price: {price, number, ::currency/EUR}. <b>Bold</b>",
  fr: "{name} a {count, plural, =0 {aucun message} one {# message} other {# messages}} ({ratio, number, percent}), le {date, date, long}. Prix : {price, number, ::currency/EUR}.",
  ar: "{count, plural, zero {لا رسائل} one {رسالة واحدة} two {رسالتان} few {# رسائل} many {# رسالة} other {# رسالة}}",
  ru: "{count, plural, one {# сообщение} few {# сообщения} many {# сообщений} other {# сообщения}}",
  ja: "{name}さんは{count, number}件のメッセージがあります。{date, date, full}",
};

export default () => {
  const results = [];
  const date = new Date(Date.UTC(2024, 0, 15, 9, 30));
  for (const [locale, message] of Object.entries(messages)) {
    const format = new IntlMessageFormat(message, locale, undefined, { formatters: undefined });
    for (const count of [0, 1, 2, 5, 21, 1000]) {
      const value = format.format({ name: "Alice", count, ratio: count / 1000, date, gender: count % 2 ? "female" : "male", place: count, price: count * 1.5, b: chunks => `[${chunks.join("")}]` });
      results.push(locale + " " + count + ": " + (Array.isArray(value) ? value.join("") : value));
    }
  }

  results.push(JSON.stringify(new IntlMessageFormat("Hello {who}, {n, plural, one {#} other {#}}", "en").getAst()));
  try {
    new IntlMessageFormat("{unclosed", "en");
  }
  catch (error) {
    results.push(error.name + ": " + error.message);
  }

  return results.join("\n");
};
