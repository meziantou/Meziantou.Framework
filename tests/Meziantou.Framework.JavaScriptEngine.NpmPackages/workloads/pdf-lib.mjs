// pdf-lib: creating a PDF (pages, standard fonts, text, shapes, images, form fields, metadata), saving it without
// random parts, then loading it back and inspecting it
import { PDFDocument, StandardFonts, rgb, degrees, PageSizes } from "pdf-lib";

// A 2x2 PNG (RGBA)
const png = Uint8Array.from(atob("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAABytg0kAAAAFklEQVR4AWP4z8DwnwEIGP4zMDCAAAA8AwP/FvGnDwAAAABJRU5ErkJggg=="), char => char.charCodeAt(0));

const hash = data => {
  let value = 0x811c9dc5;
  for (const byte of data) {
    value ^= byte;
    value = Math.imul(value, 0x01000193) >>> 0;
  }
  return value.toString(16);
};

export default async () => {
  const document = await PDFDocument.create({ updateMetadata: false });
  document.setTitle("Test document ✓");
  document.setAuthor("Engine");
  document.setCreationDate(new Date(Date.UTC(2020, 0, 1)));
  document.setModificationDate(new Date(Date.UTC(2020, 0, 2)));
  const helvetica = await document.embedFont(StandardFonts.Helvetica);
  const courier = await document.embedFont(StandardFonts.CourierBold);
  const image = await document.embedPng(png);
  const page = document.addPage(PageSizes.A4);
  page.drawText("Hello, PDF (é à ü)", { x: 50, y: 780, size: 24, font: helvetica, color: rgb(0.2, 0.4, 0.6) });
  page.drawText("Rotated", { x: 300, y: 500, size: 18, font: courier, rotate: degrees(30) });
  page.drawRectangle({ x: 50, y: 600, width: 200, height: 100, borderColor: rgb(1, 0, 0), borderWidth: 2, color: rgb(0.9, 0.9, 0.9), opacity: 0.5 });
  page.drawCircle({ x: 400, y: 650, size: 40, color: rgb(0, 1, 0) });
  page.drawLine({ start: { x: 50, y: 550 }, end: { x: 500, y: 550 }, thickness: 3, dashArray: [5, 5] });
  page.drawSvgPath("M 0,20 L 100,160 Q 130,200 150,120 C 190,-40 200,200 300,150 L 400,90", { x: 50, y: 400, borderColor: rgb(0, 0, 1) });
  page.drawImage(image, { x: 50, y: 200, width: 64, height: 64 });
  const form = document.getForm();
  form.createTextField("name").addToPage(page, { x: 300, y: 200, width: 200, height: 24 });
  form.getTextField("name").setText("Ada");
  form.createCheckBox("agree").addToPage(page, { x: 300, y: 150 });
  form.getCheckBox("agree").check();
  const second = document.addPage([300, 300]);
  second.drawText(helvetica.encodeText("x") ? "Width: " + helvetica.widthOfTextAtSize("Hello", 12).toFixed(3) : "", { x: 10, y: 10, size: 12, font: helvetica });

  const bytes = await document.save({ useObjectStreams: false });
  const compressed = await document.save({ useObjectStreams: true });
  const loaded = await PDFDocument.load(bytes, { updateMetadata: false });
  const copy = await PDFDocument.create({ updateMetadata: false });
  const [copied] = await copy.copyPages(loaded, [1]);
  copy.addPage(copied);
  const copyBytes = await copy.save({ useObjectStreams: false });
  return JSON.stringify({
    sizes: [bytes.length, compressed.length, copyBytes.length],
    hashes: [hash(bytes), hash(compressed), hash(copyBytes)],
    header: new TextDecoder().decode(bytes.subarray(0, 8)),
    pages: loaded.getPages().map(item => item.getSize()),
    title: loaded.getTitle(),
    author: loaded.getAuthor(),
    fields: loaded.getForm().getFields().map(field => field.getName() + ":" + field.constructor.name),
    name: loaded.getForm().getTextField("name").getText(),
    objects: loaded.context.enumerateIndirectObjects().length,
  }, null, 1);
};
