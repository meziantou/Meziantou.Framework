---
title: Tooltips in a web page
description: Show a styled tooltip with several values when the pointer is over a chart value, with an SVG inlined in an HTML page.
---

The [Title](cref:Meziantou.Framework.Charts.Mark{T}.Title) channel of a mark writes a `title` element in each shape of
the SVG. Browsers show it as a plain-text tooltip, with no script. For a styled tooltip that shows several values,
write the values in `data-*` attributes with the
[DataAttributes](cref:Meziantou.Framework.Charts.Mark{T}.DataAttributes) channel, then let a script of the page read
them and draw the tooltip.

## Render the chart

Set the `DataAttributes` channel, and keep a readable `Title`: it is the tooltip and the accessible name of the shape
when the script does not run. Give the mark a [CssClass](cref:Meziantou.Framework.Charts.Mark.CssClass), and render
the SVG without the `xmlns` attribute and with an identifier prefix of its own, to inline it in the page:

<!-- ref:../../tests/Meziantou.Framework.Charts.Tests/CartesianChartTests.cs;region=Charts_RichTooltip;format=md-fenced-code-block;dedent=true -->
```csharp
var totals = SampleData.Sales.GroupBy(sale => sale.Region).ToDictionary(group => group.Key, group => group.Sum(sale => sale.Amount));
var plot = new Plot { Title = "Sales by region, stacked by year" };
plot.Add(new BarMark<Sale>(SampleData.Sales, x: sale => sale.Region, y: sale => sale.Amount)
{
    Fill = sale => sale.Year,
    Transform = new StackTransform(),
    Title = sale => $"{sale.Region}, {sale.Year}: {sale.Amount}",
    DataAttributes = sale =>
    [
        new("region", sale.Region),
        new("year", sale.Year),
        new("amount", sale.Amount),
        new("share", sale.Amount / totals[sale.Region]),
    ],
    CssClass = "sales",
});
var svg = plot.ToSvg(new SvgRenderOptions { IdPrefix = "sales", IncludeNamespace = false });
```
<!-- endref -->

Each bar of the SVG holds the values of its row:

```xml
<rect x="52.78" y="191.93" width="129.87" height="94.18" fill="#efb118" data-amount="135" data-region="North" data-share="0.3333333333333333" data-year="2023"><title>North, 2023: 135</title></rect>
```

The names are without the `data-` prefix: lowercase ASCII letters, digits, hyphens, underscores and dots. Other names
throw an `ArgumentException` when the chart is rendered. The values are formatted with the invariant culture: numbers
round-trip, times use ISO 8601 (`2024-01-02`, `2024-01-02T03:04:05.0000000Z`), and `null` skips the attribute. Send
raw numbers rather than formatted texts: the page formats them in the language of the reader.

## Draw the tooltip

Insert the SVG in the page as raw HTML (in Razor, `@Html.Raw(svg)`), next to an empty tooltip element. The script
moves the `title` of each bar to its `aria-label` attribute, so the browser no longer shows its own tooltip, and makes
the bar reachable with the keyboard. Then one listener per chart shows the tooltip next to the pointer, or next to the
bar that has the focus. It reads the values with `element.dataset`:

