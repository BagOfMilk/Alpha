"""Звуки бою за рингом, родина «стіл, промах і відлік» (власна генерація, чистий numpy).

Запуск (з теки tools):  python -m ring_audio.ring_outside_props --out <тека> [--only слот,слот]
Слоти: weapon_table (стіл ламається під тілом на бетоні з матом), whiff (промах, контакту немає),
count_out (один «удар» відліку рефері, без голосу). По 3 варіанти, різні ідеєю.
Фізика: деревина (ДСП/фанера) ламається серією коротких широкосмугових тріщин + сильно демпфований
резонанс панелі; підлога - бетон під матом: щільно й глухо, без порожнистого буму помосту; сталеві ніжки -
нерегулярні глухі удари труб (центральна частота кожного удару своя, без кілець). Усе детерміноване.
"""
import argparse
import os
import zlib

import numpy as np

from .loudness import clip_loudness, true_peak_db
from .synth import SR, filt, sweep_filter
from .ring_impacts import (C, N, pk, bn, env, slap, meat, knock, body, sub, rattle, rub, creak,
                           write_wav, spectrogram, sheet)

TARGETS = {"weapon_table": -15.0, "whiff": -22.0, "count_out": -19.0}


# ---------------------------------------------------------------- примітиви
def crack(c, t, g=1.0, fres=700.0, q=1.6, tau_res=9.0, dur=0.07, hi=9000):
    """одна тріщина деревини: 1-3 мс широкосмуговий імпульс + сильно демпфований резонанс панелі"""
    n = int(dur * SR)
    tt = np.arange(n) / SR
    imp = pk(bn(n, c.sd(), 500, hi, 1)) * np.exp(-tt / 0.0014)
    res = pk(filt(N(n, c.sd()), "bp", fres, q, 1)) * np.exp(-tt / (tau_res / 1000))
    x = pk(imp + 0.6 * res)
    x *= 1 - np.exp(-tt / 0.0002)
    c.add(x, t, g)


def crack_burst(c, t0, span, count, g=1.0, f_lo=300, f_hi=1500, decay=0.5):
    """серія нерегулярних тріщин за span секунд, тихіше до кінця"""
    r = np.random.default_rng(c.sd())
    ts = np.sort(r.uniform(0, span, count))
    ts[0] = 0.0
    for k, ti in enumerate(ts):
        gi = g * r.uniform(0.45, 1.0) * np.exp(-ti / max(span, 1e-3) * decay)
        if k == 0:
            gi = g
        crack(c, t0 + ti, gi, fres=float(np.exp(r.uniform(np.log(f_lo), np.log(f_hi)))),
              tau_res=r.uniform(5, 16), dur=r.uniform(0.04, 0.09))


def debris(c, t0, t1, count, g=0.3, lo=1200, hi=6500, chunk=0.25):
    """осипання уламків: рідшаючі короткі клацання; частина - важчі шматки (400-1300 Гц)"""
    r = np.random.default_rng(c.sd())
    span = t1 - t0
    ts = t0 + span * (1 - np.exp(-r.uniform(0, 3.0, count))) / (1 - np.exp(-3.0))
    for ti in ts:
        decay = np.exp(-(ti - t0) / span * 3.2)
        if r.random() < chunk:
            n = int(r.uniform(0.012, 0.03) * SR)
            x = pk(filt(N(n, c.sd()), "bp", r.uniform(450, 1300), 1.4, 1)) * env(n, 0.3, r.uniform(4, 9))
            c.add(x, ti, g * 1.3 * decay * r.uniform(0.5, 1))
        else:
            n = int(r.uniform(0.003, 0.012) * SR) + 32
            x = pk(filt(N(n, c.sd()), "bp", float(np.exp(r.uniform(np.log(lo), np.log(hi)))), 1.2, 1)) * env(n, 0.15, r.uniform(1.2, 3.5))
            c.add(x, ti, g * decay * r.uniform(0.25, 1))


