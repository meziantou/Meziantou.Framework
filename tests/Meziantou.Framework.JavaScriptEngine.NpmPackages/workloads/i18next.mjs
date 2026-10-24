// i18next: resources, interpolation, nesting, plurals (Intl.PluralRules), contexts, formatting (Intl.NumberFormat,
// Intl.DateTimeFormat, Intl.RelativeTimeFormat, Intl.ListFormat), fallbacks and namespaces
import i18next from "i18next";

const resources = {
  en: {
    translation: {
      welcome: "Welcome, {{name}}!",
      items_one: "{{count}} item",
      items_other: "{{count}} items",
      place_ordinal_one: "{{count}}st",
      place_ordinal_two: "{{count}}nd",
      place_ordinal_few: "{{count}}rd",
      place_ordinal_other: "{{count}}th",
      friend_male: "A boyfriend",
      friend_female: "A girlfriend",
      nested: "$t(welcome) You have $t(items, {\"count\": {{count}} })",
      price: "Price: {{value, currency(EUR)}}",
      when: "On {{value, datetime}}",
      ago: "{{value, relativetime(day)}}",
      list: "{{value, list(disjunction)}}",
      number: "{{value, number(minimumFractionDigits: 2)}}",
      escaped: "{{html}} vs {{- html}}",
      onlyEnglish: "fallback",
    },
    other: { title: "Other namespace" },
  },
  fr: { translation: { welcome: "Bienvenue, {{name}} !", items_one: "{{count}} élément", items_other: "{{count}} éléments", price: "Prix : {{value, currency(EUR)}}" } },
  ar: { translation: { items_zero: "لا عناصر", items_one: "عنصر واحد", items_two: "عنصران", items_few: "{{count}} عناصر", items_many: "{{count}} عنصرًا", items_other: "{{count}} عنصر" } },
  pl: { translation: { items_one: "{{count}} plik", items_few: "{{count}} pliki", items_many: "{{count}} plików", items_other: "{{count}} pliku" } },
};

export default async () => {
  const results = [];
  const i18n = i18next.createInstance();
  await i18n.init({ lng: "en", fallbackLng: "en", resources, ns: ["translation", "other"], interpolation: { escapeValue: true } });
  const t = i18n.t.bind(i18n);
  results.push(t("welcome", { name: "Alice" }), t("items", { count: 1 }), t("items", { count: 5 }), t("place", { count: 22, ordinal: true }), t("place", { count: 13, ordinal: true }));
  results.push(t("friend", { context: "female" }), t("nested", { count: 3 }), t("price", { value: 1234.5 }), t("when", { value: new Date(Date.UTC(2024, 0, 15)), formatParams: { value: { dateStyle: "full", timeZone: "UTC" } } }));
  results.push(t("ago", { value: -3 }), t("list", { value: ["a", "b", "c"] }), t("number", { value: 1234 }), t("escaped", { html: "<b>&</b>" }), t("other:title"), t("missing.key"), t("missing", { defaultValue: "default" }));
  for (const lng of ["fr", "ar", "pl"]) {
    await i18n.changeLanguage(lng);
    results.push(lng + ": " + [0, 1, 2, 3, 5, 11, 22, 102].map(count => i18n.t("items", { count })).join(" | ") + " | " + i18n.t("onlyEnglish") + " | " + i18n.t("price", { value: 9.99 }));
  }

  return results.join("\n");
};
