// The functions of the form scripts of PDF viewers (the AF functions, util): written from their documented behavior.
// The numbers and the dates are formatted with fixed rules, not with the culture of the process.
(function (global) {
  "use strict";

  // NaN when the text is not a number
  function parseNumber(value) {
    if (typeof value === "number") return value;
    var text = String(value === undefined || value === null ? "" : value).trim();
    if (text === "") return 0;
    // A comma decimal separator is accepted when there is no period
    if (text.indexOf(".") < 0 && /^-?[\d\s]*,\d+$/.test(text)) text = text.replace(",", ".");
    text = text.replace(/[\s,]/g, "").replace(/^\((.*)\)$/, "-$1").replace(/[^\d.eE+\-]/g, "");
    return Number(text);
  }

  function toNumber(value) {
    var number = parseNumber(value);
    return isNaN(number) ? 0 : number;
  }

  function AFMakeNumber(value) {
    var text = String(value === undefined || value === null ? "" : value).trim();
    if (text === "") return null;
    var number = parseNumber(text);
    return isFinite(number) ? number : null;
  }

  function groupDigits(integer, separator) {
    var result = "";
    for (var i = 0; i < integer.length; i++) {
      if (i > 0 && (integer.length - i) % 3 === 0) result += separator;
      result += integer.charAt(i);
    }
    return result;
  }

  // sepStyle: 0 1,234.56  1 1234.56  2 1.234,56  3 1234,56  4 1'234.56
  function formatNumber(number, decimals, sepStyle) {
    var fixed = Math.abs(number).toFixed(Math.max(0, Math.min(decimals, 20)));
    var parts = fixed.split(".");
    var grouping = { 0: ",", 2: ".", 4: "'" }[sepStyle];
    var integer = grouping !== undefined ? groupDigits(parts[0], grouping) : parts[0];
    var point = sepStyle === 2 || sepStyle === 3 ? "," : ".";
    return parts.length > 1 ? integer + point + parts[1] : integer;
  }

  // negStyle: 0 -1.00  1 red 1.00  2 (1.00)  3 red (1.00); currency prepended or appended
  function AFNumber_Format(nDec, sepStyle, negStyle, currStyle, strCurrency, bCurrencyPrepend) {
    var number = AFMakeNumber(event.value);
    if (number === null) { event.value = ""; return; }
    var text = formatNumber(number, nDec, sepStyle);
    if (strCurrency) text = bCurrencyPrepend ? strCurrency + text : text + strCurrency;
    if (number < 0) {
      if (negStyle === 2 || negStyle === 3) text = "(" + text + ")";
      else if (negStyle === 0) text = "-" + text;
      if ((negStyle === 1 || negStyle === 3) && event.target) event.target.textColor = ["RGB", 1, 0, 0];
    }
    event.value = text;
  }

  function AFNumber_Keystroke(nDec, sepStyle) {
    var value = AFMergeChange(event);
    if (!event.willCommit) {
      event.rc = /^[\s\d.,'\-+()]*$/.test(value);
      return;
    }
    if (String(value).trim() === "") return;
    var normalized = String(value);
    if (sepStyle === 2 || sepStyle === 3) normalized = normalized.replace(/\./g, "").replace(",", ".");
    if (AFMakeNumber(normalized) === null || !/\d/.test(normalized)) {
      event.rc = false;
      app.alert("The value entered does not match the format of the field [ " + (event.target ? event.target.name : "") + " ]");
      return;
    }
    event.value = String(AFMakeNumber(normalized));
  }

  function AFPercent_Format(nDec, sepStyle, bPercentPrepend) {
    var number = AFMakeNumber(event.value);
    if (number === null) { event.value = ""; return; }
    var text = (number < 0 ? "-" : "") + formatNumber(number * 100, nDec, sepStyle);
    event.value = bPercentPrepend ? "%" + text : text + "%";
  }

  function AFPercent_Keystroke(nDec, sepStyle) { AFNumber_Keystroke(nDec, sepStyle); }

  function AFSimple(cFunction, nValue1, nValue2) {
    switch (cFunction) {
      case "AVG": case "SUM": return nValue1 + nValue2;
      case "PRD": return nValue1 * nValue2;
      case "MIN": return Math.min(nValue1, nValue2);
      case "MAX": return Math.max(nValue1, nValue2);
    }
    return 0;
  }

  function fieldNames(cFields) {
    if (Array.isArray(cFields)) return cFields;
    return String(cFields).split(",").map(function (name) { return name.trim(); }).filter(function (name) { return name !== ""; });
  }

  function AFSimple_Calculate(cFunction, cFields) {
    var names = fieldNames(cFields);
    var result = cFunction === "PRD" ? 1 : 0;
    var count = 0;
    for (var i = 0; i < names.length; i++) {
      var field = global.getField(names[i]);
      if (!field) continue;
      var number = toNumber(field.value);
      result = count === 0 && (cFunction === "MIN" || cFunction === "MAX") ? number : AFSimple(cFunction, result, number);
      count++;
    }
    if (cFunction === "AVG" && count > 0) result /= count;
    event.value = result;
  }

  function AFRange_Validate(bGreaterThan, nGreaterThan, bLessThan, nLessThan) {
    if (String(event.value).trim() === "") return;
    var value = toNumber(event.value);
    if ((bGreaterThan && value < nGreaterThan) || (bLessThan && value > nLessThan)) {
      event.rc = false;
      app.alert("The value must be in the range of the field");
    }
  }

  function AFMergeChange(e) {
    var value = String(e.value === undefined || e.value === null ? "" : e.value);
    if (e.willCommit) return value;
    var start = e.selStart >= 0 ? e.selStart : value.length;
    var end = e.selEnd >= 0 ? e.selEnd : value.length;
    return value.substring(0, start) + (e.change || "") + value.substring(end);
  }

  // Dates: the format letters of util.printd (yyyy, yy, mmmm, mmm, mm, m, dddd, ddd, dd, d, HH, H, hh, h, MM, M, ss, s, tt, t)
  var monthNames = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];
  var dayNames = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

  function pad(number, length) {
    var text = String(number);
    while (text.length < length) text = "0" + text;
    return text;
  }

  function printd(format, date) {
    if (!(date instanceof Date) || isNaN(date.getTime())) return "";
    if (typeof format === "number") format = ["m/d", "mm/dd/yyyy", "yyyy/mm/dd HH:MM:ss"][format] || "mm/dd/yyyy";
    var tokens = /yyyy|yy|mmmm|mmm|mm|m|dddd|ddd|dd|d|HH|H|hh|h|MM|M|ss|s|tt|t|\\.|./g;
    return String(format).replace(tokens, function (token) {
      var hours = date.getHours();
      switch (token) {
        case "yyyy": return pad(date.getFullYear(), 4);
        case "yy": return pad(date.getFullYear() % 100, 2);
        case "mmmm": return monthNames[date.getMonth()];
        case "mmm": return monthNames[date.getMonth()].substring(0, 3);
        case "mm": return pad(date.getMonth() + 1, 2);
        case "m": return String(date.getMonth() + 1);
        case "dddd": return dayNames[date.getDay()];
        case "ddd": return dayNames[date.getDay()].substring(0, 3);
        case "dd": return pad(date.getDate(), 2);
        case "d": return String(date.getDate());
        case "HH": return pad(hours, 2);
        case "H": return String(hours);
        case "hh": return pad(hours % 12 === 0 ? 12 : hours % 12, 2);
        case "h": return String(hours % 12 === 0 ? 12 : hours % 12);
        case "MM": return pad(date.getMinutes(), 2);
        case "M": return String(date.getMinutes());
        case "ss": return pad(date.getSeconds(), 2);
        case "s": return String(date.getSeconds());
        case "tt": return hours < 12 ? "am" : "pm";
        case "t": return hours < 12 ? "a" : "p";
      }
      return token.charAt(0) === "\\" ? token.charAt(1) : token;
    });
  }

  function scand(format, text) {
    var value = String(text === undefined || text === null ? "" : text).trim();
    if (value === "") return null;
    var numbers = value.match(/\d+/g) || [];
    var order = [];
    String(format).replace(/yyyy|yy|mmm+|mm|m|dd|d|HH|H|hh|h|MM|M|ss|s/g, function (token) { order.push(token); return token; });
    var year = 2000, month = 0, day = 1, hours = 0, minutes = 0, seconds = 0, index = 0;
    var monthName = /[A-Za-z]{3,}/.exec(value);
    for (var i = 0; i < order.length; i++) {
      var token = order[i];
      if (token.indexOf("mmm") === 0) {
        if (!monthName) return null;
        var found = -1;
        for (var j = 0; j < 12; j++) if (monthNames[j].substring(0, 3).toLowerCase() === monthName[0].substring(0, 3).toLowerCase()) found = j;
        if (found < 0) return null;
        month = found;
        continue;
      }
      if (index >= numbers.length) return null;
      var number = Number(numbers[index++]);
      switch (token) {
        case "yyyy": year = number; break;
        case "yy": year = number < 50 ? 2000 + number : 1900 + number; break;
        case "mm": case "m": month = number - 1; break;
        case "dd": case "d": day = number; break;
        case "HH": case "H": case "hh": case "h": hours = number; break;
        case "MM": case "M": minutes = number; break;
        case "ss": case "s": seconds = number; break;
      }
    }
    // 12 am is midnight, 12 pm is noon
    if (/(^|[^a-z])am([^a-z]|$)/i.test(value) && hours === 12) hours = 0;
    else if (/(^|[^a-z])pm([^a-z]|$)/i.test(value) && hours < 12) hours += 12;
    var date = new Date(year, month, day, hours, minutes, seconds);
    if (date.getMonth() !== month || date.getDate() !== day) return null;
    return date;
  }

  var dateFormats = ["m/d", "m/d/yy", "mm/dd/yy", "mm/yy", "d-mmm", "d-mmm-yy", "dd-mmm-yy", "yy-mm-dd", "mmm-yy", "mmmm-yy", "mmm d, yyyy", "mmmm d, yyyy", "m/d/yy h:MM tt", "m/d/yy HH:MM"];
  var timeFormats = ["HH:MM", "h:MM tt", "HH:MM:ss", "h:MM:ss tt"];

  function AFDate_FormatEx(cFormat) {
    if (String(event.value).trim() === "") return;
    var date = scand(cFormat, event.value) || new Date(String(event.value));
    event.value = isNaN(date.getTime()) ? event.value : printd(cFormat, date);
  }

  function AFDate_KeystrokeEx(cFormat) {
    if (!event.willCommit || String(event.value).trim() === "") return;
    if (scand(cFormat, event.value) === null) {
      event.rc = false;
      app.alert("Invalid date/time: please ensure that the date/time exists. Field [ " + (event.target ? event.target.name : "") + " ] should match format " + cFormat);
    }
  }

  function AFDate_Format(pdf) { AFDate_FormatEx(dateFormats[pdf] || dateFormats[0]); }
  function AFDate_Keystroke(pdf) { AFDate_KeystrokeEx(dateFormats[pdf] || dateFormats[0]); }
  function AFTime_FormatEx(cFormat) { AFDate_FormatEx(cFormat); }
  function AFTime_KeystrokeEx(cFormat) { AFDate_KeystrokeEx(cFormat); }
  function AFTime_Format(ptf) { AFDate_FormatEx(timeFormats[ptf] || timeFormats[0]); }
  function AFTime_Keystroke(ptf) { AFDate_KeystrokeEx(timeFormats[ptf] || timeFormats[0]); }

  // psf: 0 zip code, 1 zip + 4, 2 phone number, 3 social security number
  var specialMasks = ["99999", "99999-9999", "(999) 999-9999", "999-99-9999"];

  function AFSpecial_Format(psf) {
    var digits = String(event.value).replace(/\D/g, "");
    if (digits === "") { event.value = ""; return; }
    var mask = psf === 2 && digits.length <= 7 ? "999-9999" : specialMasks[psf] || specialMasks[0];
    event.value = applyMask(mask, digits);
  }

  function applyMask(mask, digits) {
    var result = "", index = 0;
    for (var i = 0; i < mask.length && index < digits.length; i++) {
      result += mask.charAt(i) === "9" ? digits.charAt(index++) : mask.charAt(i);
    }
    return result;
  }

  function AFSpecial_Keystroke(psf) {
    if (!event.willCommit) return;
    var digits = String(event.value).replace(/\D/g, "");
    var lengths = [[5], [9], [7, 10], [9]][psf] || [5];
    if (digits !== "" && lengths.indexOf(digits.length) < 0) {
      event.rc = false;
      app.alert("The value entered does not match the format of the field [ " + (event.target ? event.target.name : "") + " ]");
    }
  }

  function AFSpecial_KeystrokeEx(mask) {
    if (!event.willCommit) return;
    var value = String(event.value);
    if (value !== "" && value.length !== String(mask).length) event.rc = false;
  }

  // util.printf: %d, %f, %s, %x and %, with flags (",0" to ",4" for the separators, "+", " ", "0", "#"), width and precision
  function printf(format) {
    var args = arguments, argIndex = 1;
    return String(format).replace(/%(,[0-4])?([+ 0#]*)(\d+)?(?:\.(\d+))?([dfsx%])/g, function (match, sep, flags, width, precision, type) {
      if (type === "%") return "%";
      var value = args[argIndex++];
      var text;
      switch (type) {
        case "d": text = formatNumber(Math.trunc(toNumber(value)), 0, sep ? Number(sep.charAt(1)) : 1); break;
        case "f": text = formatNumber(toNumber(value), precision === undefined ? 6 : Number(precision), sep ? Number(sep.charAt(1)) : 1); break;
        case "x": text = (Math.trunc(toNumber(value)) >>> 0).toString(16); break;
        default: text = String(value === undefined ? "" : value); if (precision !== undefined) text = text.substring(0, Number(precision)); break;
      }
      var negative = (type === "d" || type === "f") && toNumber(value) < 0;
      if (negative) text = "-" + text;
      else if ((type === "d" || type === "f") && flags.indexOf("+") >= 0) text = "+" + text;
      else if ((type === "d" || type === "f") && flags.indexOf(" ") >= 0) text = " " + text;
      if (width !== undefined) {
        var padding = flags.indexOf("0") >= 0 && type !== "s" ? "0" : " ";
        while (text.length < Number(width)) text = padding === "0" ? (/^[+\- ]/.test(text) ? text.charAt(0) + "0" + text.substring(1) : "0" + text) : " " + text;
      }
      return text;
    });
  }

  var exports = {
    AFMakeNumber: AFMakeNumber, AFNumber_Format: AFNumber_Format, AFNumber_Keystroke: AFNumber_Keystroke,
    AFPercent_Format: AFPercent_Format, AFPercent_Keystroke: AFPercent_Keystroke, AFSimple: AFSimple,
    AFSimple_Calculate: AFSimple_Calculate, AFRange_Validate: AFRange_Validate, AFMergeChange: AFMergeChange,
    AFDate_Format: AFDate_Format, AFDate_FormatEx: AFDate_FormatEx, AFDate_Keystroke: AFDate_Keystroke, AFDate_KeystrokeEx: AFDate_KeystrokeEx,
    AFTime_Format: AFTime_Format, AFTime_FormatEx: AFTime_FormatEx, AFTime_Keystroke: AFTime_Keystroke, AFTime_KeystrokeEx: AFTime_KeystrokeEx,
    AFSpecial_Format: AFSpecial_Format, AFSpecial_Keystroke: AFSpecial_Keystroke, AFSpecial_KeystrokeEx: AFSpecial_KeystrokeEx,
  };
  for (var name in exports) global[name] = exports[name];
  global.util = { printf: printf, printd: printd, scand: scand };
})(globalThis);
