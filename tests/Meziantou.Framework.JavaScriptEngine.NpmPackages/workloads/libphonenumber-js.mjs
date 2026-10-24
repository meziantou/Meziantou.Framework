// libphonenumber-js: parsing, validation, formatting and detection of phone numbers with the metadata of all countries
import { parsePhoneNumberFromString, AsYouType, isValidPhoneNumber, getCountryCallingCode, findPhoneNumbersInText, getExampleNumber } from "libphonenumber-js/max";
import examples from "libphonenumber-js/examples.mobile.json";

const inputs = [["+33 1 23 45 67 89"], ["06 12 34 56 78", "FR"], ["(213) 373-4253", "US"], ["+44 20 7946 0958"], ["030 123456", "DE"], ["+81 3-1234-5678"], ["+1 800 FLOWERS"], ["123", "US"], ["+7 912 345-67-89"], ["0412 345 678", "AU"]];

export default () => {
  const results = inputs.map(([text, country]) => {
    const number = parsePhoneNumberFromString(text, country);
    return number === undefined ? [text, null] : [text, number.number, number.country, number.getType() ?? null, number.isValid(), number.isPossible(), number.formatInternational(), number.formatNational(), number.getURI()];
  });

  const typing = new AsYouType("US");
  const progress = [..."2133734253"].map(digit => typing.input(digit));
  return JSON.stringify({
    results,
    progress,
    valid: [isValidPhoneNumber("+12133734253"), isValidPhoneNumber("+1213373425")],
    callingCodes: ["FR", "US", "JP", "BR"].map(country => getCountryCallingCode(country)),
    found: findPhoneNumbersInText("Call +1 213-373-4253 or 06 12 34 56 78 now", "FR").map(item => [item.number.number, item.startsAt, item.endsAt]),
    example: getExampleNumber("FR", examples)?.formatInternational(),
  }, null, 1);
};
