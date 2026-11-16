// The objects the scripts of a form see: the document (this), the fields, app and event. The host functions are
// captured here and removed from the global object.
(function (global) {
  "use strict";
  var host = global.__pdf;
  delete global.__pdf;

  function blocked(name) {
    return function () { host.blocked(name); return undefined; };
  }

  function Field(name) {
    Object.defineProperty(this, "name", { value: name, enumerable: true });
  }

  Object.defineProperty(Field.prototype, "value", {
    get: function () {
      var value = host.getValue(this.name);
      // A text that is a number is a number, as in the viewers
      if (typeof value === "string" && value.trim() !== "" && !isNaN(Number(value))) return Number(value);
      return value;
    },
    set: function (value) { host.setValue(this.name, value === undefined || value === null ? "" : String(value)); },
  });
  Object.defineProperty(Field.prototype, "valueAsString", {
    get: function () { return String(host.getValue(this.name)); },
  });
  ["type", "readonly", "required", "userName"].forEach(function (property) {
    Object.defineProperty(Field.prototype, property, { get: function () { return host.info(this.name, property); }, set: function () { } });
  });
  // The properties of the appearance are accepted and ignored: the appearances are written by the library
  ["display", "textColor", "fillColor", "strokeColor", "textSize", "textFont", "hidden", "borderStyle"].forEach(function (property) {
    var values = new WeakMap();
    Object.defineProperty(Field.prototype, property, { get: function () { return values.get(this); }, set: function (value) { values.set(this, value); } });
  });
  Field.prototype.setFocus = function () { };
  Field.prototype.setAction = blocked("Field.setAction");
  Field.prototype.getArray = function () { return [this]; };
  Field.prototype.isBoxChecked = function () { return host.getValue(this.name) !== "Off"; };
  Field.prototype.checkThisBox = function (index, checked) { this.value = checked === false ? "Off" : "Yes"; };

  function getField(name) {
    var text = String(name);
    return host.exists(text) ? new Field(text) : null;
  }

  global.getField = getField;
  global.display = { visible: 0, hidden: 1, noPrint: 2, noView: 3 };
  global.color = { black: ["G", 0], white: ["G", 1], red: ["RGB", 1, 0, 0], green: ["RGB", 0, 1, 0], blue: ["RGB", 0, 0, 1], transparent: ["T"] };
  global.app = {
    viewerType: "Reader",
    viewerVariation: "Reader",
    viewerVersion: 11,
    platform: "UNIX",
    alert: function (message) {
      var text = message !== null && typeof message === "object" ? message.cMsg : message;
      host.alert(text === undefined ? "" : String(text));
      return 1;
    },
    beep: function () { },
    response: function () { return null; },
    launchURL: blocked("app.launchURL"),
    mailMsg: blocked("app.mailMsg"),
    execMenuItem: blocked("app.execMenuItem"),
    openDoc: blocked("app.openDoc"),
    newDoc: blocked("app.newDoc"),
    setInterval: blocked("app.setInterval"),
    setTimeOut: blocked("app.setTimeOut"),
  };

  var doc = {
    getField: getField,
    calculateNow: function () { },
    resetForm: blocked("this.resetForm"),
    submitForm: blocked("this.submitForm"),
    mailDoc: blocked("this.mailDoc"),
    mailForm: blocked("this.mailForm"),
    print: blocked("this.print"),
    saveAs: blocked("this.saveAs"),
    exportAsFDF: blocked("this.exportAsFDF"),
    exportAsXFDF: blocked("this.exportAsXFDF"),
    importAnFDF: blocked("this.importAnFDF"),
    importDataObject: blocked("this.importDataObject"),
    exportDataObject: blocked("this.exportDataObject"),
    getURL: blocked("this.getURL"),
    gotoNamedDest: blocked("this.gotoNamedDest"),
    closeDoc: blocked("this.closeDoc"),
  };

  function makeEvent(name, target, value, source) {
    return {
      type: "Field",
      name: name,
      value: value,
      rc: true,
      willCommit: true,
      commitKey: 2,
      change: "",
      changeEx: "",
      keyDown: false,
      modifier: false,
      shift: false,
      selStart: -1,
      selEnd: -1,
      target: getField(target),
      targetName: target,
      source: source === "" ? null : getField(source),
    };
  }

  // The document is the global object: the document-level scripts run on it, and what they set on this (or declare)
  // is seen by the field scripts, whose this is the document too
  Object.keys(doc).forEach(function (name) { global[name] = doc[name]; });

  return { doc: global, makeEvent: makeEvent };
})(globalThis);
