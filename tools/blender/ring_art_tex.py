# Графіка для арен «Рингу» БЕЗ СЛІВ (ітерація 2, спортзал за референсом): банери, фартух рингу, банер залу,
# піктограма виходу, табло, картка мерчу, фото 8×10, принти футболок, банер-стенд. Звичайний Python + Pillow:
#
#   python tools/blender/ring_art_tex.py <корінь репо>
#
# Власник 27.10.2026: «Эти надписи должны быть текстом, что бы на другие языки переводить» — тому тут лише фон,
# емблема, смуги, рамки, піктограми й цифри; самі написи — реєстр текстів ring_venues.py (label/wall_label) з ключами
# перекладу (ring_strings_uk.txt), рушій малює їх шрифтом поверх цих площин.
# Промоушен вигаданий, спорткомплекс безіменний: жодних реальних назв, логотипів чи телефонів.
# Виходи: Assets/Art/Ring/Textures/art_*.png.
#
# 06.11.2026 (арт-директор, Статут вигляду docs/ring/DESIGN_SYSTEM.md): палітра — ЛИШЕ з токенів
# (tools/blender/ring_brand.py ← docs/ring/design/brand_tokens.json), знак — єдина печатка без планки
# (tools/ring_design/emblem.py; стара планка читалась як «в'їзд заборонено»), заглушки фото замінено картками борців.

import os, sys, math, random
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = sys.argv[1] if len(sys.argv) > 1 else os.path.abspath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(REPO, "tools", "ring_design"))
import ring_brand as B
from emblem import paste_seal

OUT = os.path.join(REPO, "Assets", "Art", "Ring", "Textures")
GOLD, DARK, RED, WHITE = B.rgb("gold"), B.rgb("ink0"), B.rgb("crimson"), B.rgb("paper")
SIGNAL, COBALT, ROSE, INK1, INK2 = B.rgb("signal"), B.rgb("cobalt"), B.rgb("rose"), B.rgb("ink1"), B.rgb("ink2")


def mix(a, b, t):
    """Суміш двох токенів: t=0 → a, t=1 → b."""
    return tuple(int(round(a[i] * (1 - t) + b[i] * t)) for i in range(3))


STRIPE = mix(RED, DARK, 0.80)        # діагональні смуги на чорному (було (40,10,14))
STRIPE_ROLLUP = mix(RED, DARK, 0.76)
LIGHTBOX_BG, LIGHTBOX_STRIPE = mix(RED, DARK, 0.45), mix(RED, DARK, 0.30)


def emblem(img, cx, cy, r):
    """Печатка промоушену (без слів і без планки): кільце, три канати, восьмипроменева зірка (tools/ring_design/emblem.py)."""
    paste_seal(img, cx, cy, r)


def grain(img, amt=8, seed=7):
    rnd = random.Random(seed)
    px = img.load()
    w, h = img.size
    for y in range(0, h, 2):
        for x in range(0, w, 2):
            r, g, b, a = px[x, y]
            n = rnd.randint(-amt, amt)
            px[x, y] = (max(0, min(255, r + n)), max(0, min(255, g + n)), max(0, min(255, b + n)), a)
    return img


