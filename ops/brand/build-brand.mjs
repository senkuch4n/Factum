// Genera los assets de marca de Factum (logo "Sello") para client/ y
// agent-ui/ a partir de la geometría fija de la SDD
// `Refactorizaciones/marca-comercial-sin-mpf-gfd.md` (secciones 6.1 y 6.2,
// con la resolución DP4: íconos de app en la variante CLARA). Uso:
//
//   cd ops/brand && npm ci && node build-brand.mjs
//
// Reglas:
// - Salida determinista: dos corridas seguidas dan los mismos bytes (sin
//   timestamps ni metadata variable; los números se escriben con decimales
//   fijos).
// - Sin dependencias del sistema (nada de rsvg-convert, iconutil ni
//   ImageMagick): el texto se convierte a trazos con opentype.js, el
//   rasterizado lo hace @resvg/resvg-js y los contenedores ICO/ICNS se
//   escriben a mano con PNG embebidos.

import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, join, relative, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { createHash } from "node:crypto";
import opentype from "opentype.js";
import { Resvg } from "@resvg/resvg-js";

const HERE = dirname(fileURLToPath(import.meta.url));
const ROOT = resolve(HERE, "..", "..");
const FONT_PATH = join(HERE, "fonts", "Sora-Bold.ttf");

// ── Geometría y colores (SDD 6.1) ────────────────────────────────────────
const MARK_SIZE = 64;
const MARK_RX = 14;
const GLYPH_PATH = "M20 14H40L46 20V23H30V29H40V37H30V50H20Z";

const DARK = { accent: "#7fd34e", glyph: "#0b0c0e", text: "#f3f5f7" };
const LIGHT = { accent: "#2f6f12", glyph: "#ffffff", text: "#0e1013" };
/** DP4 (2026-10-01): los íconos de app usan la variante clara. */
const APP_ICON = LIGHT;

// Proporciones del artboard "A · Sello": marca 60, separación 18, texto 52.
const TEXT_X = MARK_SIZE + (18 / 60) * MARK_SIZE; // 83.2
const FONT_SIZE = 55.47; // 52/60 · 64, redondeado como dice la SDD
const LETTER_SPACING_EM = -0.02;
const WORDMARK = "Factum";

// ── Helpers ──────────────────────────────────────────────────────────────
const fmt = (n) => {
  const s = n.toFixed(2);
  return s.includes(".") ? s.replace(/\.?0+$/, "") : s;
};

function svgDoc(width, height, body) {
  return (
    `<svg xmlns="http://www.w3.org/2000/svg" width="${fmt(width)}" height="${fmt(height)}" ` +
    `viewBox="0 0 ${fmt(width)} ${fmt(height)}">${body}</svg>\n`
  );
}

/** Marca (cuadrado redondeado + "F"). `rx = 0` para íconos a sangre completa. */
function markBody({ accent, glyph }, { rx = MARK_RX } = {}) {
  const rect = rx > 0
    ? `<rect width="${MARK_SIZE}" height="${MARK_SIZE}" rx="${rx}" fill="${accent}"/>`
    : `<rect width="${MARK_SIZE}" height="${MARK_SIZE}" fill="${accent}"/>`;
  return `${rect}<path d="${GLYPH_PATH}" fill="${glyph}"/>`;
}

function markSvg(variant, opts) {
  return svgDoc(MARK_SIZE, MARK_SIZE, markBody(variant, opts));
}

/**
 * Marca escalada y centrada dentro de un lienzo de 64 con margen
 * transparente (grilla de íconos de macOS: 824/1024).
 */
function insetMarkSvg(variant, scale) {
  const offset = (MARK_SIZE * (1 - scale)) / 2;
  return svgDoc(
    MARK_SIZE,
    MARK_SIZE,
    `<g transform="translate(${fmt(offset)} ${fmt(offset)}) scale(${scale})">${markBody(variant)}</g>`,
  );
}

/** Rectángulo redondeado como path (para combinarlo con evenodd). */
function roundedRectPath(size, r) {
  const s = size;
  return (
    `M${r} 0H${s - r}A${r} ${r} 0 0 1 ${s} ${r}V${s - r}A${r} ${r} 0 0 1 ${s - r} ${s}` +
    `H${r}A${r} ${r} 0 0 1 0 ${s - r}V${r}A${r} ${r} 0 0 1 ${r} 0Z`
  );
}

/** Template monocromo de macOS: silueta negra con la "F" calada (D9 B). */
function trayTemplateSvg() {
  return svgDoc(
    MARK_SIZE,
    MARK_SIZE,
    `<path d="${roundedRectPath(MARK_SIZE, MARK_RX)}${GLYPH_PATH}" fill="#000000" fill-rule="evenodd"/>`,
  );
}

/** Logo horizontal: marca + "Factum" (Sora 700) convertido a trazos. */
function horizontalSvg(font, variant) {
  const fs = FONT_SIZE;
  const upm = font.unitsPerEm;
  const A = (font.tables.hhea.ascender / upm) * fs;
  const D = (Math.abs(font.tables.hhea.descender) / upm) * fs;
  // line-height: 1 centrado en la marca (como en CSS).
  const top = MARK_SIZE / 2 - fs / 2;
  const baseline = top + (fs - (A + D)) / 2 + A;

  const options = { kerning: true, letterSpacing: LETTER_SPACING_EM };
  const textPath = font.getPath(WORDMARK, TEXT_X, baseline, fs, options);
  // forEachGlyph devuelve la x después del último glifo, incluido el
  // tracking final: se lo descuenta (SDD 6.1).
  const endX = font.forEachGlyph(WORDMARK, TEXT_X, baseline, fs, options, () => {}) -
    LETTER_SPACING_EM * fs;
  const width = Math.ceil(endX * 2) / 2;

  const body =
    markBody(variant) +
    `<path d="${textPath.toPathData(2)}" fill="${variant.text}"/>`;
  return svgDoc(width, MARK_SIZE, body);
}

