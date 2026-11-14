"""Попередній перегляд кадрів «Рингу» моделлю з зором (ПМ, 14.11.2026).

Шукає вади анімації на знімках матчу (проникнення, левітація, контакт не там, вивернуті суглоби, обличчя, рефері в
борці), щоб людина дивилася першими підозрілі кадри. НЕ замінює перегляд очима: кожну ваду перед звітом власнику
перевіряє людина на самому кадрі (правило «перевіряти очима»).

Калібрування 14.11.2026 на 24 кадрах біта слоту 34 (вади відомі з огляду очима):
  claude-opus-5-5  — $0,68; знайшла всі відомі вади (дошка над суперником 0,7–0,85, голова рефері в голові нападника,
                     рука рахунку на голові/спині) і одну, пропущену людиною (рефері на тілі притиснутого, кадр 100);
  claude-haiku-5-5 — $0,02; позначає майже кожен кадр, «дошку» пропустила — лише як дешеве сито, не як висновок.
Тому модель за замовчуванням — Opus 5.5.

Вхід — профіль `ant auth login` на цій машині (ключ не потрібен; ANTHROPIC_API_KEY не ставити).
Кожен прогін дописує рядок у журнал витрат %USERPROFILE%\\AppData\\LocalLow\\RingShots\\api_spend.csv —
бюджет API власника ($100) видно сумою цього файлу.

Запуск (лише з дозволу ПМ):
  python -I tools/ring_qa/frame_prescreen.py --label "<сесія: що>" --out <звіт.json> <кадр.png>...
  python -I tools/ring_qa/frame_prescreen.py --model claude-haiku-5-5 ...
"""
import argparse
import base64
import concurrent.futures as cf
import csv
import datetime
import json
import os
import re
import sys

import anthropic

PRICES = {  # $ за 1 млн токенів (вхід, вихід), прайс 2026-11
    "claude-opus-5-5": (4.0, 20.0),
    "claude-sonnet-5-5": (2.0, 10.0),
    "claude-haiku-5-5": (0.10, 0.50),
}

PROMPT = """You are a strict animation QA reviewer for a 3D wrestling video game. This is one still frame from a match
(stylized low-poly characters: two wrestlers and a referee in a striped shirt, in a ring; HUD panels and crowd around).
Ignore HUD text, UI panels, crowd, lighting and art style. Inspect ONLY the bodies of the wrestlers and the referee for
animation/physics defects visible in this still:
- penetration: a limb, head or torso visibly passing INSIDE another body (wrestler-wrestler, referee-wrestler) or the mat;
- floating: a body or body part hanging in the air with no plausible support (e.g. a wrestler lying flat in mid-air above
  the other during a pin) - normal jumps/throws mid-flight are fine only if the pose clearly reads as a throw;
- wrong_contact: contact on an implausible spot (e.g. referee's counting hand slapping a wrestler's head/body instead of
  the mat; a hand gripping empty air next to the target);
- limb_twist: broken-looking joints, wrists/elbows/knees bent the wrong way, stretched limbs;
- face: face artifacts (white/blank eyes, jaw hanging/skeletal look);
- staging: referee standing inside, on top of, or tight against a wrestler; characters overlapping the ropes/posts.
Return ONLY JSON: {"defects":[{"kind":"penetration|floating|wrong_contact|limb_twist|face|staging","who":"...",
"where":"short location in the image","confidence":0.0-1.0}], "clean": true|false}. If nothing is wrong, return
{"defects":[],"clean":true}. Be concrete; do not invent defects you cannot see."""

LEDGER = os.path.join(os.path.expanduser("~"), "AppData", "LocalLow", "RingShots", "api_spend.csv")


def media_type(path):
    ext = os.path.splitext(path)[1].lower()
    return {".png": "image/png", ".jpg": "image/jpeg", ".jpeg": "image/jpeg", ".webp": "image/webp"}[ext]


def review(client, model, path):
    with open(path, "rb") as f:
        data = base64.standard_b64encode(f.read()).decode("ascii")
    kw = dict(model=model, max_tokens=3000, messages=[{"role": "user", "content": [
        {"type": "image", "source": {"type": "base64", "media_type": media_type(path), "data": data}},
        {"type": "text", "text": PROMPT}]}])
    if "opus" in model or "sonnet" in model:
        kw["thinking"] = {"type": "adaptive"}
    msg = client.messages.create(**kw)
    text = "".join(b.text for b in msg.content if getattr(b, "type", "") == "text")
    m = re.search(r"\{.*\}", text, re.S)
    try:
        res = json.loads(m.group(0)) if m else {"parse_error": text[:300]}
    except ValueError:
        res = {"parse_error": text[:300]}
    return {"frame": os.path.basename(path), "path": path, "result": res,
            "in": msg.usage.input_tokens, "out": msg.usage.output_tokens}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--model", default="claude-opus-5-5", choices=sorted(PRICES))
    ap.add_argument("--label", required=True, help="хто і навіщо ганяє — іде в журнал витрат")
    ap.add_argument("--out", required=True)
    ap.add_argument("--min-confidence", type=float, default=0.5, help="поріг для короткого списку в консолі")
    ap.add_argument("frames", nargs="+")
    a = ap.parse_args()

    client = anthropic.Anthropic()
    with cf.ThreadPoolExecutor(max_workers=6) as ex:
        rows = list(ex.map(lambda p: review(client, a.model, p), a.frames))
    rows.sort(key=lambda r: r["frame"])
    tin = sum(r["in"] for r in rows)
    tout = sum(r["out"] for r in rows)
    pin, pout = PRICES[a.model]
    usd = tin / 1e6 * pin + tout / 1e6 * pout

    with open(a.out, "w", encoding="utf-8") as f:
        json.dump({"model": a.model, "label": a.label, "tokens_in": tin, "tokens_out": tout, "usd": round(usd, 4),
                   "rows": rows}, f, ensure_ascii=False, indent=1)
    os.makedirs(os.path.dirname(LEDGER), exist_ok=True)
    new = not os.path.exists(LEDGER)
    with open(LEDGER, "a", encoding="utf-8", newline="") as f:
        w = csv.writer(f)
        if new:
            w.writerow(["time", "label", "model", "frames", "tokens_in", "tokens_out", "usd"])
        w.writerow([datetime.datetime.now().isoformat(timespec="seconds"), a.label, a.model, len(rows), tin, tout,
                    f"{usd:.4f}"])

    sys.stdout.reconfigure(encoding="utf-8")
    print(f"{a.model}: {len(rows)} кадрів, вхід {tin}, вихід {tout}, ~${usd:.4f}; звіт — {a.out}")
    for r in rows:
        hits = [d for d in r["result"].get("defects", []) if d.get("confidence", 0) >= a.min_confidence]
        if hits or "parse_error" in r["result"]:
            print(r["frame"], "|", "; ".join(f'{d["kind"]} {d.get("confidence")}: {d.get("who", "")} — {d.get("where", "")}'
                                            for d in hits) or r["result"].get("parse_error"))


if __name__ == "__main__":
    main()
