// D3: the d3 package re-exports about thirty ES modules (d3-array, d3-scale, d3-shape, d3-format, d3-time,
// d3-hierarchy, d3-geo, d3-dsv, d3-interpolate, d3-color...): scales, shapes, layouts and projections without a DOM
import * as d3 from "d3";

export default () => {
  // The engine computes Math.sin, Math.pow... with the math library of the operating system, whose last digits can
  // differ (the specification lets them be implementation-approximated): the data are integers, and the shapes are
  // written with 6 digits
  const data = d3.range(20).map(i => ({ x: i, y: ((i * 37) % 17) - 5 + i, group: i % 3 }));
  const x = d3.scaleLinear().domain(d3.extent(data, d => d.x)).range([0, 400]).nice();
  const y = d3.scaleLog().domain([1, 100]).range([300, 0]);
  const time = d3.scaleUtc().domain([new Date(Date.UTC(2024, 0, 1)), new Date(Date.UTC(2024, 11, 31))]).range([0, 1000]);
  const color = d3.scaleOrdinal(d3.schemeCategory10);
  const root = d3.hierarchy({ name: "root", children: [{ name: "a", value: 3 }, { name: "b", children: [{ name: "c", value: 5 }, { name: "d", value: 2 }] }] }).sum(d => d.value ?? 0).sort((a, b) => b.value - a.value);
  d3.treemap().size([100, 100]).padding(1)(root);
  const projection = d3.geoMercator().scale(100).translate([0, 0]);
  const csv = d3.csvParse("name,value\nA,1\nB,2.5\n\"C, quoted\",3", d3.autoType);
  const fmt = n => typeof n === "number" ? Math.round(n * 1e6) / 1e6 : n;
  return JSON.stringify({
    line: d3.line().digits(6).x(d => x(d.x)).y(d => d.y).curve(d3.curveCatmullRom)(data),
    area: d3.area().digits(6).x(d => x(d.x)).y0(0).y1(d => d.y).curve(d3.curveStep)(data.slice(0, 5)),
    arcs: d3.pie().value(d => d.y + 10)(data.slice(0, 4)).map(d3.arc().digits(6).innerRadius(10).outerRadius(50)),
    ticks: [x.ticks(5), y.ticks(), time.ticks(6).map(d3.utcFormat("%b %d")), y.tickFormat(5, ".0s")(20)],
    formats: [d3.format(".2s")(123456), d3.format("$,.2f")(-1234.5), d3.format("+.1%")(0.123), d3.format("08.3f")(Math.PI), d3.format(".3~g")(0.000123456), d3.format("#x")(255)],
    time: [d3.utcParse("%Y-%m-%dT%H:%M:%SZ")("2024-02-29T12:34:56Z").toISOString(), d3.utcWeek.count(new Date(Date.UTC(2024, 0, 1)), new Date(Date.UTC(2024, 11, 31))), d3.utcDay.range(new Date(Date.UTC(2024, 1, 27)), new Date(Date.UTC(2024, 2, 2))).length],
    statistics: [d3.mean(data, d => d.y), d3.median(data, d => d.y), d3.deviation(data, d => d.y), d3.quantile(data.map(d => d.y).sort(d3.ascending), 0.9), d3.bisectLeft([1, 2, 4, 8], 3)].map(fmt),
    grouped: [...d3.rollup(data, values => d3.sum(values, d => d.y), d => d.group)].map(([key, value]) => [key, fmt(value)]),
    bins: d3.bin().thresholds(5)(data.map(d => d.y)).map(bin => [bin.x0, bin.x1, bin.length]),
    treemap: root.descendants().map(node => [node.data.name, node.value, fmt(node.x0), fmt(node.y0), fmt(node.x1), fmt(node.y1)]),
    geo: [projection([2.35, 48.85]).map(fmt), fmt(d3.geoDistance([2.35, 48.85], [-74, 40.7])), d3.geoPath(projection).digits(6)({ type: "LineString", coordinates: [[0, 0], [10, 10], [20, 0]] })],
    interpolate: [d3.interpolateRgb("red", "blue")(0.5), d3.interpolateHcl("red", "blue")(0.5), d3.interpolate({ a: 0, b: "1px" }, { a: 10, b: "5px" })(0.25), d3.interpolateViridis(0.3)],
    colors: [d3.color("steelblue").brighter().formatHex(), d3.hsl("#336699").toString(), fmt(d3.lab("#336699").l), color("x"), color("y")],
    csv: [csv, d3.csvFormat(csv), d3.tsvParseRows("a\tb\n1\t2")],
    delaunay: [...d3.Delaunay.from([[0, 0], [1, 0], [0, 1], [1, 1], [0.5, 0.4]]).triangles],
    quadtree: d3.quadtree([[1, 1], [2, 3], [5, 5]]).find(4, 4),
  });
};
