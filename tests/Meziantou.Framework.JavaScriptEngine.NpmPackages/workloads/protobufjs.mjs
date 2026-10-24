// protobuf.js: parsing .proto definitions, encoding and decoding (varints, 64-bit integers, packed fields, maps, oneof,
// nested messages, enums), JSON conversion and code generated with new Function
import protobuf from "protobufjs";

const proto = `
syntax = "proto3";
package example;

enum Status { UNKNOWN = 0; ACTIVE = 1; DISABLED = 2; }

message Address { string street = 1; uint32 zip = 2; }

message Person {
  string name = 1;
  int32 id = 2;
  repeated string emails = 3;
  Status status = 4;
  repeated int32 scores = 5 [packed = true];
  map<string, Address> addresses = 6;
  oneof contact { string phone = 7; string fax = 8; }
  int64 big = 9;
  sint32 negative = 10;
  double ratio = 11;
  bool active = 12;
  bytes data = 13;
  fixed64 fixed = 14;
  repeated Person children = 15;
}
`;

export default () => {
  const root = protobuf.parse(proto, { keepCase: true }).root;
  const Person = root.lookupType("example.Person");
  const payload = {
    name: "Alice ✓", id: 150, emails: ["a@example.com", "b@example.com"], status: 1, scores: [1, 300, -5, 70000],
    addresses: { home: { street: "Main", zip: 75001 } }, phone: "+33", big: "9007199254740993", negative: -42,
    ratio: 0.1, active: true, data: new Uint8Array([0, 255, 128]), fixed: "18446744073709551615",
    children: [{ name: "Bob", id: 1 }],
  };

  const results = [];
  results.push(String(Person.verify(payload)));
  const message = Person.fromObject(payload);
  const bytes = Person.encode(message).finish();
  results.push(Array.from(bytes).join(","));
  const decoded = Person.decode(bytes);
  results.push(JSON.stringify(Person.toObject(decoded, { longs: String, enums: String, bytes: String, defaults: true, oneofs: true })));
  results.push(JSON.stringify(decoded.toJSON()));
  results.push(Array.from(Person.encodeDelimited(Person.create({ name: "x" })).finish()).join(","));
  results.push(String(Person.verify({ id: "not a number" })));
  try {
    Person.decode(new Uint8Array([10, 5, 1]));
  }
  catch (error) {
    results.push(error.name + ": " + error.message);
  }

  results.push(JSON.stringify(root.toJSON()));
  return results.join("\n----\n");
};
