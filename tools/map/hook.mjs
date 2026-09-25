#!/usr/bin/env node
/**
 * PostToolUse-хук: пересобирає карту взаємодій, якщо правили те, з чого вона будується.
 *
 * Читає payload хука зі stdin, дивиться на шлях зміненого файлу і, якщо він потрапляє
 * під фільтр, запускає tools/map/build.mjs. Мовчить, коли все пройшло гладко;
 * скаржиться systemMessage-ом, якщо генератор упав або попередив про розходження.
 *
 * jq не потрібен — весь розбір тут, щоб хук працював і на голому Windows.
 */

import fs from "node:fs";
import { spawnSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";

const ROOT = path.resolve(fileURLToPath(new URL("../..", import.meta.url)));

/** Що вважаємо приводом пересобрати карту. */
const WATCHED = /(Assets[\\/].+\.cs|docs[\\/][^\\/]+\.md|tools[\\/]map[\\/].+\.(?:json|html|mjs))$/i;

function readStdin() {
  try {
    return fs.readFileSync(0, "utf8");
  } catch {
    return "";
  }
}

const raw = readStdin();
let payload = {};
try { payload = JSON.parse(raw || "{}"); } catch { /* хук викликали вручну — йдемо далі */ }

const file =
  payload?.tool_input?.file_path ||
  payload?.tool_response?.filePath ||
  payload?.tool_input?.notebook_path ||
  "";

// Порожній шлях = ручний запуск: збираємо. Непорожній, але не наш — виходимо мовчки.
if (file && !WATCHED.test(file)) process.exit(0);

const r = spawnSync(process.execPath, [path.join(ROOT, "tools", "map", "build.mjs")], {
  cwd: ROOT,
  encoding: "utf8",
  timeout: 60_000,
});

const out = `${r.stdout || ""}${r.stderr || ""}`.trim();

if (r.status !== 0) {
  const first = out.split("\n").map((l) => l.trim())
    .find((l) => l && !/^at\s/.test(l) && !/^\^+$/.test(l)) || "неизвестная ошибка";
  say(`Карта взаимодействий не собралась: ${first}`);
} else if (/^\s*!/m.test(out)) {
  // Генератор знайшов розходження між авторським шаром карти і кодом — про це варто знати.
  const notes = out.split("\n").filter((l) => /^\s*!/.test(l)).map((l) => l.replace(/^\s*!\s*/, ""));
  say(`Карта пересобрана, но с замечаниями: ${notes.join("; ")}`);
} else if (/~/.test(out)) {
  say(out.split("\n").find((l) => l.includes("~")).replace(/^\s*~\s*/, "Карта пересобрана. "));
}

function say(message) {
  process.stdout.write(JSON.stringify({ systemMessage: message, suppressOutput: true }) + "\n");
}
