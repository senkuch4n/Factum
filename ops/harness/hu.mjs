#!/usr/bin/env node
// ops/harness/hu.mjs — Mueve HU en el Project "Factum – HU" de GitHub.
//
// El Project es la fuente de verdad del estado de cada HU (reemplaza a
// backlog.json desde 2026-10-06). Este script es la única forma en que el
// orquestador cambia la Fase de una HU: así Status (columna) y Fase quedan
// siempre coherentes, el contador de reintentos se respeta y se controla
// "una HU activa por persona".
//
// Uso:
//   node ops/harness/hu.mjs ver [N]                       lista las HU abiertas (o el detalle de #N)
//   node ops/harness/hu.mjs alta <slug> "<título>" <toca>  crea el issue y lo pone en Backlog
//   node ops/harness/hu.mjs tomar N                       se asigna #N a quien corre el script
//   node ops/harness/hu.mjs fase N <fase>                 cambia la Fase (y la columna) de #N
//   node ops/harness/hu.mjs comentar N "<texto>"          deja un comentario en el issue
//
// <toca>: backend | client | agent-ui | backend+client | backend+agent-ui | client+agent-ui | todo
// Requiere `gh` logueado con scope `project`.

import { execFileSync } from "node:child_process";

const OWNER = "senkuch4n";
const REPO = "Factum";
const PROJECT_NUMBER = 3;
const PROJECT_ID = "PVT_kwHOBqZxdM4Bl7tx";
const MAX_REINTENTOS = 2;

// Fase del arnés -> columna (Status) del kanban.
const COLUMNA = {
  no_afinada: "Backlog",
  afinando: "Afinando",
  afinada_pendiente_validacion: "Por validar",
  validada: "Lista para dev",
  en_arquitectura: "Lista para dev",
  arquitectura_lista: "Lista para dev",
  implementando: "En curso",
  rechazada_reintentando: "En curso",
  en_revision: "En revisión",
  aprobada: "Hecho",
  bloqueada: "Bloqueada",
};
// Fases que cuentan para "una HU activa por persona".
const ACTIVAS = ["afinando", "en_arquitectura", "implementando", "rechazada_reintentando", "en_revision"];

function gh(...args) {
  return execFileSync("gh", args, { encoding: "utf8", stdio: ["ignore", "pipe", "pipe"] });
}
function graphql(query, vars = {}) {
  const args = ["api", "graphql", "-f", `query=${query}`];
  for (const [k, v] of Object.entries(vars)) args.push(typeof v === "number" ? "-F" : "-f", `${k}=${v}`);
  return JSON.parse(gh(...args)).data;
}
function die(msg) {
  console.error(`[hu] ${msg}`);
  process.exit(1);
}

let camposCache;
function campos() {
  if (!camposCache) {
    const data = JSON.parse(gh("project", "field-list", String(PROJECT_NUMBER), "--owner", OWNER, "--format", "json"));
    camposCache = Object.fromEntries(data.fields.map((f) => [f.name, f]));
  }
  return camposCache;
}
function opcion(campo, nombre) {
  const o = (campos()[campo].options || []).find((x) => x.name === nombre);
  if (!o) die(`El campo ${campo} no tiene la opción "${nombre}"`);
  return o.id;
}

const ITEM_FIELDS = `
  number title state url assignees(first: 5) { nodes { login } }
  projectItems(first: 10) { nodes { id project { number }
    fieldValues(first: 20) { nodes {
      ... on ProjectV2ItemFieldSingleSelectValue { name field { ... on ProjectV2FieldCommon { name } } }
      ... on ProjectV2ItemFieldTextValue { text field { ... on ProjectV2FieldCommon { name } } }
      ... on ProjectV2ItemFieldNumberValue { number field { ... on ProjectV2FieldCommon { name } } }
    } } } }`;

// Lee el issue a través de sus projectItems (no del listado del Project, que
// tarda en indexar los ítems recién agregados).
function normalizar(issue) {
  const item = issue.projectItems.nodes.find((n) => n.project.number === PROJECT_NUMBER);
  const v = {};
  for (const n of item?.fieldValues.nodes || []) if (n.field) v[n.field.name] = n.name ?? n.text ?? n.number;
  return {
    number: issue.number, title: issue.title, state: issue.state, url: issue.url,
    assignees: issue.assignees.nodes.map((a) => a.login),
    itemId: item?.id, status: v.Status, fase: v.Fase, toca: v.Toca, slug: v.Slug, reintentos: v.Reintentos ?? 0,
  };
}
function leer(n) {
  const d = graphql(`query($o:String!,$r:String!,$n:Int!){repository(owner:$o,name:$r){issue(number:$n){${ITEM_FIELDS}}}}`,
    { o: OWNER, r: REPO, n: Number(n) });
  if (!d.repository.issue) die(`No existe el issue #${n}`);
  const hu = normalizar(d.repository.issue);
  if (!hu.itemId) die(`El issue #${n} no está en el Project ${PROJECT_NUMBER}`);
  return hu;
}
function abiertas(extra = "") {
  const q = `repo:${OWNER}/${REPO} is:issue is:open label:hu ${extra}`;
  const d = graphql(`query($q:String!){search(type:ISSUE,query:$q,first:100){nodes{... on Issue{${ITEM_FIELDS}}}}}`, { q });
  return d.search.nodes.map(normalizar).filter((h) => h.itemId);
}
function setCampo(itemId, campo, valor) {
  const args = ["project", "item-edit", "--project-id", PROJECT_ID, "--id", itemId, "--field-id", campos()[campo].id];
  if (typeof valor === "number") args.push("--number", String(valor));
  else if (campos()[campo].options) args.push("--single-select-option-id", opcion(campo, valor));
  else args.push("--text", valor);
  gh(...args);
}
function linea(h) {
  const quien = h.assignees.length ? h.assignees.join(",") : "sin asignar";
  return `#${h.number}  [${h.status}] ${h.fase}  ${h.slug}  (${quien}, toca ${h.toca}, reintentos ${h.reintentos})`;
}

