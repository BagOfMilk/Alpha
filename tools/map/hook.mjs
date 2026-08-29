#!/usr/bin/env node
/**
 * PostToolUse-хук: пересобирает карту взаимодействий, если правили то, из чего она строится.
 *
 * Читает payload хука со stdin, смотрит на путь изменённого файла и, если он попадает
 * под фильтр, запускает tools/map/build.mjs. Молчит, когда всё прошло гладко;
 * жалуется systemMessage-ом, если генератор упал или предупредил о расхождении.
 *
 * jq не нужен — весь разбор здесь, чтобы хук работал и на голой Windows.
 */

import fs from "node:fs";
import { spawnSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";

const ROOT = path.resolve(fileURLToPath(new URL("../..", import.meta.url)));

/** Что считаем поводом пересобрать карту. */
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
try { payload = JSON.parse(raw || "{}"); } catch { /* хук вызвали вручную — идём дальше */ }

const file =
  payload?.tool_input?.file_path ||
  payload?.tool_response?.filePath ||
  payload?.tool_input?.notebook_path ||
  "";

// Пустой путь = ручной запуск: собираем. Непустой, но не наш — выходим молча.
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
  // Генератор нашёл расхождение между авторским слоем карты и кодом — про это стоит знать.
  const notes = out.split("\n").filter((l) => /^\s*!/.test(l)).map((l) => l.replace(/^\s*!\s*/, ""));
  say(`Карта пересобрана, но с замечаниями: ${notes.join("; ")}`);
} else if (/~/.test(out)) {
  say(out.split("\n").find((l) => l.includes("~")).replace(/^\s*~\s*/, "Карта пересобрана. "));
}

function say(message) {
  process.stdout.write(JSON.stringify({ systemMessage: message, suppressOutput: true }) + "\n");
}