def tube(c, t, g=0.5, lo=600, hi=3200, tau=9.0):
    """глухий удар сталевої труби об бетон/мат: широка смуга з випадковою центральною частотою, без кільця"""
    r = np.random.default_rng(c.sd())
    n = int((tau * 4 / 1000) * SR) + 64
    fc = float(np.exp(r.uniform(np.log(lo), np.log(hi))))
    x = pk(filt(N(n, c.sd()), "bp", fc, 1.1, 1)) * env(n, 0.2, tau)
    x += 0.5 * pk(bn(n, c.sd(), 1800, 7500, 1)) * env(n, 0.1, tau * 0.35)
    c.add(pk(x), t, g)


def legs_fall(c, t0, count, g=0.45, gap0=0.09, ratio=0.72, lo=600, hi=3200):
    """ніжки падають і відскакують: проміжки скорочуються й тихішають, але нерівно"""
    r = np.random.default_rng(c.sd())
    t, gap, gi = t0, gap0, g
    for _ in range(count):
        tube(c, t, gi * r.uniform(0.7, 1.0), lo, hi, tau=r.uniform(5, 13))
        if r.random() < 0.5:
            tube(c, t + r.uniform(0.004, 0.02), gi * 0.5, lo, hi, tau=r.uniform(4, 9))
        t += gap * r.uniform(0.7, 1.25)
        gap *= ratio
        gi *= 0.72


def floor_thud(c, t, g=1.0, heavy=1.0, subtau=70.0, meattau=38.0):
    """важке тіло на бетон з матом: ляпас по мату, м'ясо 50-200 Гц, короткий sub; без буму помосту"""
    slap(c, t, g * 0.55, 700, 3500, 6.0, 0.5, 0.05)
    meat(c, t, g * 1.0 * heavy, 55, 200, meattau, 1.0, 0.25)
    knock(c, t, g * 0.25, 300, 1000, 9.0)
    sub(c, t, g * 0.55 * heavy, 32, 80, subtau, 0.3)


def tail(c, t=0.0, dur=0.6, g=0.15, lo=60, hi=700, tau=250.0):
    n = int(dur * SR)
    c.add(pk(bn(n, c.sd(), lo, hi)) * env(n, 6.0, tau), t, g)


# ---------------------------------------------------------------- рецепти
R = {}


def rec(slot, dur, labels):
    def deco(f):
        R[slot] = (dur, TARGETS[slot], labels, f)
        return f
    return deco


