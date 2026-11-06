#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Ручний аудит вигляду «Рингу» (Статут вигляду; запускає арт-директор на прохання, за розкладом НЕ запускається).

Показує: (1) літерали кольору/шрифту/дрібного кеглю у Ring-коді проти базової лінії храповика; (2) коміти гілки, що чіпали
видимі шляхи, і чи є в них рядок «Ворота: У0/У1/У2». Підрахунок тут — орієнтир; авторитет — RingDesignGuardTests (ті самі
регулярні вирази, але без розбору аргументів виклику: smallFont рахується лише для Lbl/fontSize-літералів).

  python tools/ring_design/audit.py [--since "3 days ago"] [--write]     # --write → docs/ring/design/AUDIT_<дата>.md
"""
import datetime
import io
import json
import os
import re
import subprocess
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
BASELINE = os.path.join(ROOT, "docs", "ring", "design", "guard_baseline.json")
GUARDED = [os.path.join("Assets", "_Project", "Scripts", "Gameplay", "Ring"), os.path.join("Assets", "_Project", "Scripts", "Editor")]
VISIBLE_PATHS = ["Assets/_Project/Scripts/Gameplay/Ring/UI", "Assets/_Project/Scripts/Gameplay/Ring/Stage/Unity",
                 "Assets/Art/Ring", "tools/blender", "docs/ring/design"]
ALLOW = {"RingTheme.cs", "BrandTokens.cs"}
PATTERNS = {
    "colorCtor": re.compile(r"\bnew\s+Color(32)?\s*\("),
    "hexLiteral": re.compile(r"#[0-9A-Fa-f]{6}(?:[0-9A-Fa-f]{2})?\b"),
    "namedColor": re.compile(r"\bColor\.(red|green|blue|white|black|yellow|cyan|magenta|gray|grey)\b"),
    "fontLoad": re.compile(r"FontDefinition\.FromFont|unityFontDefinition|Resources\.Load<Font>|CreateDynamicFontFromOSFont|\bLoadFont\s*\("),
    "smallFont": re.compile(r"\bLbl\s*\([^;]*?,\s*(\d+(?:\.\d+)?)f?\b|\bfontSize\s*=\s*(\d+(?:\.\d+)?)f?\s*;"),
}
MIN_INFO = 15


def strip_comments(s):
    s = re.sub(r"/\*.*?\*/", "", s, flags=re.S)
    return re.sub(r"(?m)^\s*//.*$|(?<=[;{}\s])//(?![^\"\n]*\"\s*[,;)]).*$", "", s)


def measure():
    out = {k: {} for k in PATTERNS}
    for base in GUARDED:
        top = os.path.join(ROOT, base)
        for dirpath, _, names in os.walk(top):
            for n in names:
                if not n.endswith(".cs") or n in ALLOW:
                    continue
                if base.endswith("Editor") and not n.startswith("Ring"):
                    continue
                path = os.path.join(dirpath, n)
                rel = os.path.relpath(path, ROOT).replace("\\", "/")
                code = strip_comments(io.open(path, encoding="utf-8", errors="replace").read())
                for k, rx in PATTERNS.items():
                    if k == "smallFont":
                        cnt = 0
                        for m in rx.finditer(code):
                            v = float(m.group(1) or m.group(2))
                            cnt += 1 if v < MIN_INFO else 0
                    else:
                        cnt = len(rx.findall(code))
                    if cnt:
                        out[k][rel] = cnt
    return out


def git(*args):
    return subprocess.run(["git", "-C", ROOT] + list(args), capture_output=True, text=True, encoding="utf-8").stdout


def main(argv):
    since = argv[argv.index("--since") + 1] if "--since" in argv else "7 days ago"
    now, base = measure(), json.load(io.open(BASELINE, encoding="utf-8"))
    lines = ["# Аудит вигляду «Рингу» — %s" % datetime.date.today().isoformat(), "",
             "Орієнтир (авторитет — `RingDesignGuardTests`); база — `guard_baseline.json`.", "",
             "## Літерали проти базової лінії", "", "| Категорія | Зараз | База | Різниця |", "|---|---|---|---|"]
    worse = []
    for k in PATTERNS:
        total_now, total_base = sum(now[k].values()), sum(base.get(k, {}).values())
        lines.append("| %s | %d | %d | %+d |" % (k, total_now, total_base, total_now - total_base))
        for f, n in now[k].items():
            if n > base.get(k, {}).get(f, 0):
                worse.append("%s: %s %d > %d" % (k, f, n, base.get(k, {}).get(f, 0)))
    lines += ["", "### Нові відхилення (мають бути порожні)", ""] + (["- " + w for w in worse] or ["немає"])
    log = git("log", "--since=" + since, "--format=%h%x09%an%x09%s%x09%b%x1e", "--", *VISIBLE_PATHS)
    lines += ["", "## Коміти з видимими змінами (з %s)" % since, "", "| Коміт | Автор | Тема | «Ворота:» |", "|---|---|---|---|"]
    for rec in [r for r in log.split("\x1e") if r.strip()]:
        h, a, s, *b = (rec.strip().split("\t", 3) + [""])[:4]
        lines.append("| %s | %s | %s | %s |" % (h, a, s[:80].replace("|", "/"), "так" if "Ворота:" in rec else "**немає**"))
    text = "\n".join(lines) + "\n"
    if "--write" in argv:
        path = os.path.join(ROOT, "docs", "ring", "design", "AUDIT_%s.md" % datetime.date.today().isoformat())
        io.open(path, "w", encoding="utf-8", newline="\n").write(text)
        print("записано", path)
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except AttributeError:
        pass
    print(text)


if __name__ == "__main__":
    main(sys.argv[1:])