```html
<div class="chart">
  <!-- The SVG of the chart: svg -->
</div>
<div id="chart-tooltip" class="chart-tooltip" role="tooltip" hidden></div>

<style>
  .chart .sales > [data-region] { cursor: pointer; outline: none; }
  .chart .sales > [data-region]:hover, .chart .sales > [data-region]:focus-visible { stroke: #1f2328; stroke-width: 2; }
  .chart-tooltip {
    position: fixed; z-index: 1000; pointer-events: none;
    min-width: 140px; padding: 8px 10px; border-radius: 6px;
    background: #ffffff; color: #1f2328; font: 13px/1.4 system-ui, sans-serif;
    box-shadow: 0 4px 16px rgb(0 0 0 / 18%);
  }
  .chart-tooltip header { display: flex; align-items: center; gap: 6px; margin-bottom: 4px; font-weight: 600; }
  .chart-tooltip .swatch { width: 10px; height: 10px; border-radius: 2px; }
  .chart-tooltip dl { display: grid; grid-template-columns: auto auto; gap: 2px 12px; margin: 0; }
  .chart-tooltip dt { color: #656d76; }
  .chart-tooltip dd { margin: 0; text-align: right; font-variant-numeric: tabular-nums; }
</style>

<script>
  const tooltip = document.getElementById("chart-tooltip");
  const amount = new Intl.NumberFormat(undefined, { style: "currency", currency: "EUR", maximumFractionDigits: 0 });
  const percent = new Intl.NumberFormat(undefined, { style: "percent", maximumFractionDigits: 1 });
  const bars = ".chart .sales > [data-region]";

  // The title becomes the accessible name of the bar, so the browser no longer
  // shows its own tooltip, and the bar can be reached with the keyboard.
  for (const bar of document.querySelectorAll(bars)) {
    const title = bar.querySelector("title");
    if (title) {
      bar.setAttribute("aria-label", title.textContent);
      title.remove();
    }

    bar.setAttribute("tabindex", "0");
  }

  function element(name, properties, ...children) {
    const result = Object.assign(document.createElement(name), properties);
    result.append(...children);
    return result;
  }

  function show(bar, x, y) {
    const data = bar.dataset; // data-region, data-year, data-amount, data-share
    const swatch = element("span", { className: "swatch" });
    swatch.style.background = bar.getAttribute("fill");
    tooltip.replaceChildren(
      element("header", {}, swatch, data.region),
      element("dl", {},
        element("dt", {}, "Year"), element("dd", {}, data.year),
        element("dt", {}, "Sales"), element("dd", {}, amount.format(Number(data.amount) * 1000)),
        element("dt", {}, "Share of region"), element("dd", {}, percent.format(Number(data.share)))));
    tooltip.hidden = false;

    // Next to the pointer, flipped when it would leave the window
    const { width, height } = tooltip.getBoundingClientRect();
    const left = x + 12 + width > innerWidth ? x - 12 - width : x + 12;
    const top = y + 12 + height > innerHeight ? y - 12 - height : y + 12;
    tooltip.style.left = `${Math.max(0, left)}px`;
    tooltip.style.top = `${Math.max(0, top)}px`;
  }

  for (const chart of document.querySelectorAll(".chart svg")) {
    chart.addEventListener("pointermove", event => {
      const bar = event.target.closest(bars);
      if (bar) show(bar, event.clientX, event.clientY);
      else tooltip.hidden = true;
    });
    chart.addEventListener("pointerleave", () => tooltip.hidden = true);
    chart.addEventListener("focusin", event => {
      const bar = event.target.closest(bars);
      if (!bar) return;
      const box = bar.getBoundingClientRect();
      show(bar, box.right, box.top);
    });
    chart.addEventListener("focusout", () => tooltip.hidden = true);
  }
</script>
```

The tooltip is built with `textContent`, never `innerHTML`, so the texts of the data cannot inject markup in the page.

## Which shapes get the attributes

- A shape drawn for one row gets the attributes of that row: a bar, a dot, a cell, a link of a
  [SankeyMark\<T\>](cref:Meziantou.Framework.Charts.SankeyMark{T}), a ribbon of a
  [ChordMark\<T\>](cref:Meziantou.Framework.Charts.ChordMark{T}) or an edge of a
  [NetworkMark\<T\>](cref:Meziantou.Framework.Charts.NetworkMark{T}).
- A shape drawn for several rows gets the attributes of the first one. A
  [LineMark\<T\>](cref:Meziantou.Framework.Charts.LineMark{T}) draws a series as a single path: for values per point,
  set [MarkerRadius](cref:Meziantou.Framework.Charts.LineMark{T}.MarkerRadius), and each marker gets the attributes of
  its row. Or add a [DotMark\<T\>](cref:Meziantou.Framework.Charts.DotMark{T}) with its own channel.
- A shape that counts rows gets none: the bars of a [BinTransform](cref:Meziantou.Framework.Charts.BinTransform),
  the hexagons of a [HexbinMark\<T\>](cref:Meziantou.Framework.Charts.HexbinMark{T}).
- The nodes of a tree have their own channel:
  [HierarchyMark\<T\>.DataAttributes](cref:Meziantou.Framework.Charts.HierarchyMark{T}.DataAttributes). The nodes of
  the Sankey, chord and network marks have none.

To build a scene by hand, set the attributes of an element with
[SetDataAttribute](cref:Meziantou.Framework.Charts.SceneElement.SetDataAttribute).