@rec("weapon_table", (1.08, 1.5, 1.5), [
    ("Варіант 1 — ламається навпіл під спиною",
     "Тіло падає на стільницю: ляпас і глухий удар, одразу густа серія з 6–7 гострих тріщин фанери 300–1500 Гц за ~90 мс, за 75 мс важкий удар тіла об бетон з матом, потім ніжки падають із відскоками й осипаються уламки. Найкоротший і найрізкіший."),
    ("Варіант 2 — тріщить, прогинається, тримає",
     "Спершу удар тіла по стільниці і довге деревне тертя з рідкими тріщинами та скрипом сталевих шарнірів: стіл прогинається, але не розвалюється. Потім тіло зісковзує й важко падає на бетон з матом (тертя одягу, глухий удар), стіл дозвучує двома-трьома клацаннями. Повільніший, виснажливий."),
    ("Варіант 3 — важкий удар, довге осипання",
     "Один дуже важкий удар тіла з єдиною великою тріщиною і низьким глухим тріском стільниці, потім довге падіння ніжок: складані труби гепають об бетон з відскоками, що скорочуються, і ще секунду сиплеться дрібний і середній уламок. Найдовший хвіст."),
])
def _wt(v, c):
    if v == 1:
        body(c, 0.0, 0.6, s=0.6, m=0.8, k=0.3)
        crack_burst(c, 0.004, 0.09, 7, 0.7, 280, 1600, 0.4)
        crack(c, 0.0, 0.5, fres=520, tau_res=18)
        tail(c, 0.0, 0.18, 0.3, 120, 700, 40)
        floor_thud(c, 0.075, 1.5, 1.2, subtau=28.0, meattau=17.0)
        legs_fall(c, 0.19, 5, 0.3, 0.085, 0.72, 500, 2600)
        rub(c, 0.1, 0.25, 0.1, 400, 3500)
        debris(c, 0.14, 1.0, 40, 0.2)
    elif v == 2:
        body(c, 0.0, 0.8, s=0.9, m=0.9, k=0.3)
        creak(c, 0.01, 0.55, 5, 250, 1100, 0.45, tau=(5, 22), burst=(3, 7), gap=(0.005, 0.014), q=2.5)
        creak(c, 0.1, 0.5, 3, 1800, 4200, 0.18, tau=(3, 10), burst=(2, 5), gap=(0.006, 0.016), q=3.0)
        crack(c, 0.12, 0.6, fres=420, tau_res=14)
        crack(c, 0.31, 0.75, fres=640, tau_res=12)
        crack(c, 0.47, 0.5, fres=360, tau_res=16)
        rub(c, 0.55, 0.38, 0.32, 250, 3200, 18.0)
        floor_thud(c, 0.74, 1.6, 1.3)
        rub(c, 0.76, 0.2, 0.18, 300, 2500)
        tube(c, 0.9, 0.35, 700, 2600, 8)
        crack(c, 1.02, 0.3, fres=800, tau_res=8)
        tube(c, 1.1, 0.25, 800, 2800, 7)
        debris(c, 0.78, 1.45, 12, 0.18, chunk=0.35)
    else:
        body(c, 0.0, 0.7, s=0.5, m=1.1, k=0.35, mlo=60, mhi=240)
        crack(c, 0.0, 0.9, fres=450, q=1.3, tau_res=24, dur=0.12)
        crack(c, 0.012, 0.45, fres=1100, tau_res=10)
        tail(c, 0.0, 0.3, 0.45, 80, 500, 70)
        floor_thud(c, 0.09, 1.6, 1.4)
        tube(c, 0.2, 0.3, 600, 3000, 10)
        legs_fall(c, 0.28, 6, 0.3, 0.16, 0.78, 500, 2400)
        debris(c, 0.14, 1.45, 60, 0.2, chunk=0.2)


def _band_whoosh(c, t, dur, g, f_start, f_peak, f_end, t_peak, lo=250.0, width=0.2, base=0.35, **_):
    """свіп без тону: широкосмуговий шум крізь ФНЧ із плавно змінним зрізом (f_start -> f_peak у t_peak -> f_end)
    і ФВЧ, що рухається слідом; без вузьких смуг, тож горизонтальних ліній немає. Зріз ще й нерівно гойдається."""
    n = int(dur * SR)
    tt = np.linspace(0, 1, n)
    r = np.random.default_rng(c.sd())
    wob = filt(N(n, c.sd()), "lp", 18, 0.7, 2)
    wob = wob / max(np.abs(wob).max(), 1e-9)
    up = np.clip(tt / t_peak, 0, 1)
    dn = np.clip((tt - t_peak) / (1 - t_peak), 0, 1)
    lf = np.where(tt < t_peak, np.log(f_start) + (np.log(f_peak) - np.log(f_start)) * np.sin(up * np.pi / 2) ** 1.3,
                  np.log(f_peak) + (np.log(f_end) - np.log(f_peak)) * np.sin(dn * np.pi / 2))
    fc = np.exp(lf + 0.18 * wob)
    x = N(n, c.sd())
    x = sweep_filter(sweep_filter(x, fc, "lp", 0.7, 9), fc, "lp", 0.7, 9)
    x = sweep_filter(x, np.maximum(lo, fc * 0.18), "hp", 0.7, 9)
    amp = (np.exp(-0.5 * ((tt - t_peak) / width) ** 2) + base * np.exp(-tt / 0.12)) * (0.85 + 0.3 * np.abs(wob))
    amp = amp * (1 - np.exp(-tt / 0.002))
    c.add(pk(x * amp), t, g)


