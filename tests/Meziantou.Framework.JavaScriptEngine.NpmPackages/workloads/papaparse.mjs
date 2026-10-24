// Papa Parse: CSV parsing (quotes, delimiters detection, headers, dynamic typing, comments, errors) and unparsing
import Papa from "papaparse";

const csv = `name,age,"city, country",joined,active
"Smith, John",42,"Paris, France",2020-01-15,true
"O""Brien",,"New
York",2019-12-01,false
# a comment
Zoë,7.5,Tōkyō,,TRUE
trailing,1,x,y,z,extra
`;

export default () => {
  const results = [];
  results.push(JSON.stringify(Papa.parse(csv, { header: true, dynamicTyping: true, comments: "#", skipEmptyLines: true }), null, 2));
  results.push(JSON.stringify(Papa.parse("a;b;c\n1;2;3\n4;5;6").meta));
  results.push(JSON.stringify(Papa.parse("a\tb\n1\t2").data));
  results.push(JSON.stringify(Papa.parse("a,b\n1,2\n3,4", { header: true, transformHeader: header => header.toUpperCase(), transform: (value, field) => field === "A" ? Number(value) * 10 : value }).data));
  const rows = [];
  Papa.parse("x,y\n1,2\n3,4\n5,6", { header: true, step: row => rows.push(row.data), preview: 2 });
  results.push(JSON.stringify(rows));
  results.push(JSON.stringify(Papa.parse("\"unclosed,1\n2,3").errors));
  results.push(Papa.unparse([{ a: 1, b: "x,y", c: "say \"hi\"" }, { a: 2, b: "line\nbreak", c: null }]));
  results.push(Papa.unparse({ fields: ["x", "y"], data: [[1, 2], [3, "=SUM(A1)"]] }, { quotes: true, delimiter: ";", newline: "\n", escapeFormulae: true }));
  return results.join("\n----\n");
};