function rasterize(svg, size) {
  const resvg = new Resvg(svg, {
    fitTo: { mode: "width", value: size },
    background: "rgba(0,0,0,0)",
    font: { loadSystemFonts: false },
  });
  return Buffer.from(resvg.render().asPng());
}

/** ICO con PNG embebidos (ICONDIR de 6 bytes + entradas de 16 bytes). */
function buildIco(entries) {
  const header = Buffer.alloc(6);
  header.writeUInt16LE(0, 0); // reservado
  header.writeUInt16LE(1, 2); // tipo: ícono
  header.writeUInt16LE(entries.length, 4);

  const dir = Buffer.alloc(16 * entries.length);
  let offset = 6 + dir.length;
  entries.forEach(({ size, png }, i) => {
    const o = i * 16;
    dir.writeUInt8(size >= 256 ? 0 : size, o); // ancho (0 = 256)
    dir.writeUInt8(size >= 256 ? 0 : size, o + 1); // alto
    dir.writeUInt8(0, o + 2); // paleta
    dir.writeUInt8(0, o + 3); // reservado
    dir.writeUInt16LE(1, o + 4); // planos
    dir.writeUInt16LE(32, o + 6); // bits por pixel
    dir.writeUInt32LE(png.length, o + 8);
    dir.writeUInt32LE(offset, o + 12);
    offset += png.length;
  });
  return Buffer.concat([header, dir, ...entries.map((e) => e.png)]);
}

/** ICNS con bloques PNG (`tipo` + longitud big-endian + datos). */
function buildIcns(entries) {
  const blocks = entries.map(({ type, png }) => {
    const head = Buffer.alloc(8);
    head.write(type, 0, 4, "ascii");
    head.writeUInt32BE(8 + png.length, 4);
    return Buffer.concat([head, png]);
  });
  const total = 8 + blocks.reduce((n, b) => n + b.length, 0);
  const head = Buffer.alloc(8);
  head.write("icns", 0, 4, "ascii");
  head.writeUInt32BE(total, 4);
  return Buffer.concat([head, ...blocks]);
}

const written = [];
function out(relPath, data) {
  const abs = join(ROOT, relPath);
  mkdirSync(dirname(abs), { recursive: true });
  writeFileSync(abs, data);
  written.push({ path: relative(ROOT, abs), data: Buffer.isBuffer(data) ? data : Buffer.from(data) });
}

// ── Generación ───────────────────────────────────────────────────────────
const fontBuffer = readFileSync(FONT_PATH);
const font = opentype.parse(
  fontBuffer.buffer.slice(fontBuffer.byteOffset, fontBuffer.byteOffset + fontBuffer.byteLength),
);

const appMark = markSvg(APP_ICON);
const appMarkFullBleed = markSvg(APP_ICON, { rx: 0 });
const appMarkMac = insetMarkSvg(APP_ICON, 824 / 1024);
const tray = trayTemplateSvg();

// client/public
out("client/public/logo-theme-dark.svg", horizontalSvg(font, DARK));
out("client/public/logo-theme-white.svg", horizontalSvg(font, LIGHT));
out("client/public/logo-mark-dark.svg", markSvg(DARK));
out("client/public/logo-mark-white.svg", markSvg(LIGHT));
out("client/public/icon.svg", appMark);
out(
  "client/public/logo-app.ico",
  buildIco([16, 32, 48].map((size) => ({ size, png: rasterize(appMark, size) }))),
);
out("client/public/icon-512.png", rasterize(appMark, 512));
out("client/public/apple-icon.png", rasterize(appMarkFullBleed, 180));

// agent-ui/resources
out("agent-ui/resources/icon.png", rasterize(appMark, 1024));
out(
  "agent-ui/resources/icon.ico",
  buildIco([16, 24, 32, 48, 64, 128, 256].map((size) => ({ size, png: rasterize(appMark, size) }))),
);
out(
  "agent-ui/resources/icon.icns",
  buildIcns(
    [
      ["icp4", 16],
      ["icp5", 32],
      ["icp6", 64],
      ["ic07", 128],
      ["ic08", 256],
      ["ic09", 512],
      ["ic10", 1024],
      ["ic11", 32],
      ["ic12", 64],
      ["ic13", 256],
      ["ic14", 512],
    ].map(([type, size]) => ({ type, png: rasterize(appMarkMac, size) })),
  ),
);
out("agent-ui/resources/tray/trayTemplate.png", rasterize(tray, 16));
out("agent-ui/resources/tray/trayTemplate@2x.png", rasterize(tray, 32));
out("agent-ui/resources/tray/tray.png", rasterize(appMark, 32));
out("agent-ui/resources/tray/tray@2x.png", rasterize(appMark, 64));
out(
  "agent-ui/resources/tray/tray.ico",
  buildIco([16, 24, 32, 48].map((size) => ({ size, png: rasterize(appMark, size) }))),
);

// Pie de atribución del informe (se imprime sobre papel blanco: variante clara).
out("server/src/Factum.Backend/Templates/factum-sello.png", rasterize(markSvg(LIGHT), 256));

for (const { path, data } of written) {
  const sha = createHash("sha256").update(data).digest("hex").slice(0, 16);
  console.log(`${sha}  ${String(data.length).padStart(7)}  ${path}`);
}