@rec("whiff", 0.32, [
    ("Варіант 1 — широкий хук рукою",
     "Важка рука проходить повз: смуговий шум 300 Гц–3 кГц наростає за ~110 мс і спадає, на початку вже є шурхіт рукава, на спаді — одне зсування підошви по полотну. Тіло тягне вперед, але нічого не торкається."),
    ("Варіант 2 — низький удар ногою повз",
     "Нога розрізає повітря нижче й довше: шум із плавним зрізом (без вузьких смуг), центр спектра іде з ~200 Гц до ~1 кГц і назад, верх майже до 2,5 кГц, глухий рух штанини і помітне тертя підошви о полотно в кінці (ковзання, а не удар)."),
    ("Варіант 3 — короткий різкий джеб повз",
     "Коротка швидка рука: вищий свіп 700 Гц–3,5 кГц за ~60 мс, ледь чутний шурхіт манжети, потім два дрібних кроки-шаркання ногами по полотну. Найкоротший."),
])
def _wf(v, c):
    if v == 1:
        rub(c, 0.0, 0.12, 0.5, 600, 3500, 30.0)
        _band_whoosh(c, 0.0, 0.3, 1.0, 300, 3000, 1200, 0.38)
        rub(c, 0.15, 0.17, 0.35, 250, 2800, 25.0)
        slap(c, 0.255, 0.1, 500, 2200, 8.0, 1.5, 0.04)
    elif v == 2:
        rub(c, 0.0, 0.2, 0.12, 200, 1200, 18.0)
        _band_whoosh(c, 0.0, 0.36, 1.0, 180, 1700, 450, 0.45, lo=120.0, width=0.17, base=1.0)
        meat(c, 0.2, 0.06, 70, 220, 20.0)
        rub(c, 0.2, 0.15, 0.9, 300, 1500, 55.0)
        rub(c, 0.25, 0.08, 0.45, 400, 1400, 70.0)
    else:
        rub(c, 0.0, 0.07, 0.55, 1200, 5000, 34.0)
        _band_whoosh(c, 0.0, 0.16, 1.0, 700, 3500, 2000, 0.45, width=0.2)
        rub(c, 0.1, 0.08, 0.4, 400, 3000, 40.0)
        slap(c, 0.16, 0.18, 700, 3000, 7.0, 1.0, 0.04)
        slap(c, 0.27, 0.12, 700, 2600, 7.0, 1.0, 0.04)
        rub(c, 0.15, 0.2, 0.1, 400, 2400, 30.0)


@rec("count_out", 0.24, [
    ("Варіант 1 — долоня по фартуху рингу",
     "Плоска долоня ляскає по тканинній спідниці на фанерному краю помосту: сухий ляпас 0,9–4,5 кГц, порожнистий глухий відгук панелі 150–450 Гц і коротке тертя тканини. Пустіше й гулкіше."),
    ("Варіант 2 — кулак по краю помосту",
     "Кулак у фанерний край: сухий короткий стук 1–3 кГц у перші ~8 мс, одразу щільний глухий широкосмуговий удар 40–1000 Гц, що гасне без дзвеніння (кільця немає). Найважчий і найнижчий."),
    ("Варіант 3 — долоня по брезенту на бетоні",
     "Суха долоня по брезенту на бетоні: ляпас 1,5–6 кГц, тіло удару 100–600 Гц близько 0,12 с і нерівне м'яке шарудіння складок брезенту (три дрібні сплески, не ритмічні клацання)."),
])
def _co(v, c):
    if v == 1:
        slap(c, 0.0, 0.8, 900, 4500, 7.0, 0.5, 0.07)
        n = int(0.2 * SR)
        panel = pk(filt(N(n, c.sd()), "bp", 260, 1.2, 1)) * env(n, 1.5, 40.0)
        c.add(panel, 0.0, 0.9)
        meat(c, 0.0, 0.35, 90, 220, 25.0)
        rub(c, 0.0, 0.1, 0.22, 500, 3000, 35.0)
    elif v == 2:
        # кулак: сухий стук 1-3 кГц (перші ~8 мс) + широкий глухий удар без дзвеніння (ФНЧ-шум, а не вузька смуга)
        slap(c, 0.0, 2.0, 1000, 3200, 2.5, 0.2, 0.03)
        n = int(0.09 * SR)
        thud = pk(filt(bn(n, c.sd(), 40, None, 1), "lp", 1000, 0.6, 1)) * env(n, 0.8, 10.0)
        c.add(thud, 0.0, 1.0)
        knock(c, 0.0, 0.35, 300, 1100, 5.0)
    else:
        # долоня по брезенту на бетоні: ядро 150-400 Гц ~0.1 с, ляпас, нерівне м'яке шарудіння
        slap(c, 0.0, 0.7, 1500, 6000, 4.0, 0.3, 0.06)
        n = int(0.16 * SR)
        core = pk(filt(bn(n, c.sd(), 100, None, 1), "lp", 600, 0.6, 2)) * env(n, 1.2, 38.0)
        c.add(core, 0.0, 1.0)
        meat(c, 0.0, 0.5, 90, 250, 16.0)
        r = np.random.default_rng(c.sd())
        for ti, dr, gg in ((0.02, 0.035, 0.28), (0.05, 0.05, 0.22), (0.095, 0.06, 0.15)):
            m = int(dr * SR)
            x = pk(filt(bn(m, c.sd(), 900, None, 1), "lp", r.uniform(2800, 4200), 0.6, 1)) * env(m, 4.0, dr * 400)
            c.add(x, ti + r.uniform(0, 0.006), gg)