def banner_promo(w=2048, h=512):
    img = Image.new("RGBA", (w, h), (*DARK, 255))
    d = ImageDraw.Draw(img)
    for k in range(-h, w, 40):
        d.line((k, h, k + h * 0.6, 0), fill=STRIPE, width=14)
    d.rectangle((0, 0, w, 22), fill=GOLD)
    d.rectangle((0, h - 22, w, h), fill=GOLD)
    emblem(img, 260, h // 2, 190)
    d.rectangle((520, h * 0.62, 1900, h * 0.64), fill=GOLD)        # лінія під заголовком
    return grain(img)


def apron(w=2048, h=512):
    img = Image.new("RGBA", (w, h), (*DARK, 255))
    d = ImageDraw.Draw(img)
    d.rectangle((0, 40, w, 70), fill=RED)
    d.rectangle((0, h - 70, w, h - 40), fill=RED)
    emblem(img, w // 2, h // 2, 170)
    return grain(img, 6)


def rental(w=2048, h=384):
    img = Image.new("RGBA", (w, h), (24, 92, 64, 255))              # вивіска спорткомплексу — не бренд «Рингу»
    d = ImageDraw.Draw(img)
    d.rectangle((14, 14, w - 14, h - 14), outline=(235, 235, 225), width=8)
    d.ellipse((60, 70, 300, 310), outline=(235, 235, 225), width=14)
    d.line((180, 70, 180, 310), fill=(235, 235, 225), width=10)
    d.arc((-20, 70, 220, 310), 300, 60, fill=(235, 235, 225), width=10)
    d.arc((140, 70, 380, 310), 120, 240, fill=(235, 235, 225), width=10)
    return grain(img, 6)


def exit_sign(w=512, h=192):
    img = Image.new("RGBA", (w, h), (20, 150, 70, 255))             # нормативний зелений знак виходу
    d = ImageDraw.Draw(img)
    x, y = 60, 30
    c = (245, 255, 245)
    d.ellipse((x + 22, y, x + 52, y + 30), fill=c)
    d.line((x + 36, y + 34, x + 30, y + 90), fill=c, width=14)
    d.line((x + 30, y + 90, x + 60, y + 130), fill=c, width=12)
    d.line((x + 30, y + 90, x, y + 128), fill=c, width=12)
    d.line((x + 34, y + 48, x + 70, y + 70), fill=c, width=10)
    d.line((x + 34, y + 48, x + 4, y + 62), fill=c, width=10)
    d.polygon([(150, 96), (190, 66), (190, 126)], fill=c)            # стрілка
    return img


def scoreboard(w=1024, h=384):
    """Табло: корпус, сегментні цифри (цифри не перекладаються), місця під підписи — порожні. Колір цифр — токени."""
    img = Image.new("RGBA", (w, h), (*DARK, 255))
    d = ImageDraw.Draw(img)
    def seg_digit(x, y, s, col):
        t = s // 6
        for (x0, y0, x1, y1) in ((x, y, x + s, y + t), (x, y + s - t // 2, x + s, y + s + t // 2), (x, y + 2 * s - t, x + s, y + 2 * s),
                                 (x, y, x + t, y + 2 * s), (x + s - t, y, x + s, y + 2 * s)):
            d.rectangle((x0, y0, x1, y1), fill=col)
    for x in (110, 220, 694, 804):
        seg_digit(x, 120, 90, SIGNAL)
    for x in (400, 470, 550, 620):
        seg_digit(x, 110, 50, GOLD)
    d.rectangle((40, 30, 330, 80), outline=B.rgb("ink3"), width=3)
    d.rectangle((694, 30, 984, 80), outline=B.rgb("ink3"), width=3)
    return img


def price_card(w=512, h=384):
    """Прайс мерчу на паперовій картці (рядки й суми — текстом від рушія)."""
    img = Image.new("RGBA", (w, h), (*WHITE, 255))
    d = ImageDraw.Draw(img)
    d.rectangle((8, 8, w - 8, h - 8), outline=DARK, width=6)
    d.rectangle((30, 110, w - 30, 114), fill=RED)
    for k in range(4):
        d.line((36, 170 + k * 52, w - 36, 170 + k * 52), fill=mix(WHITE, DARK, 0.18), width=2)
    return img


def rollup(w=512, h=1280):
    """Банер-стенд (85 × 200 см): емблема вгорі, смуги, місце під назву шоу й дату."""
    img = Image.new("RGBA", (w, h), (*DARK, 255))
    d = ImageDraw.Draw(img)
    for k in range(-w, h, 46):
        d.line((0, k, w, k + w * 0.7), fill=STRIPE_ROLLUP, width=16)
    emblem(img, w // 2, 300, 210)
    d.rectangle((40, 640, w - 40, 646), fill=GOLD)
    d.rectangle((40, 1000, w - 40, 1006), fill=GOLD)
    d.rectangle((0, h - 120, w, h), fill=RED)
    return grain(img)


def photo_8x10(k, w=320, h=400):
    """Фото 8×10 з мерч-столу: картка борця — бюст-силует, ребро кольору сторони, золота планка, печатка в куті.
    Сторони: 0 лиходій, 1 герой, 2 бунтар, 3 нейтральна (кольори — токени; було: силует із зигзагом)."""
    pal = [(RED, GOLD, SIGNAL), (COBALT, WHITE, B.rgb("cobaltText")), (INK2, ROSE, ROSE), (INK1, GOLD, GOLD)]
    bg, fg, rim = pal[k % len(pal)]
    img = Image.new("RGBA", (w, h), (*bg, 255))
    d = ImageDraw.Draw(img)
    for i in range(h):
        d.line((0, i, w, i), fill=mix(bg, DARK, 0.55 * (1 - i / h) + 0.05))
    for s in range(-h, w, 38):                                          # діагональні смуги за бюстом
        d.line((s, h, s + h * 0.6, 0), fill=mix(bg, WHITE, 0.10), width=8)
    cx = w // 2 + (k % 3 - 1) * 14
    d.ellipse((cx - 50, 56, cx + 50, 164), fill=DARK)
    d.polygon([(cx - 128, h), (cx - 98, 214), (cx - 44, 172), (cx + 44, 172), (cx + 98, 214), (cx + 128, h)], fill=DARK)
    d.line([(cx - 98, 214), (cx - 44, 172)], fill=rim, width=5)         # ребро світла
    d.line([(cx + 44, 172), (cx + 98, 214)], fill=rim, width=5)
    d.rectangle((0, h - 76, w, h), fill=fg)                             # планка під ім'я (ім'я — текстом у грі)
    d.rectangle((0, h - 76, w, h - 70), fill=DARK)
    emblem(img, 44, 44, 26)
    d.rectangle((0, 0, w - 1, h - 1), outline=WHITE, width=8)
    return img


def shirt_print(k, w=512, h=512):
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    if k % 2 == 0:
        emblem(img, w // 2, h // 2, 180)
    else:
        for i in range(5):
            d.rectangle((80, 120 + i * 60, 432, 150 + i * 60), fill=(*(GOLD if i % 2 else WHITE), 255))
    return img


def flag(w=512, h=1280):
    """Вертикальний прапор-банер з поруччя: чорне полотно, червоний клин, емблема, золоті смуги, хвіст-ластівка."""
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.polygon([(0, 0), (w, 0), (w, h), (w // 2, h - 150), (0, h)], fill=(*DARK, 255))
    d.polygon([(0, 260), (w, 140), (w, 520), (0, 640)], fill=(*RED, 255))
    emblem(img, w // 2, 380, 170)
    for y in (700, 724):
        d.rectangle((40, y, w - 40, y + 10), fill=(*GOLD, 255))
    return grain(img)


def lightbox(w=1024, h=256):
    """Світлова коробка над виходом: емблема й смуги на темно-червоному (рушій додає назву текстом)."""
    img = Image.new("RGBA", (w, h), (*LIGHTBOX_BG, 255))
    d = ImageDraw.Draw(img)
    for k in range(0, w, 32):
        d.line((k, 0, k - 60, h), fill=LIGHTBOX_STRIPE, width=10)
    emblem(img, 130, h // 2, 100)
    emblem(img, w - 130, h // 2, 100)
    d.rectangle((0, 0, w, 12), fill=GOLD)
    d.rectangle((0, h - 12, w, h), fill=GOLD)
    return img


def poster_show(k, w=512, h=724):
    """Афіша наступного шоу (А2): два силуети навпроти, блискавка, рамка; заголовок і дата — текстом.
    Сторони за Статутом: герой — кобальт, лиходій — сигнал; блискавка й рамка — золото."""
    bgs = [INK1, mix(RED, DARK, 0.35), mix(COBALT, DARK, 0.72)]
    bg = bgs[k % len(bgs)]
    img = Image.new("RGBA", (w, h), (*bg, 255))
    d = ImageDraw.Draw(img)
    for i in range(h):
        d.line((0, i, w, i), fill=tuple(min(255, int(v * (0.55 + 0.6 * i / h))) for v in bg))
    for sx, col in ((0.3, SIGNAL), (0.7, B.rgb("cobaltText"))):
        cx = int(w * sx)
        body = [(cx - 120, 620), (cx - 95, 400), (cx - 45, 360), (cx + 45, 360), (cx + 95, 400), (cx + 120, 620)]
        d.ellipse((cx - 50, 250, cx + 50, 350), fill=DARK, outline=col, width=4)       # ребро світла кольору сторони
        d.polygon(body, fill=DARK)
        d.line(body[1:5], fill=col, width=5)
    d.polygon([(w // 2 - 20, 260), (w // 2 + 30, 380), (w // 2 - 5, 380), (w // 2 + 20, 520), (w // 2 - 40, 360), (w // 2 - 5, 360)],
              fill=GOLD)
    emblem(img, w // 2, 110, 70)
    d.rectangle((0, h - 120, w, h), fill=(*RED, 255) if k % 2 else (*GOLD, 255))
    d.rectangle((6, 6, w - 6, h - 6), outline=GOLD, width=6)
    return grain(img, 10, 11 + k)


def runner(w=512, h=2048):
    """Доріжка виходу (1,6 × 6 м): чорний ковролін, червоні краї, емблеми кроком."""
    img = Image.new("RGBA", (w, h), (*mix(DARK, INK1, 0.5), 255))
    d = ImageDraw.Draw(img)
    d.rectangle((0, 0, 40, h), fill=RED)
    d.rectangle((w - 40, 0, w, h), fill=RED)
    d.rectangle((52, 0, 60, h), fill=GOLD)
    d.rectangle((w - 60, 0, w - 52, h), fill=GOLD)
    for y in (h // 4, 3 * h // 4):
        emblem(img, w // 2, y, 150)
    return grain(img, 10, 5)


def pad(w=256, h=512):
    """Подушка кута: чорний вініл з емблемою (вертикальна)."""
    img = Image.new("RGBA", (w, h), (*mix(DARK, INK1, 0.5), 255))
    emblem(img, w // 2, h // 2, 100)
    return grain(img, 6, 3)


def main():
    os.makedirs(OUT, exist_ok=True)
    for old in os.listdir(OUT):                      # прибрати старі текстури зі словами
        if old.startswith("text_") and old.endswith(".png"):
            os.remove(os.path.join(OUT, old))
    jobs = {"art_banner_promo": banner_promo(), "art_apron": apron(), "art_rental": rental(), "art_exit": exit_sign(),
            "art_scoreboard": scoreboard(), "art_price": price_card(), "art_rollup": rollup(), "art_flag": flag(),
            "art_lightbox": lightbox(), "art_runner": runner(), "art_pad": pad()}
    for k in range(3):
        jobs["art_poster_show_%d" % k] = poster_show(k)
    for k in range(4):
        jobs["art_photo_%d" % k] = photo_8x10(k)
    for k in range(2):
        jobs["art_shirt_%d" % k] = shirt_print(k)
    for name, img in jobs.items():
        img.save(os.path.join(OUT, name + ".png"))
        print("ART", name, img.size)

main()
