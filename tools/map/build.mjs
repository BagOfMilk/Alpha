#!/usr/bin/env node
/**
 * Пересобирает docs/interaction-map.html.
 *
 * Что откуда берётся:
 *   tools/map/graph.json  — авторский слой: код-узлы, дизайн-слой GDD, ручные разрывы.
 *   Assets/**\/*.cs        — всё, что можно вычитать из кода: секции, слоты, склонности,
 *                           ресурсы, архетипы, их числа, прогон первого дня и автопроверки.
 *   docs/*.md, ProjectSettings — сверка документации с кодом.
 *
 * Запуск:  node tools/map/build.mjs [--check]
 *   --check  ничего не пишет, только сообщает, устарел ли файл (код возврата 1).
 */

import fs from "node:fs";
import path from "node:path";
import crypto from "node:crypto";
import { execFileSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const ROOT = path.resolve(fileURLToPath(new URL("../..", import.meta.url)));
const rel = (...p) => path.join(ROOT, ...p);
const read = (p) => fs.readFileSync(rel(p), "utf8");
const exists = (p) => fs.existsSync(rel(p));

const CHECK_ONLY = process.argv.includes("--check");
const warnings = [];
const warn = (m) => warnings.push(m);

/* ──────────────────────────── утилиты ──────────────────────────── */

/** Округление как в C# Math.Round — к ближайшему чётному на .5 */
function roundHalfEven(x) {
  const f = Math.floor(x);
  const d = x - f;
  if (d > 0.5) return f + 1;
  if (d < 0.5) return f;
  return f % 2 === 0 ? f : f + 1;
}
const lineOf = (text, index) => text.slice(0, index).split("\n").length;
const cap = (s) => (s ? s[0].toUpperCase() + s.slice(1) : s);
const sentence = (s) => {
  if (!s) return "";
  const t = cap(s.trim());
  return /[.!?]$/.test(t) ? t : t + ".";
};
const num = (n) => (n === undefined || n === null || Number.isNaN(n) ? "—" : String(n));
/** Коэффициент как в BALANCE.md: всегда хотя бы один знак после точки. */
const coef = (n) =>
  n === undefined || n === null || Number.isNaN(n) ? "—" : Number.isInteger(n) ? n.toFixed(1) : String(n);

/**
 * Безопасные агрегаты. На пустом наборе Math.max(...[]) даёт -Infinity, а arr.at(-1)
 * даёт undefined — и генератор падает ровно тогда, когда контент временно пуст
 * посреди рефакторинга. Каждый агрегат ниже возвращает запасное значение.
 */
const maxOf = (arr, fallback = 0) => (arr.length ? Math.max(...arr) : fallback);
const minOf = (arr, fallback = 0) => (arr.length ? Math.min(...arr) : fallback);
const first = (arr, fallback = null) => (arr.length ? arr[0] : fallback);
const last = (arr, fallback = null) => (arr.length ? arr[arr.length - 1] : fallback);

/* ──────────────────────── сбор исходников ──────────────────────── */

function walk(dir, out = []) {
  for (const e of fs.readdirSync(rel(dir), { withFileTypes: true })) {
    const p = `${dir}/${e.name}`;
    if (e.isDirectory()) walk(p, out);
    else if (e.name.endsWith(".cs")) out.push(p);
  }
  return out;
}
const CS_FILES = walk("Assets").sort();
const CS = Object.fromEntries(CS_FILES.map((p) => [p, read(p)]));
const CORE_FILES = CS_FILES.filter((p) => !p.includes("/Tests/"));
const allCode = CS_FILES.map((p) => CS[p]).join("\n");
const coreCode = CORE_FILES.map((p) => CS[p]).join("\n");

const SRC = {
  defaults: "Assets/_Project/Scripts/Core/DefaultContent.cs",
  balance: "Assets/_Project/Scripts/Core/Balance/BalanceConfig.cs",
  demo: "Assets/_Project/Scripts/Gameplay/BaseGameDemo.cs",
  asset: "Assets/_Project/Scripts/Gameplay/BalanceConfigAsset.cs",
  statType: "Assets/_Project/Scripts/Core/Stats/StatType.cs",
  resType: "Assets/_Project/Scripts/Core/Economy/ResourceType.cs",
  section: "Assets/_Project/Scripts/Core/Base/BaseSectionType.cs",
  companion: "Assets/_Project/Scripts/Core/Characters/Companion.cs",
  baseState: "Assets/_Project/Scripts/Core/Base/BaseState.cs",
  ledger: "Assets/_Project/Scripts/Core/Economy/ResourceLedger.cs",
};
for (const [k, p] of Object.entries(SRC)) if (!exists(p)) warn(`нет файла ${p} (ключ ${k}) — карта соберётся неполной`);

/* ──────────────────────────── парсеры ──────────────────────────── */

/** enum → [{name, value, comment, line}] */
function parseEnum(file, name) {
  const text = CS[file];
  if (!text) return [];
  const m = text.match(new RegExp(`public enum\\s+${name}\\s*\\r?\\n?\\s*\\{([\\s\\S]*?)\\n\\s*\\}`));
  if (!m) {
    warn(`не разобрал enum ${name} в ${file}`);
    return [];
  }
  const startLine = lineOf(text, m.index);
  const out = [];
  m[1].split("\n").forEach((raw, i) => {
    const mm = raw.match(/^\s*(\w+)\s*=\s*(\d+)\s*,?\s*(?:\/\/\s*(.*?))?\s*$/);
    if (mm) out.push({ name: mm[1], value: +mm[2], comment: (mm[3] || "").trim(), line: startLine + 1 + i });
  });
  return out;
}

const statTypes = parseEnum(SRC.statType, "StatType");
const resTypes = parseEnum(SRC.resType, "ResourceType");
const sectionTypes = parseEnum(SRC.section, "BaseSectionType");
const statusTypes = parseEnum(SRC.companion, "CompanionStatus");

/** Боевые статы держатся в диапазоне 1–10, ролевые склонности с 20. */
const isCombatStat = (n) => {
  const s = statTypes.find((x) => x.name === n);
  return s ? s.value < 20 : false;
};

/** Архетипы из DefaultContent */
function parseArchetypes() {
  const text = CS[SRC.defaults] || "";
  const out = [];
  const re = /public static CompanionArchetype (\w+)\(\)\s*\r?\n\s*\{([\s\S]*?)\n {8}\}/g;
  let m;
  while ((m = re.exec(text))) {
    const body = m[2];
    const head = body.match(/new CompanionArchetype\("([^"]+)",\s*"([^"]+)"\)/);
    if (!head) continue;
    const stats = {}, growth = {};
    for (const s of body.matchAll(/BaseStats\.Set\(StatType\.(\w+),\s*(-?\d+)\)/g)) stats[s[1]] = +s[2];
    for (const g of body.matchAll(/Growth\.SetWeight\(StatType\.(\w+),\s*([\d.]+)\)/g)) growth[g[1]] = +g[2];
    out.push({ method: m[1], id: head[1], name: head[2], stats, growth, line: lineOf(text, m.index) });
  }
  if (!out.length) warn("не разобрал ни одного архетипа в DefaultContent.cs");
  return out;
}

/** Слоты из DefaultContent */
function parseSlots() {
  const text = CS[SRC.defaults] || "";
  const out = [];
  const re = /new AssignmentSlotDefinition\("([^"]+)",\s*"([^"]+)",\s*BaseSectionType\.(\w+)\)\s*\r?\n\s*\{([\s\S]*?)\n\s*\}\)/g;
  let m;
  while ((m = re.exec(text))) {
    const b = m[4];
    const f = (k, d = null) => {
      const mm = b.match(new RegExp(`${k}\\s*=\\s*([^,\\n]+)`));
      return mm ? mm[1].trim() : d;
    };
    const enumVal = (k, d) => {
      const v = f(k);
      return v ? v.split(".").pop() : d;
    };
    out.push({
      id: m[1],
      name: m[2],
      section: m[3],
      line: lineOf(text, m.index),
      kind: enumVal("OutputKind", "Resource"),
      resource: enumVal("OutputResource", "None"),
      passive: (f("PassiveBonusId") || "").replace(/"/g, "") || null,
      primary: enumVal("PrimaryAptitude", "None"),
      secondary: enumVal("SecondaryAptitude", "None"),
      base: +(f("BaseOutput", "0")),
      k1: +(f("OutputPerPrimaryPoint", "1.0")),
      k2: +(f("OutputPerSecondaryPoint", "0.5")),
      unlocked: f("UnlockedByDefault", "true") !== "false",
    });
  }
  if (!out.length) warn("не разобрал ни одного слота в DefaultContent.cs");
  return out;
}

/** Числа баланса */
function parseBalance() {
  const text = CS[SRC.balance] || "";
  const out = {};
  for (const m of text.matchAll(/public\s+(?:double|int)\s+(\w+)\s*=\s*([\d.]+)\s*;/g)) out[m[1]] = +m[2];
  return out;
}

/** Стартовая расстановка и запасы из демки */
function parseDemo() {
  const text = CS[SRC.demo] || "";
  const list = text.match(/assignments\s*=\s*new[\s\S]*?\{([\s\S]*?)\}/);
  const assignments = [];
  if (list) for (const m of list[1].matchAll(/\("([^"]+)",\s*"([^"]+)"\)/g)) assignments.push({ instance: m[1], slot: m[2] });
  const starting = {};
  for (const m of text.matchAll(/ledger\.Add\(ResourceType\.(\w+),\s*(\d+)\)/g)) starting[m[1]] = +m[2];
  const cycles = +(text.match(/cyclesToSimulate\s*=\s*(\d+)/)?.[1] ?? 10);
  return { assignments, starting, cycles };
}

const archetypes = parseArchetypes();
const slots = parseSlots();
const balance = parseBalance();
const demo = parseDemo();
const globalMult = balance.GlobalProductionMultiplier ?? 1;

/* ─────────────────── генерация узлов слоя «База» ─────────────────── */

const G = JSON.parse(read("tools/map/graph.json"));
const bind = G.bind || {};
const label = (k, fallback) => (G.labels && G.labels[k]) || fallback;
const note = (id) => (G.notes && G.notes[id]) || "";
const withNote = (id, text) => [text, note(id)].filter(Boolean).join(" ");

const idSection = (n) => (bind.sections && bind.sections[n]) || "b.sec." + n.toLowerCase();
const idSlot = (n) => (bind.slots && bind.slots[n]) || "b.slot." + n;
const idArch = (n) => (bind.archetypes && bind.archetypes[n]) || "b.arch." + n;
const idStat = (n) => "b.st." + n;
const idRes = (n) => "b.res." + n;
const idOut = (n) => "b.out." + n;

const nodes = [...G.nodes];
const edges = [...G.edges];
const push = (n) => nodes.push(n);
const link = (s, t, l, tag) => edges.push([s, t, l, tag]);

/** Числовая справка узла — считается из исходников, дописывается седьмым полем. */
const facts = {};
const fact = (id, ...parts) => {
  const s = parts.filter(Boolean).join(" ");
  if (s) facts[id] = facts[id] ? facts[id] + " " + s : s;
};
const plural = (n, one, few, many) => {
  const a = Math.abs(n) % 100, b = a % 10;
  return `${n} ${a > 10 && a < 20 ? many : b === 1 ? one : b >= 2 && b <= 4 ? few : many}`;
};

// секции
const slotsBySection = {};
slots.forEach((s) => (slotsBySection[s.section] ||= []).push(s));
for (const sec of sectionTypes) {
  if (sec.name === "None") continue;
  const id = idSection(sec.name);
  const own = slotsBySection[sec.name] || [];
  push([id, label(sec.name, sec.name), "base", "section", "Секции", `BaseSectionType.${sec.name}`,
        withNote(id, sentence(sec.comment.replace(/^[^:]+:\s*/, "")))]);
  fact(id, `Значение enum ${sec.value}.`,
       own.length
         ? `${plural(own.length, "слот", "слота", "слотов")}, суммарная база ${own.reduce((s, x) => s + x.base, 0)}/цикл` +
           (own.some((x) => !x.unlocked) ? `, из них закрытых ${own.filter((x) => !x.unlocked).length}.` : ".")
         : `Слотов 0 — назначить некого.`);
}

// склонности (только те, что где-то участвуют)
const usedStats = new Set();
slots.forEach((s) => { if (s.primary !== "None") usedStats.add(s.primary); if (s.secondary !== "None") usedStats.add(s.secondary); });
archetypes.forEach((a) => { Object.keys(a.stats).forEach((k) => usedStats.add(k)); Object.keys(a.growth).forEach((k) => usedStats.add(k)); });
const statMax = {};
archetypes.forEach((a) => Object.entries(a.stats).forEach(([k, v]) => (statMax[k] = Math.max(statMax[k] ?? 0, v))));

for (const st of statTypes) {
  if (st.name === "None" || !usedStats.has(st.name)) continue;
  const id = idStat(st.name);
  const group = isCombatStat(st.name) ? "Боевые статы" : "Склонности";
  push([id, st.name, "base", "stat", group, `StatType.${st.name}`, withNote(id, sentence(st.comment))]);

  const asPrimary = slots.filter((s) => s.primary === st.name);
  const asSecondary = slots.filter((s) => s.secondary === st.name);
  const top = archetypes.filter((a) => a.stats[st.name]).sort((a, b) => b.stats[st.name] - a.stats[st.name])[0];
  const growers = archetypes.filter((a) => a.growth[st.name]);
  fact(id, `Значение enum ${st.value}.`,
       asPrimary.length || asSecondary.length
         ? `Primary в ${asPrimary.length}, Secondary в ${asSecondary.length} слотах` +
           (asPrimary.length ? ` (коэффициенты ${[...new Set(asPrimary.map((s) => coef(s.k1)))].join(", ")}).` : ".")
         : `В формулах выработки не участвует.`,
       top ? `Максимум у архетипов ${top.stats[st.name]} (${top.name}).` : `У стартовых архетипов не задан.`,
       growers.length ? `Растёт у ${plural(growers.length, "архетипа", "архетипов", "архетипов")}: ${growers.map((a) => `${a.name} ×${coef(a.growth[st.name])}`).join(", ")}.` : null);
}

// ресурсы
const producedRes = new Set(slots.filter((s) => s.kind === "Resource").map((s) => s.resource));
const spendCalls = [...allCode.matchAll(/TrySpend\(ResourceType\.(\w+)/g)].map((m) => m[1]);
for (const r of resTypes) {
  if (r.name === "None") continue;
  const id = idRes(r.name);
  const from = slots.filter((s) => s.kind === "Resource" && s.resource === r.name);
  const best = from.map((s) => maxOf(archetypes.map((a) =>
    roundHalfEven(s.base + (a.stats[s.primary] ?? 0) * s.k1 + (a.stats[s.secondary] ?? 0) * s.k2)), s.base));
  push([id, r.name, "base", "res", "Ресурсы", `ResourceType.${r.name}`, withNote(id, sentence(r.comment))]);
  fact(id, `Значение enum ${r.value}.`,
       from.length
         ? `Производят ${plural(from.length, "слот", "слота", "слотов")} (${from.map((s) => `«${s.name}» база ${s.base}`).join(", ")}), потолок на стартовых статах ${maxOf(best)}/цикл.`
         : `Производителей 0.`,
       spendCalls.includes(r.name)
         ? `Тратится: ${plural(spendCalls.filter((x) => x === r.name).length, "вызов", "вызова", "вызовов")} TrySpend.`
         : `Трат 0 — только копится.`);
}

// именованные выходы
const passiveIds = [...new Set(slots.filter((s) => s.kind === "Passive" && s.passive).map((s) => s.passive))];
for (const p of passiveIds) {
  const from = slots.filter((s) => s.passive === p);
  push([idOut(p), label(p, p), "base", "out", "Выходы", `PassiveBonusId = "${p}"`,
        withNote(idOut(p), `Пассивный бонус, копится в отчёте дня.`)]);
  fact(idOut(p), `Источников ${from.length}: ${from.map((s) => `«${s.name}» база ${s.base}`).join(", ")}.`,
       new RegExp(`"${p}"`).test(allCode.replace(CS[SRC.defaults] || "", "")) ? null : `Потребителей 0 — ключ «${p}» больше нигде в коде не встречается.`);
}
if (slots.some((s) => s.kind === "Healing")) {
  const heal = slots.filter((s) => s.kind === "Healing");
  push([idOut("Healing"), label("Healing", "Healing"), "base", "out", "Выходы", "SlotOutputKind.Healing",
        withNote(idOut("Healing"), "Лечебные очки за цикл.")]);
  fact(idOut("Healing"), `Источников ${heal.length}.`,
       `Плюс естественная регенерация ${coef(balance.BaseHealingPerCycle ?? 0)} очка всем раненым за цикл — она идёт и без лазарета.`);
}

// слоты
const scaleMismatches = [];
const KIND_TEXT = { Resource: "Ресурс в кошелёк", Healing: "Лечение раненых", Passive: "Пассивный бонус" };
for (const s of slots) {
  const id = idSlot(s.id);
  const target = s.kind === "Resource" ? s.resource : s.kind === "Passive" ? s.passive : "Healing";
  const parts = [`${KIND_TEXT[s.kind] || s.kind}: ${target}.`, `Base ${num(s.base)}`];
  if (s.primary !== "None") parts.push(`+ ${s.primary} ×${coef(s.k1)}`);
  if (s.secondary !== "None") parts.push(`+ ${s.secondary} ×${coef(s.k2)}`);
  const tail = s.unlocked ? "" : " Закрыт по умолчанию — требует постройки.";
  push([id, s.name, "base", "slot", "Слоты", `Core/DefaultContent.cs:${s.line}`,
        withNote(id, parts.join(" ") + "." + tail)]);

  const perArch = archetypes.map((a) => ({
    name: a.name,
    out: roundHalfEven((s.base + (a.stats[s.primary] ?? 0) * s.k1 + (a.stats[s.secondary] ?? 0) * s.k2) * globalMult),
  })).sort((x, y) => y.out - x.out);
  const suited = archetypes.filter((a) => (a.stats[s.primary] ?? 0) >= (balance.AptitudeMatchThreshold ?? 5)).length;
  const top = first(perArch), bottom = last(perArch);
  fact(id, top ? `На стартовых архетипах даёт от ${bottom.out} до ${top.out} за цикл: лучший — ${top.name} (${top.out}).` : `Архетипов в контенте нет — выработку не на ком посчитать.`,
       `Порог «по профилю» ${balance.AptitudeMatchThreshold ?? 5} перешагивают ${suited} из ${archetypes.length} архетипов` +
       (suited ? ` — им ролевой опыт ${roundHalfEven((balance.RoleXpPerCycle ?? 20) * (balance.WellSuitedXpMultiplier ?? 1.5))} вместо ${balance.RoleXpPerCycle ?? 20}.` : "."));

  link(idSection(s.section), id, "позиция секции" + (s.unlocked ? "" : ", закрыта по умолчанию"), "code");
  for (const [role, stat, k] of [["Primary", s.primary, s.k1], ["Secondary", s.secondary, s.k2]]) {
    if (stat === "None") continue;
    const mism = isCombatStat(stat) && (statMax[stat] ?? 0) > 20;
    link(id, idStat(stat), `${role} ×${coef(k)}` + (mism ? ` — но ${stat} в шкале 0–100` : ""), mism ? "gap" : "code");
    if (mism) scaleMismatches.push({ slot: s, stat, k, role });
  }
  const outId = s.kind === "Resource" ? idRes(s.resource) : s.kind === "Passive" ? idOut(s.passive) : idOut("Healing");
  link(id, outId, `${s.kind}, base ${num(s.base)}`, "code");
}

// архетипы
const instanceArch = (instanceId) => instanceId.replace(/_\d+$/, "");
for (const a of archetypes) {
  const id = idArch(a.id);
  const st = Object.entries(a.stats).map(([k, v]) => `${k} ${v}`).join(", ");
  const gr = Object.entries(a.growth).map(([k, v]) => `${k} ×${num(v)}`).join(", ");
  push([id, a.name, "base", "arch", "Архетипы", `DefaultContent.${a.method}()`,
        withNote(id, `Старт: ${st}. Рост: ${gr}.`)]);

  const fits = slots.map((s) => ({
    name: s.name,
    out: roundHalfEven((s.base + (a.stats[s.primary] ?? 0) * s.k1 + (a.stats[s.secondary] ?? 0) * s.k2) * globalMult),
  })).sort((x, y) => y.out - x.out);
  const perLevel = balance.StatPointsPerLevel ?? 3;
  const wTotal = Object.values(a.growth).reduce((s, v) => s + v, 0);
  fact(id, `Стартовых очков ${Object.values(a.stats).reduce((s, v) => s + v, 0)} на ${Object.keys(a.stats).length} статах.`,
       wTotal ? `За уровень ${perLevel} очка по весам (сумма весов ${coef(wTotal)}), то есть ${Object.entries(a.growth).map(([k, v]) => `${k} ≈ ${(perLevel * v / wTotal).toFixed(1)}`).join(", ")} за уровень.` : null,
       fits.length ? `Лучший слот — «${first(fits).name}» (${first(fits).out}/цикл), худший — «${last(fits).name}» (${last(fits).out}).` : `Слотов в контенте нет — приткнуть некуда.`);
  for (const [k, v] of Object.entries(a.stats)) {
    if (!usedStats.has(k)) continue;
    const w = a.growth[k];
    link(id, idStat(k), `${k} ${v}` + (w ? `, рост ×${num(w)}` : ""), "code");
  }
  for (const [k, w] of Object.entries(a.growth)) {
    if (a.stats[k] !== undefined) continue;
    link(id, idStat(k), `рост ×${num(w)} с нуля`, "code");
  }
}

/* ──────────────────────── прогон первого дня ──────────────────────── */

const slotById = Object.fromEntries(slots.map((s) => [s.id, s]));
const archById = Object.fromEntries(archetypes.map((a) => [a.id, a]));

const runRows = [];
const produced = {};
let offProfile = [];

for (const as of demo.assignments) {
  const slot = slotById[as.slot];
  const arch = archById[instanceArch(as.instance)];
  if (!slot || !arch) { warn(`расстановка демки ссылается на неизвестные ${as.instance} / ${as.slot}`); continue; }
  const p = slot.primary !== "None" ? (arch.stats[slot.primary] ?? 0) : 0;
  const s2 = slot.secondary !== "None" ? (arch.stats[slot.secondary] ?? 0) : 0;
  const raw = slot.base + p * slot.k1 + s2 * slot.k2;
  const out = roundHalfEven(raw * globalMult);
  const wellSuited = slot.primary !== "None" && p >= (balance.AptitudeMatchThreshold ?? 5);
  const xp = roundHalfEven((balance.RoleXpPerCycle ?? 20) * (wellSuited ? balance.WellSuitedXpMultiplier ?? 1.5 : 1));
  const unit = slot.kind === "Resource" ? slot.resource : slot.kind === "Passive" ? slot.passive : "Healing";
  const formula = `${num(slot.base)}` +
    (slot.primary !== "None" ? ` + ${p}×${coef(slot.k1)}` : "") +
    (slot.secondary !== "None" ? ` + ${s2}×${coef(slot.k2)}` : "") + ` = ${raw.toFixed(1)}`;
  runRows.push([arch.name, slot.name, formula, `${out} ${unit}`, String(xp)]);
  if (slot.kind === "Resource") produced[slot.resource] = (produced[slot.resource] ?? 0) + out;
  if (!wellSuited) offProfile.push({ arch: arch.name, slot: slot.name, stat: slot.primary, xp });

  link(idArch(arch.id), idSlot(slot.id),
       wellSuited ? "стартовая расстановка демки" : `стартовая расстановка: ${slot.primary} = ${p}, не по профилю`,
       wellSuited ? "code" : "gap");
}

const takenSlots = new Set(demo.assignments.map((a) => a.slot));
const idleSlots = slots.filter((s) => !takenSlots.has(s.id) && s.unlocked);
const lockedSlots = slots.filter((s) => !s.unlocked);
for (const s of [...idleSlots, ...lockedSlots]) {
  const reason = s.unlocked ? "слот свободен: некому встать" : "UnlockedByDefault = false, открыть нечем";
  const unit = s.kind === "Resource" ? s.resource : s.kind === "Passive" ? s.passive : "Healing";
  runRows.push([s.unlocked ? "— никого" : "— закрыт", s.name, reason, `0 ${unit}`, "—"]);
}

// баланс еды
const companions = new Set(demo.assignments.map((a) => a.instance)).size || archetypes.length;
const upkeep = (balance.FoodUpkeepPerCompanion ?? 1) * companions;
const foodIn = produced.Food ?? 0;
const foodStart = demo.starting.Food ?? 0;

// день первого уровня
const xpToNext1 = roundHalfEven((balance.XpBase ?? 100) * Math.pow(1, balance.XpExponent ?? 1.5));
const xpValues = [...new Set(runRows.filter((r) => r[4] !== "—").map((r) => +r[4]))].sort((a, b) => b - a);
const levelDays = xpValues.map((v) => ({ xp: v, day: Math.ceil(xpToNext1 / v) }));

// ресурсы, которые за прогон остаются нулём
const zeroRes = resTypes
  .filter((r) => r.name !== "None" && !(produced[r.name] > 0) && !(demo.starting[r.name] > 0))
  .map((r) => r.name);

const runNote = [
  `Работают ${demo.assignments.length} слотов из ${slots.length}: ` +
    [idleSlots.length ? `${idleSlots.map((s) => `«${s.name}»`).join(", ")} — некому встать` : null,
     lockedSlots.length ? `${lockedSlots.map((s) => `«${s.name}»`).join(", ")} закрыт по умолчанию` : null]
      .filter(Boolean).join("; ") + ".",
  zeroRes.length ? `За все ${demo.cycles} дней прогона нулём остаются: ${zeroRes.join(", ")}.` : null,
  `Еда: производится ${foodIn}, прокорм списывает ${balance.FoodUpkeepPerCompanion ?? 1} × ${companions} = ${upkeep}` +
    (foodIn === upkeep
      ? `. Сходится в ноль — стартовые ${foodStart} не растут и не тают, седьмой напарник уже уводит базу в минус.`
      : foodIn > upkeep ? `, запас растёт на ${foodIn - upkeep} в день.` : `, запас тает на ${upkeep - foodIn} в день.`),
  offProfile.length
    ? `Не по профилю: ${offProfile.map((o) => `${o.arch} на «${o.slot}» (${o.stat} ниже порога ${balance.AptitudeMatchThreshold ?? 5})`).join(", ")} — и выработка голая, и ролевой опыт ${offProfile[0].xp} вместо ${first(xpValues, "—")}.`
    : null,
  slots.some((s) => s.kind === "Healing") && !/InjuryPoints\s*=/.test(coreCode.replace(/InjuryPoints\s*=\s*0/g, ""))
    ? `Очки лечения уходят в пустоту: ранения в игре никто не наносит.`
    : null,
  levelDays.length
    ? `Порог первого уровня — ${xpToNext1} XP: ` +
      levelDays.map((l) => `при ${l.xp} XP в день это ${l.day}-й день`).join(", ") + "."
    : `Порог первого уровня — ${xpToNext1} XP, но ролевой опыт сейчас никто не набирает.`,
].filter(Boolean).join(" ");

/* ──────────────────────────── автопроверки ──────────────────────────── */

const autoGaps = [];
/** cat — что это за разрыв: см. CATS в шаблоне. */
const addGap = (title, where, text, sev, cat, edge) => {
  autoGaps.push([title, where, text, sev, cat]);
  if (edge) link(edge[0], edge[1], edge[2], "gap");
};

// секции без слотов
for (const sec of sectionTypes) {
  if (sec.name === "None" || slotsBySection[sec.name]) continue;
  addGap(`Секция ${label(sec.name, sec.name)} без слотов`,
    `Core/BaseSectionType.cs:${sec.line} ↔ DefaultContent.AllSlots()`,
    `Секция объявлена в enum под значением ${sec.value}, но слотов у неё 0 — при том что остальные ${sectionTypes.length - 2} секции держат ${slots.length} слотов. Назначить туда некого, секция недостижима.`,
    "low", "Мёртвый код", [idSection(sec.name), "b.m.assign", "секция есть в enum, слотов у неё нет"]);
}

// Ресурсы без производителя и без трат.
// Тратой считается и прямой TrySpend(ResourceType.X, n), и запись в словаре цены
// вида { ResourceType.X, n } — словарную перегрузку TrySpend регексп по имени
// ресурса не увидит, и «экономика односторонняя» висела бы ложно.
const gameCode = CS_FILES.filter((p) => !p.includes("/Tests/")).map((p) => CS[p]).join("\n");
const spentDirect = [...gameCode.matchAll(/TrySpend\(ResourceType\.(\w+)/g)].map((m) => m[1]);
const spentInCosts = [...gameCode.matchAll(/\{\s*ResourceType\.(\w+)\s*,\s*\d+\s*\}/g)].map((m) => m[1]);
const spentRes = [...spentDirect, ...spentInCosts];
for (const r of resTypes) {
  if (r.name === "None" || producedRes.has(r.name) || spentRes.includes(r.name) || demo.starting[r.name]) continue;
  addGap(`Ресурс ${r.name} мёртвый`, `Core/Economy/ResourceType.cs:${r.line}`,
    `Значение enum ${r.value}, производителей 0 из ${slots.length} слотов, вызовов TrySpend 0, стартового запаса 0. Либо слот-источник, либо убрать из enum до времени.`,
    "low", "Мёртвый код", [idRes(r.name), "b.m.production", "ресурс объявлен, но его никто не производит и не тратит"]);
}

// односторонняя экономика
const unspent = [...producedRes].filter((r) => !spentRes.includes(r));
if (spentRes.length && unspent.length) {
  const spendLine = lineOf(CS[SRC.baseState] || "", (CS[SRC.baseState] || "").indexOf("TrySpend("));
  const perDay = unspent.map((r) => {
    const from = slots.filter((s) => s.kind === "Resource" && s.resource === r);
    return `${r} до ${maxOf(from.map((s) => maxOf(archetypes.map((a) => roundHalfEven(s.base + (a.stats[s.primary] ?? 0) * s.k1 + (a.stats[s.secondary] ?? 0) * s.k2)), s.base)))}/цикл`;
  });
  addGap("Экономика односторонняя", `Core/Base/BaseState.cs:${spendLine}`,
    `Тратятся только ${[...new Set(spentRes)].join(", ")} (${plural(spentDirect.length, "прямой вызов", "прямых вызова", "прямых вызовов")} TrySpend, ${plural(spentInCosts.length, "строка", "строки", "строк")} в словарях цен). Остальные ${plural(unspent.length, "ресурс", "ресурса", "ресурсов")} копятся без потолка: ${perDay.join(", ")}.`,
    "low", "Нет логики", [idRes(unspent[0]), "b.m.upkeep", `кроме ${[...new Set(spentRes)].join(", ")} не тратится ни один ресурс`]);
}

// поля баланса, которые никто не читает
const balanceText = CS[SRC.balance] || "";
const balanceFields = Object.keys(balance);
const assetFields = (CS[SRC.asset] || "").match(/public\s+(?:double|int)\s+\w+/g)?.length ?? 0;
// Ассет — это проводка конфига, а не потребитель: поле, которое только зеркалится
// в BalanceConfigAsset и больше нигде не читается, остаётся мёртвой крутилкой.
const PLUMBING = new Set([SRC.balance, SRC.asset]);
for (const field of balanceFields) {
  const readers = CS_FILES.filter((p) => !PLUMBING.has(p) && new RegExp(`\\b${field}\\b`).test(CS[p]));
  if (readers.length) continue;
  const line = lineOf(balanceText, balanceText.indexOf(`${field} =`));
  const mirrored = new RegExp(`\\b${field[0].toLowerCase()}${field.slice(1)}\\b`).test(CS[SRC.asset] || "");
  addGap(`${field} не используется`, `Core/Balance/BalanceConfig.cs:${line}`,
    `Значение ${coef(balance[field])}, но читателей 0 из ${CS_FILES.length - PLUMBING.size} файлов вне самого конфига` +
    (mirrored
      ? ` — в BalanceConfigAsset поле перенесено (${assetFields} из ${balanceFields.length} чисел), но крутить его бесполезно: ни одна формула его не спрашивает.`
      : `, и в BalanceConfigAsset оно даже не перенесено (там ${assetFields} из ${balanceFields.length} чисел конфига).`),
    "low", "Мёртвый код");
}

// ScriptableObject-конфиг, который никто не применяет
if (exists(SRC.asset)) {
  const assetText = CS[SRC.asset];
  const callers = CS_FILES.filter((p) => p !== SRC.asset && /ToConfig\s*\(/.test(CS[p]));
  const fieldLine = lineOf(CS[SRC.demo] || "", (CS[SRC.demo] || "").indexOf("BalanceConfigAsset "));
  if (!callers.length && /BalanceConfigAsset/.test(CS[SRC.demo] || "")) {
    addGap("balanceAsset ни на что не влияет", `Gameplay/BaseGameDemo.cs:${fieldLine} ↔ BalanceConfigAsset.cs:${lineOf(assetText, assetText.indexOf("ToConfig"))}`,
      `Ассет переносит ${assetFields} из ${balanceFields.length} чисел конфига, но вызовов ToConfig() в проекте 0 — база собирается на new BalanceConfig() с дефолтами. Крутить ${assetFields} слайдеров бесполезно, хотя README и BALANCE.md §7 описывают это как рабочий воркфлоу.`,
      "mid", "Мёртвый код", ["c.BaseGameDemo", "c.BalanceConfigAsset", "поле есть, но ToConfig() никем не вызывается"]);
  }
}

// статусы, которые никто не присваивает
const assigned = new Set([...allCode.matchAll(/Status\s*=\s*CompanionStatus\.(\w+)/g)].map((m) => m[1]));
const neverSet = statusTypes.filter((s) => !assigned.has(s.name)).map((s) => s.name);
if (neverSet.length) {
  const total = [...allCode.matchAll(/Status\s*=\s*CompanionStatus\.\w+/g)].length;
  addGap(`Статусы ${neverSet.join(", ")} не выставляются`,
    `Core/Characters/Companion.cs:${statusTypes.find((s) => s.name === neverSet[0]).line}`,
    `Присваиваний Status по всему проекту ${total}, и все они ставят одно из ${assigned.size} значений: ${[...assigned].join(", ")}. Остальные ${neverSet.length} из ${statusTypes.length} значений enum только сравниваются — значит ветки, завязанные на них (возврат в Idle после выздоровления), недостижимы.`,
    "mid", "Мёртвый код");
}

// ранения без источника
const injurySetters = CORE_FILES.filter((p) =>
  [...CS[p].matchAll(/InjuryPoints\s*=\s*([^;]+);/g)].some((m) => m[1].trim() !== "0"));
if (slots.some((s) => s.kind === "Healing") && !injurySetters.length) {
  const bestHeal = maxOf(slots.filter((s) => s.kind === "Healing")
    .map((s) => maxOf(archetypes.map((a) => roundHalfEven(s.base + (a.stats[s.primary] ?? 0) * s.k1 + (a.stats[s.secondary] ?? 0) * s.k2)), s.base)));
  addGap("Ранения некому наносить", "Core/Base/BaseState.cs · Companion.InjuryPoints",
    `InjuryPoints поднимается в ${injurySetters.length} файлах игрового кода — то есть нигде: значение выставляют только тесты через internal. При этом лазарет считает до ${bestHeal} очков лечения за цикл плюс ${coef(balance.BaseHealingPerCycle)} естественной регенерации. Всё в пустоту, пока нет боя и вылазок.`,
    "mid", "Нет логики", ["b.m.heal", "b.m.assign", "InjuryPoints выставляет только тест — источника ранений в игре нет"]);
}

// боевой стат в формуле выработки: шкалы 0–100 и 0–7 несопоставимы
for (const m of scaleMismatches) {
  const best = archetypes
    .map((a) => ({ name: a.name, out: roundHalfEven(m.slot.base + (a.stats[m.slot.primary] ?? 0) * m.slot.k1 + (a.stats[m.slot.secondary] ?? 0) * m.slot.k2) }))
    .sort((x, y) => y.out - x.out);
  addGap(`Боевой ${m.stat} в формуле «${m.slot.name}» смешивает шкалы`,
    `Core/DefaultContent.cs:${m.slot.line} ↔ docs/BALANCE.md §2`,
    `Слот берёт ${m.stat} как ${m.role} с коэффициентом ×${coef(m.k)}, но ${m.stat} — боевой стат в шкале 0–100 (максимум у архетипов ${statMax[m.stat]}), тогда как склонности живут в 0–7. ` +
    `Итог: ${best.slice(0, 3).map((b) => `${b.name} даёт ${b.out}`).join(", ")} — профильный архетип оказывается не первым. Заявленное в BALANCE.md §3 «ставить людей по профилю выгодно вдвойне» тут ломается.`,
    "mid", "Баланс");
}

// закрытые слоты, которые нечем открыть
const unlockSetters = CORE_FILES.filter((p) => /\.Unlocked\s*=/.test(CS[p]));
for (const s of lockedSlots) {
  if (unlockSetters.length) break;
  addGap(`«${s.name}» открыть нечем`, `Core/Base/AssignmentSlot.cs · DefaultContent.cs:${s.line}`,
    `Единственный из ${slots.length} слотов с UnlockedByDefault = false, а присваиваний Unlocked в игровом коде 0 (одно есть в тестах). Механики стройки, которая его переключит, ещё нет — слот закрыт навсегда, и заложенные в него ${s.base} базы плюс ${s.primary} ×${coef(s.k1)} не работают.`,
    "low", "Нет логики", ["b.m.unlock", idSlot(s.id), "переключать Unlocked некому"]);
}

// События без потребителя в игровом коде.
// Границы слова обязательны: без них подписка на BandChanged закрывала карточку
// про Changed, потому что одно имя — подстрока другого.
for (const p of CORE_FILES) {
  for (const m of CS[p].matchAll(/public event\s+[\w<>,\s]+\s+(\w+)\s*;/g)) {
    const sub = new RegExp(`\\b${m[1]}\\s*\\+=`);
    const inGame = CS_FILES.filter((f) => !f.includes("/Tests/") && sub.test(CS[f]));
    if (inGame.length) continue;
    // Событие без потребителя, но с тестом — осознанная точка расширения:
    // контракт зафиксирован и не отвалится молча. Без теста и без подписок —
    // просто мёртвая проводка.
    const inTests = CS_FILES.filter((f) => f.includes("/Tests/") && sub.test(CS[f]));
    if (inTests.length) continue;
    addGap(`${path.basename(p, ".cs")}.${m[1]} никто не слушает`,
      `${p.replace("Assets/_Project/Scripts/", "")}:${lineOf(CS[p], m.index)}`,
      `Подписок 0 во всех ${CS_FILES.length} файлах, теста на контракт тоже нет, ` +
      `вызовов Invoke ${(CS[p].match(new RegExp(`\\b${m[1]}\\?\\.Invoke`, "g")) || []).length}. Событие стреляет в пустоту.`,
      "low", "Мёртвый код");
  }
}

// версия Unity
if (exists("ProjectSettings/ProjectVersion.txt") && exists("docs/GDD.md")) {
  const editor = read("ProjectSettings/ProjectVersion.txt").match(/m_EditorVersion:\s*(\S+)/)?.[1];
  const gddVer = read("docs/GDD.md").match(/Unity\s+(\d+\.\d+)/)?.[1];
  const major = editor?.match(/^6000\.(\d+)/);
  const editorLabel = major ? `6.${major[1]}` : editor;
  if (editor && gddVer && editorLabel !== gddVer) {
    addGap("Версия Unity разошлась с GDD", "docs/GDD.md Э18 ↔ ProjectSettings/ProjectVersion.txt",
      `GDD называет Unity ${gddVer}, ProjectVersion.txt стоит на ${editor}, то есть Unity ${editorLabel}. Мелочь, но GDD — источник истины, стоит синхронизировать.`,
      "low", "Числа в доках");
  }
}

// таблица опыта в BALANCE.md против формулы
if (exists("docs/BALANCE.md")) {
  const bal = read("docs/BALANCE.md");
  const xpToNext = (lvl) => roundHalfEven((balance.XpBase ?? 100) * Math.pow(lvl, balance.XpExponent ?? 1.5));
  const total = (lvl) => { let t = 0; for (let l = 1; l < lvl; l++) t += xpToNext(l); return t; };
  const bad = [];
  for (const m of bal.matchAll(/^\|\s*(\d+)\s*\|\s*(\d+)\s*\|\s*(\d+)\s*\|/gm)) {
    const [lvl, next, sum] = [+m[1], +m[2], +m[3]];
    if (xpToNext(lvl) !== next) bad.push(`уровень ${lvl}: XpToNext ${next} вместо ${xpToNext(lvl)}`);
    if (total(lvl) !== sum) bad.push(`уровень ${lvl}: суммарно ${sum} вместо ${total(lvl)}`);
  }
  if (bad.length) {
    addGap("Таблица опыта в BALANCE.md не сходится с формулой",
      `docs/BALANCE.md §1 ↔ Core/Balance/ProgressionMath.cs`,
      `По формуле round(${num(balance.XpBase ?? 100)} × level^${num(balance.XpExponent ?? 1.5)}) расходятся ${plural(bad.length, "строка", "строки", "строк")}: ${bad.join("; ")}. Таблица противоречит и коду, и самой себе.`,
      "mid", "Числа в доках");
  }
}

/* ──────────────────── сверка авторского слоя с кодом ──────────────────── */

const nodeById = Object.fromEntries(nodes.map((n) => [n[0], n]));
const typeFile = {};
for (const n of G.nodes) {
  if (!n[0].startsWith("c.") || !n[5]) continue;
  const p = "Assets/_Project/Scripts/" + n[5].split(":")[0].replace(/^Core\//, "Core/").replace(/^Gameplay\//, "Gameplay/");
  const guess = CS_FILES.find((f) => f.endsWith("/" + n[5].split(":")[0]) || f.endsWith(n[5].split(":")[0]));
  if (guess) typeFile[n[0]] = guess;
  else if (!n[5].startsWith("Tests/") && !/asmdef/.test(n[5])) warn(`узел ${n[0]}: не нашёл файл «${n[5]}»`);
}
/* Числовые справки для узлов кода — размер типа, кто им пользуется. */
function typeBlock(text, name) {
  const m = text.match(new RegExp(`(?:class|struct|enum|interface)\\s+${name}\\b`));
  if (!m) return null;
  let i = text.indexOf("{", m.index);
  if (i < 0) return null;
  let d = 0;
  for (let j = i; j < text.length; j++) {
    if (text[j] === "{") d++;
    else if (text[j] === "}" && --d === 0) return text.slice(i, j + 1);
  }
  return null;
}
for (const n of G.nodes) {
  if (n[2] !== "code") continue;
  const [id, name, , kind] = n;
  if (kind === "asm") {
    const own = CS_FILES.filter((p) => p.includes(name === "Game.Tests.EditMode" ? "/Tests/" : name === "Game.Gameplay" ? "/Gameplay/" : "/Core/"));
    const types = own.reduce((s, p) => s + (CS[p].match(/\b(?:class|struct|enum)\s+\w+/g) || []).length, 0);
    fact(id, `${plural(own.length, "файл", "файла", "файлов")}, ${plural(types, "тип", "типа", "типов")}, ${plural(own.reduce((s, p) => s + CS[p].split("\n").length, 0), "строка", "строки", "строк")}.`);
    continue;
  }
  const file = typeFile[id];
  if (!file) continue;
  const block = typeBlock(CS[file], name);
  const users = CS_FILES.filter((p) => p !== file && new RegExp(`\\b${name}\\b`).test(CS[p]));
  const lines = block ? block.split("\n").length : CS[file].split("\n").length;
  if (kind === "enum") {
    const members = (block?.match(/^\s*\w+\s*=\s*\d+/gm) || []).length;
    const working = (block?.match(/^\s*(\w+)\s*=\s*\d+/gm) || []).filter((s) => !/\bNone\b/.test(s)).length;
    fact(id, `${plural(members, "член", "члена", "членов")} enum` + (members !== working ? `, рабочих значений ${working}.` : "."),
         `Используют ${plural(users.length, "файл", "файла", "файлов")}.`);
  } else if (kind === "test") {
    const tests = (CS[file].match(/\[Test\]/g) || []).length;
    fact(id, `${plural(tests, "тест", "теста", "тестов")}, ${CS[file].split("\n").length} строк.`);
  } else {
    const pub = (block?.match(/\bpublic\s+(?!class|struct|enum)/g) || []).length;
    fact(id, `${lines} строк, ${plural(pub, "публичный член", "публичных члена", "публичных членов")}.`,
         `Используют ${plural(users.length, "файл", "файла", "файлов")}.`);
  }
}

/* Числовые справки для узлов механики базы — прямо из BalanceConfig. */
fact("b.m.production", `GlobalProductionMultiplier ${coef(globalMult)}, штраф раненому ×${coef(balance.InjuredProductionMultiplier ?? 1)}.`,
     `${plural(slots.length, "слот", "слота", "слотов")} со своими коэффициентами, база от ${minOf(slots.map((s) => s.base))} до ${maxOf(slots.map((s) => s.base))}.`);
fact("b.m.rolexp", `RoleXpPerCycle ${balance.RoleXpPerCycle}, порог соответствия ${balance.AptitudeMatchThreshold}, множитель ×${coef(balance.WellSuitedXpMultiplier)} — то есть ${roundHalfEven(balance.RoleXpPerCycle * balance.WellSuitedXpMultiplier)} против ${balance.RoleXpPerCycle}.`);
fact("b.m.levelup", `XpBase ${num(balance.XpBase)}, XpExponent ${coef(balance.XpExponent)}, MaxLevel ${balance.MaxLevel}, очков за уровень ${balance.StatPointsPerLevel}.`,
     `Пороги: ${[1, 2, 3, 5, 10].map((l) => `ур.${l} → ${roundHalfEven(balance.XpBase * Math.pow(l, balance.XpExponent))}`).join(", ")}.`);
fact("b.m.heal", `BaseHealingPerCycle ${coef(balance.BaseHealingPerCycle)} всем раненым за цикл.`,
     `Плюс до ${maxOf(slots.filter((s) => s.kind === "Healing").map((s) => maxOf(archetypes.map((a) => roundHalfEven(s.base + (a.stats[s.primary] ?? 0) * s.k1 + (a.stats[s.secondary] ?? 0) * s.k2)), s.base)))} очков из лазарета на лучшем медике.`);
fact("b.m.upkeep", `FoodUpkeepPerCompanion ${balance.FoodUpkeepPerCompanion}; на ${archetypes.length} напарниках это ${balance.FoodUpkeepPerCompanion * archetypes.length} еды за цикл.`);
fact("b.m.cycle", `За вызов обходит ${plural(slots.length, "слот", "слота", "слотов")} и ${plural(archetypes.length, "напарника", "напарников", "напарников")}, шагов ${4}.`);
fact("b.m.assign", `${plural(parseEnum("Assets/_Project/Scripts/Core/Base/AssignmentResult.cs", "AssignmentResult").length, "код возврата", "кода возврата", "кодов возврата")}, из них ошибок ${parseEnum("Assets/_Project/Scripts/Core/Base/AssignmentResult.cs", "AssignmentResult").length - 1}.`);
fact("b.m.unlock", `Закрытых по умолчанию слотов ${slots.filter((s) => !s.unlocked).length} из ${slots.length}.`);
fact("b.m.report", `Полей отчёта ${(typeBlock(CS["Assets/_Project/Scripts/Core/Base/CycleReport.cs"] || "", "CycleReport")?.match(/\bpublic\s+(?!class)/g) || []).length}.`);

for (const [s, t, lbl, tag] of G.edges) {
  if (tag !== "code" || !s.startsWith("c.") || !t.startsWith("c.")) continue;
  const file = typeFile[s], target = nodeById[t]?.[1];
  if (!file || !target || /asmdef|Сборки/.test(nodeById[s]?.[4] || "")) continue;
  if (!new RegExp(`\\b${target}\\b`).test(CS[file])) warn(`связь ${s} → ${t}: «${target}» не встречается в ${file}`);
}
for (const [s, t] of edges) {
  if (!nodeById[s]) warn(`связь ссылается на несуществующий узел ${s}`);
  if (!nodeById[t]) warn(`связь ссылается на несуществующий узел ${t}`);
}

/* ──────────────────────────── сборка файла ──────────────────────────── */

const sha1 = (s) => crypto.createHash("sha1").update(s).digest("hex").slice(0, 8);
const designSources = ["docs/GDD.md", "docs/DESIGN.md", "docs/BALANCE.md"].filter(exists);
const fingerprints = Object.fromEntries(designSources.map((p) => [p, sha1(read(p))]));
const stale = designSources.filter((p) => G.designCheckedAgainst?.[p] && G.designCheckedAgainst[p] !== fingerprints[p]);

let gitSha = null;
try { gitSha = execFileSync("git", ["-C", ROOT, "rev-parse", "--short", "HEAD"], { encoding: "utf8" }).trim(); } catch {}

/* Подстановки {{...}} для авторских текстов — чтобы числа в них не протухали. */
const slotOut = (id) => {
  const s = slotById[id];
  if (!s) return [0, 0];
  const v = archetypes.map((a) => roundHalfEven((s.base + (a.stats[s.primary] ?? 0) * s.k1 + (a.stats[s.secondary] ?? 0) * s.k2) * globalMult));
  return [minOf(v, s.base), maxOf(v, s.base)];
};
const [benchMin, benchMax] = slotOut("workshop_bench");
const [councilMin, councilMax] = slotOut("council_seat");
const VARS = {
  slotCount: slots.length,
  sectionCount: sectionTypes.filter((s) => s.name !== "None").length,
  resourceCount: resTypes.filter((r) => r.name !== "None").length,
  archetypeCount: archetypes.length,
  statCount: statTypes.filter((s) => s.name !== "None").length,
  statusCount: statusTypes.length,
  producedCount: producedRes.size,
  spentCount: new Set(spentRes).size,
  balanceFieldCount: balanceFields.length,
  assetFieldCount: assetFields,
  pointsPerLevel: balance.StatPointsPerLevel,
  reportFields: (typeBlock(CS["Assets/_Project/Scripts/Core/Base/CycleReport.cs"] || "", "CycleReport")?.match(/\bpublic\s+(?!class)/g) || []).length,
  benchMin, benchMax, councilMin, councilMax,
};
const subst = (text) => String(text).replace(/\{\{(\w+)\}\}/g, (m, k) => {
  if (VARS[k] === undefined) { warn(`в авторском тексте нет подстановки {{${k}}}`); return m; }
  return String(VARS[k]);
});

/* Числовые справки дизайн-слоя: числа из GDD там, где они есть, и честная пометка там, где их нет. */
let gddWithout = 0;
for (const n of G.nodes) {
  if (n[2] !== "gdd") continue;
  const g = (G.gddNumbers || {})[n[0]];
  if (g?.n) fact(n[0], g.n, g.src ? `Источник: ${g.src}.` : null);
  else if (!/\d/.test(n[6] || "")) { fact(n[0], "Числовых значений GDD для этой системы не задаёт."); gddWithout++; }
}

/* Каждому узлу — его вес в графе. Гарантирует числа даже там, где их нет в источниках. */
const degIn = {}, degOut = {};
for (const [s, t] of edges) { degOut[s] = (degOut[s] ?? 0) + 1; degIn[t] = (degIn[t] ?? 0) + 1; }
for (const n of nodes) {
  const i = degIn[n[0]] ?? 0, o = degOut[n[0]] ?? 0;
  fact(n[0], `Связей ${i + o}: ${plural(o, "исходящая", "исходящие", "исходящих")}, ${plural(i, "входящая", "входящие", "входящих")}.`);
}

const gaps = [...G.gaps.map((g) => [g[0], g[1], subst(g[2]), g[3], g[4]]), ...autoGaps]
  .sort((a, b) => ({ high: 0, mid: 1, low: 2 })[a[3]] - ({ high: 0, mid: 1, low: 2 })[b[3]]);
for (const g of gaps) if (!g[4]) warn(`у разрыва «${g[0]}» не проставлена категория`);

const DATA = {
  meta: {
    builtAt: new Date().toISOString().slice(0, 16).replace("T", " "),
    gitSha,
    autoGaps: autoGaps.length,
    gddWithout,
    gddTotal: G.nodes.filter((n) => n[2] === "gdd").length,
    stale,
    fingerprints,
  },
  nodes: nodes.map((n) => (facts[n[0]] ? [...n.slice(0, 7), facts[n[0]]] : n)),
  edges, gaps, run: runRows, runNote,
};

const noNumbers = DATA.nodes.filter((n) => !/\d/.test((n[6] || "") + (n[7] || "")));
if (noNumbers.length) console.log(`  · без числовых значений осталось ${noNumbers.length} узлов: ${noNumbers.slice(0, 6).map((n) => n[1]).join(", ")}${noNumbers.length > 6 ? "…" : ""}`);
const gapsNoNumbers = gaps.filter((g) => !/\d/.test(g[2]));
if (gapsNoNumbers.length) console.log(`  · без числовых значений осталось ${gapsNoNumbers.length} разрывов: ${gapsNoNumbers.map((g) => g[0]).join("; ")}`);

const template = read("tools/map/template.html");
if (!template.includes("/*__DATA__*/")) { console.error("в шаблоне нет метки /*__DATA__*/"); process.exit(2); }
const html = template.replace("/*__DATA__*/null", JSON.stringify(DATA));

const outPath = rel("docs/interaction-map.html");
const prev = fs.existsSync(outPath) ? fs.readFileSync(outPath, "utf8") : "";
const strip = (s) => s.replace(/"builtAt":"[^"]*"/, "");
const changed = strip(prev) !== strip(html);

for (const w of warnings) console.error("  ! " + w);
console.log(`карта: ${nodes.length} узлов, ${edges.length} связей, ${gaps.length} разрывов (${autoGaps.length} автоматом), ${runRows.length} строк прогона`);
if (stale.length) console.log(`  ~ дизайн-слой сверялся с другой версией: ${stale.join(", ")} — GDD-слой стоит перечитать`);

if (CHECK_ONLY) {
  console.log(changed ? "docs/interaction-map.html устарел" : "docs/interaction-map.html актуален");
  process.exit(changed ? 1 : 0);
}
// Копия без обёртки документа — для публикации артефактом (её каркас добавляет платформа).
const artifactIdx = process.argv.indexOf("--artifact");
if (artifactIdx > -1 && process.argv[artifactIdx + 1]) {
  const bare = html
    .replace(/^[\s\S]*?<head>\s*/i, "")
    .replace(/<\/head>\s*<body>/i, "")
    .replace(/<\/body>\s*<\/html>\s*$/i, "");
  fs.writeFileSync(process.argv[artifactIdx + 1], bare, "utf8");
  console.log("записана копия для артефакта:", process.argv[artifactIdx + 1]);
}

if (!changed) { console.log("без изменений"); process.exit(0); }
fs.writeFileSync(outPath, html, "utf8");
console.log("записано docs/interaction-map.html");