# ---------------------------------------------------------------- рендер
def finish(y, target, fade=0.04):
    y = filt(y, "hp", 22, 0.7, 1)
    n = len(y)
    f_in = int(0.0003 * SR)
    y[:f_in] *= np.linspace(0, 1, f_in)
    f_out = int(min(fade, n / SR * 0.15) * SR)
    y[-f_out:] *= np.cos(np.linspace(0, np.pi / 2, f_out)) ** 2
    y = y / max(np.max(np.abs(y)), 1e-9)
    for _ in range(6):
        y *= 10 ** ((target - clip_loudness(y)) / 20)
        thr = 0.6
        a = np.abs(y)
        y = np.where(a > thr, np.sign(y) * (thr + (1 - thr) * np.tanh((a - thr) / (1 - thr))), y)
        tp = true_peak_db(y)
        if tp > -1.05:
            y *= 10 ** ((-1.1 - tp) / 20)
    return y


def render(slot, v):
    dur, target, labels, f = R[slot]
    if isinstance(dur, tuple):
        dur = dur[v - 1]
    c = C(dur, zlib.crc32(f"{slot}.v{v}".encode()) % 100000)
    f(v, c)
    return finish(c.y, target, 0.12 if (slot, v) == ("weapon_table", 1) else 0.04), labels[v - 1]


def onset_ms(y):
    """час до першого відліку вище -30 dB від піка, мс"""
    a = np.abs(y)
    return float(np.argmax(a > a.max() * 10 ** (-30 / 20)) / SR * 1000)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--only", default="")
    a = ap.parse_args()
    ad, sd = os.path.join(a.out, "audio"), os.path.join(a.out, "spec")
    os.makedirs(ad, exist_ok=True)
    os.makedirs(sd, exist_ok=True)
    ids = []
    for slot in R:
        if a.only and slot not in a.only.split(","):
            continue
        for v in (1, 2, 3):
            y, (label, design) = render(slot, v)
            i = f"{slot}.v{v}"
            write_wav(os.path.join(ad, i + ".wav"), y)
            spectrogram(y, os.path.join(sd, i + ".png"), f"{i}  {label}")
            ids.append(i)
            print(f"{i:16s} {len(y)/SR:5.2f}s  {clip_loudness(y):6.1f} LUFS  {true_peak_db(y):5.2f} dBTP  onset30={onset_ms(y):.1f}ms")
    if not a.only:
        sheet(ids, sd, os.path.join(sd, "_outside_props.png"), cols=3)


if __name__ == "__main__":
    main()
