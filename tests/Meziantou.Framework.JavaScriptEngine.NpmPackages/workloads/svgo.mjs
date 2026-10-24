// SVGO (the browser build): the default preset and configured plugins
import { optimize } from "svgo/browser";

const svg = `<?xml version="1.0" encoding="UTF-8" standalone="no"?>
<!-- Generator: an editor -->
<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" width="100px" height="100px" viewBox="0 0 100 100" version="1.1">
  <title>Icon</title>
  <desc>An icon</desc>
  <defs>
    <linearGradient id="gradient-unused"><stop offset="0" stop-color="#ff0000"/></linearGradient>
    <linearGradient id="gradient"><stop offset="0%" stop-color="#FF0000"/><stop offset="100%" stop-color="#0000ff"/></linearGradient>
    <style>.cls-1{fill:#ffffff;stroke:#000000}</style>
  </defs>
  <g id="Layer_1" fill="none">
    <g>
      <rect x="10.000" y="10.000" width="80.000" height="80.000" rx="0" fill="url(#gradient)"/>
      <circle class="cls-1" cx="50" cy="50" r="20.5000"/>
      <path d="M 10 10 L 20 10 L 20 20 L 10 20 Z" fill="#000000" stroke="none"/>
      <polygon points="50,5 95,95 5,95" opacity="1"/>
      <ellipse cx="50" cy="50" rx="10" ry="10"/>
      <line x1="0" y1="0" x2="100" y2="100" stroke="black"/>
    </g>
  </g>
</svg>`;

export default () => JSON.stringify({
  default: optimize(svg).data,
  multipass: optimize(svg, { multipass: true, floatPrecision: 2 }).data,
  pretty: optimize(svg, { js2svg: { pretty: true, indent: 2 }, plugins: ["preset-default", "removeDimensions", "sortAttrs"] }).data,
  custom: optimize(svg, {
    plugins: [{
      name: "uppercase-ids",
      fn: () => ({ element: { enter: node => { if (node.attributes.id) node.attributes.id = node.attributes.id.toUpperCase(); } } }),
    }],
  }).data,
}, null, 2);