const [cmd, ...args] = process.argv.slice(2);
switch (cmd) {
  case "ver": {
    if (args[0]) {
      const h = leer(args[0]);
      console.log(linea(h));
      console.log(`${h.title}\n${h.url}`);
    } else {
      const todas = abiertas();
      if (!todas.length) console.log("No hay HU abiertas.");
      for (const h of todas) console.log(linea(h));
    }
    break;
  }
  case "alta": {
    const [slug, titulo, toca] = args;
    if (!slug || !titulo || !toca) die('Uso: alta <slug> "<título>" <toca>');
    if (!/^[a-z0-9]+(-[a-z0-9]+)*$/.test(slug)) die("El slug tiene que ser kebab-case");
    if (abiertas().some((h) => h.slug === slug)) die(`Ya hay una HU abierta con slug ${slug}`);
    opcion("Toca", toca);
    const labels = ["hu", ...(toca === "todo" ? ["backend", "client", "agent-ui"] : toca.split("+"))];
    const body = `**Slug:** \`${slug}\`\n\n${titulo}\n\n**Artefactos:** RDD \`docs/hu-${slug}.md\` (pendiente) · SDD \`Refactorizaciones/${slug}.md\` (pendiente) · rama \`feat/${slug}\``;
    const url = gh("issue", "create", "-R", `${OWNER}/${REPO}`, "--title", `[HU] ${titulo}`, "--label", labels.join(","), "--body", body).trim();
    const itemId = JSON.parse(gh("project", "item-add", String(PROJECT_NUMBER), "--owner", OWNER, "--url", url, "--format", "json")).id;
    setCampo(itemId, "Status", "Backlog");
    setCampo(itemId, "Fase", "no_afinada");
    setCampo(itemId, "Toca", toca);
    setCampo(itemId, "Slug", slug);
    setCampo(itemId, "Reintentos", 0);
    console.log(`alta -> ${url}`);
    break;
  }
  case "tomar": {
    const h = leer(args[0]);
    gh("issue", "edit", String(h.number), "-R", `${OWNER}/${REPO}`, "--add-assignee", "@me");
    console.log(`#${h.number} asignada a vos`);
    break;
  }
  case "fase": {
    const [n, fase] = args;
    if (!COLUMNA[fase]) die(`Fase inválida: ${fase}. Válidas: ${Object.keys(COLUMNA).join(", ")}`);
    const h = leer(n);
    if (ACTIVAS.includes(fase)) {
      if (!h.assignees.length) die(`#${h.number} no tiene asignado: primero 'tomar ${h.number}'`);
      for (const quien of h.assignees) {
        const otras = abiertas(`assignee:${quien}`).filter((o) => o.number !== h.number && ACTIVAS.includes(o.fase));
        if (otras.length) die(`${quien} ya tiene una HU activa: ${otras.map(linea).join("; ")}`);
      }
    }
    let reintentos = h.reintentos;
    let destino = fase;
    if (fase === "rechazada_reintentando") {
      reintentos += 1;
      if (reintentos > MAX_REINTENTOS) {
        destino = "bloqueada";
        console.log(`[hu] Tercer rechazo de #${h.number}: queda bloqueada. Avisar al humano.`);
      }
      setCampo(h.itemId, "Reintentos", reintentos);
    }
    setCampo(h.itemId, "Fase", destino);
    setCampo(h.itemId, "Status", COLUMNA[destino]);
    console.log(`#${h.number} ${h.fase} -> ${destino} [${COLUMNA[destino]}]`);
    break;
  }
  case "comentar": {
    const [n, texto] = args;
    if (!texto) die('Uso: comentar N "<texto>"');
    gh("issue", "comment", String(n), "-R", `${OWNER}/${REPO}`, "--body", texto);
    console.log(`comentario en #${n}`);
    break;
  }
  default:
    die("Comandos: ver [N] | alta <slug> \"<título>\" <toca> | tomar N | fase N <fase> | comentar N \"<texto>\"");
}
