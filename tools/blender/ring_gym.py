# Спортзал «Рингу» — перша й найпроробленіша арена (ітерація 2, docs/TRPG_DESIGN.md §11.0.3). Підключається з
# ring_venues.py (exec) і заміняє його build_gym.
#
# Власник 27.10.2026: «Перша арена має бути найпропрацьвана, збери більше референсів в інтернеті»; «Ці кадри це
# реальна камери тому додай шо люди з недорогими камерами ходять». Референс — шоу UWA «Бійцівський Дух»
# (24.02.2024, «СК на Відрадному», Київ): спортивне покриття з розміткою, бежевий тиньк із пілястрами, шведська
# стінка, галерея з банером оренди залу, ферма з PAR над рингом, вихід — завіса, банер-стенди, екран під проєктор,
# дим і CO2, LED-планки; біле полотно й білі канати; оператори з ручними камерами. Плюс американські шкільні шоу
# (сталеві ферми стелі, підвісні світильники, складені щити, стільці впритул) і столи мерчу інді-шоу.
# Усе — власне моделювання; написи — реєстр текстів (label/wall_label), у текстурах слів немає.

SPORT_GREEN = "Sport_Floor"

MATS.update({
    "Sport_Floor": ((0.11, 0.36, 0.24), 0.0, 0.5, 0), "Line_White": ((0.92, 0.92, 0.9), 0.0, 0.45, 0),
    "Line_Yellow": ((0.9, 0.72, 0.1), 0.0, 0.45, 0), "Line_Red": ((0.75, 0.12, 0.1), 0.0, 0.45, 0),
    "Wall_Beige": ((0.78, 0.70, 0.56), 0.0, 0.9, 0), "Wall_Dado": ((0.22, 0.38, 0.40), 0.0, 0.6, 0),
    "Skirting": ((0.15, 0.15, 0.16), 0.3, 0.5, 0), "Window_Frame": ((0.88, 0.88, 0.86), 0.0, 0.5, 0),
    "Window_Glass": ((0.35, 0.45, 0.55), 0.1, 0.08, 0.35), "Curtain_Blue": ((0.12, 0.2, 0.38), 0.0, 0.95, 0),
    "Radiator": ((0.86, 0.86, 0.84), 0.6, 0.45, 0), "Wallbar_Wood": ((0.66, 0.46, 0.26), 0.0, 0.5, 0),
    "Truss_Steel": ((0.42, 0.44, 0.46), 0.8, 0.45, 0), "Lamp_Housing": ((0.55, 0.56, 0.58), 0.8, 0.4, 0),
    "Lamp_Lens": ((1.0, 0.97, 0.9), 0.0, 0.3, 3.0), "Duct_Steel": ((0.62, 0.63, 0.65), 0.9, 0.35, 0),
    "Backboard": ((0.92, 0.94, 0.95), 0.0, 0.15, 0), "Rim_Orange": ((0.9, 0.35, 0.05), 0.7, 0.4, 0),
    "Net_White": ((0.92, 0.92, 0.9), 0.0, 0.9, 0), "Pad_Grey": ((0.3, 0.32, 0.34), 0.0, 0.6, 0),
    "Door_Steel": ((0.48, 0.52, 0.56), 0.7, 0.45, 0), "Chrome": ((0.8, 0.82, 0.85), 1.0, 0.15, 0),
    "Rope_Red": ((0.55, 0.05, 0.06), 0.0, 0.85, 0), "Screen_White": ((0.92, 0.92, 0.93), 0.0, 0.7, 0.6),
    "Fog_Black": ((0.05, 0.05, 0.06), 0.4, 0.5, 0), "LEDBar_Lens": ((0.6, 0.35, 1.0), 0.0, 0.3, 6.0),
    "Popcorn": ((0.95, 0.85, 0.5), 0.0, 0.8, 0), "Fridge_Inner": ((0.9, 0.95, 1.0), 0.0, 0.4, 2.0),
    "Can_Red": ((0.7, 0.05, 0.05), 0.8, 0.3, 0), "Can_Blue": ((0.1, 0.25, 0.7), 0.8, 0.3, 0),
    "Pizza_Box": ((0.75, 0.62, 0.42), 0.0, 0.9, 0), "Belt_Gold": ((0.85, 0.65, 0.2), 1.0, 0.25, 0),
    "Belt_Leather": ((0.08, 0.06, 0.05), 0.0, 0.6, 0), "Pegboard": ((0.55, 0.42, 0.28), 0.0, 0.8, 0),
    "Mixer_Body": ((0.12, 0.12, 0.13), 0.5, 0.45, 0), "Knob": ((0.9, 0.9, 0.9), 0.3, 0.4, 0),
    "Clock_Face": ((0.95, 0.95, 0.92), 0.0, 0.5, 0), "Tape_Silver": ((0.6, 0.6, 0.62), 0.5, 0.4, 0),
})
KIT_RULES[0:0] = [
    (("sport_floor",), ("Sport_Floor",)),
    (("plastered_wall_04", "plaster"), ("Wall_Beige",)),
    (("painted_metal_shutter", "metal_painted"), ("Wall_Dado", "Radiator", "Door_Steel", "Lamp_Housing", "Mixer_Body")),
    (("metal_plate_02", "metal_brushed"), ("Truss_Steel", "Duct_Steel", "Chrome")),
    (("wood_board",), ("Wallbar_Wood", "Pegboard")),
    (("velour_velvet", "cloth"), ("Curtain_Blue", "Rope_Red")),
    (("fabric_leather_02", "vinyl"), ("Pad_Grey",)),
]
BEVEL.update({"Wallbar_Wood": 0.006, "Radiator": 0.008, "Door_Steel": 0.01, "Mixer_Body": 0.008, "Pegboard": 0.006,
              "Pizza_Box": 0.004, "Backboard": 0.012, "Fog_Black": 0.01, "Window_Frame": 0.005})

# ---------------------------------------------------------------- будівля

def gym_wall(P, a, b, H, inward, pil_step=6.0, dado=1.5):
    """Стіна спорткомплексу: фарбований цоколь (панель) до dado, бежевий тиньк вище, пілястри, плінтус, карниз."""
    a, b = Vector((a[0], a[1], 0)), Vector((b[0], b[1], 0))
    d = b - a
    L = d.length
    t = d.normalized()
    n = Vector(inward).normalized()
    horiz = abs(t.x) > 0.5
    sz = lambda along, thick, h: (along if horiz else thick, thick if horiz else along, h)
    mid = (a + b) / 2
    c = mid - n * 0.2
    P.box("Wall_Beige", (c.x, c.y, H / 2), sz(L + 0.8, 0.4, H))
    w = mid + n * 0.015
    P.box("Wall_Dado", (w.x, w.y, dado / 2), sz(L, 0.03, dado))
    w = mid + n * 0.035
    P.box("Skirting", (w.x, w.y, 0.06), sz(L, 0.03, 0.12))
    P.box("Window_Frame", (w.x, w.y, dado), sz(L, 0.04, 0.05))
    P.box("Wall_Beige", (w.x, w.y, H - 0.35), sz(L, 0.25, 0.3), bevel=0.02, segs=2)       # карниз
    s_ = pil_step / 2
    while s_ < L:
        q = a + t * s_ + n * 0.15
        P.box("Wall_Beige", (q.x, q.y, H / 2), sz(0.55, 0.3, H), bevel=0.015, segs=2)
        P.box("Wall_Dado", (q.x + n.x * 0.155, q.y + n.y * 0.155, dado / 2), sz(0.57, 0.02, dado))
        s_ += pil_step
    return t, n

def window(P, base, t, n, w, h, z0):
    """Високе вікно: рама з імпостами (3 × 2), скло, захисна сітка з прутів перед ним, штори з боків."""
    base = Vector(base)
    c = base + n * 0.02
    horiz = abs(t.x) > 0.5
    sz = lambda along, thick, hh: (along if horiz else thick, thick if horiz else along, hh)
    P.box("Window_Glass", (c.x, c.y, z0 + h / 2), sz(w, 0.02, h))
    f = base + n * 0.04
    for zz in (z0, z0 + h, z0 + h * 0.62):
        P.box("Window_Frame", (f.x, f.y, zz), sz(w + 0.08, 0.06, 0.07))
    for k in range(4):
        q = f + t * (-w / 2 + k * w / 3)
        P.box("Window_Frame", (q.x, q.y, z0 + h / 2), sz(0.07, 0.06, h))
    g = base + n * 0.14                                           # сітка-захист від м'ячів
    for k in range(int(w / 0.12) + 1):
        q = g + t * (-w / 2 - 0.1 + k * 0.12)
        P.cyl("Chrome", (q.x, q.y, z0 - 0.1), (q.x, q.y, z0 + h + 0.1), 0.004, segs=4)
    for k in range(int((h + 0.2) / 0.12) + 1):
        zz = z0 - 0.1 + k * 0.12
        q0, q1 = g - t * (w / 2 + 0.1), g + t * (w / 2 + 0.1)
        P.cyl("Chrome", (q0.x, q0.y, zz), (q1.x, q1.y, zz), 0.004, segs=4)
    for side in (-1, 1):                                          # штори, зібрані по боках
        q = base + n * 0.08 + t * (side * (w / 2 - 0.22))
        curtain(P, q - t * 0.22, q + t * 0.22, z0 - 0.25, z0 + h + 0.2, "Curtain_Blue")
    P.cyl("Chrome", (base + n * 0.1 - t * (w / 2 + 0.1)).to_tuple()[:2] + (z0 + h + 0.25,),
          (base + n * 0.1 + t * (w / 2 + 0.1)).to_tuple()[:2] + (z0 + h + 0.25,), 0.012, segs=6)       # карниз

def curtain(P, p0, p1, z0, z1, mat, folds=0.06):
    """Штора зі складками між z0 і z1 (синусоїдна драпіровка, низ трохи нерівний)."""
    p0, p1 = Vector(p0), Vector(p1)
    d = p1 - p0
    side = Vector((-d.y, d.x, 0)).normalized()
    n = max(4, int(d.length / 0.06))
    for k in range(n):
        u0, u1 = k / n, (k + 1) / n
        a = p0 + d * u0 + side * folds * math.sin(u0 * math.pi * 9)
        b = p0 + d * u1 + side * folds * math.sin(u1 * math.pi * 9)
        za, zb = z0 + 0.03 * math.sin(u0 * 23), z0 + 0.03 * math.sin(u1 * 23)
        P.quad(mat, [(a.x, a.y, za), (b.x, b.y, zb), (b.x, b.y, z1), (a.x, a.y, z1)])
        P.quad(mat, [(a.x, a.y, z1), (b.x, b.y, z1), (b.x, b.y, zb), (a.x, a.y, za)])

def radiator(P, pos, t, n, w=1.4):
    """Чавунний радіатор секціями під вікном + дерев'яна ґратка-захист."""
    pos = Vector(pos)
    k_ = int(w / 0.09)
    for k in range(k_):
        q = pos + n * 0.08 + t * (-w / 2 + (k + 0.5) * w / k_)
        P.box("Radiator", (q.x, q.y, 0.45), (0.07 if abs(t.x) > 0.5 else 0.12, 0.12 if abs(t.x) > 0.5 else 0.07, 0.55))
    g = pos + n * 0.2
    for k in range(int(w / 0.1) + 1):
        q = g + t * (-w / 2 - 0.05 + k * 0.1)
        P.box("Wallbar_Wood", (q.x, q.y, 0.45), (0.04 if abs(t.x) > 0.5 else 0.02, 0.02 if abs(t.x) > 0.5 else 0.04, 0.8))
    for zz in (0.08, 0.84):
        P.box("Wallbar_Wood", (g.x, g.y, zz), (w + 0.12 if abs(t.x) > 0.5 else 0.03, 0.03 if abs(t.x) > 0.5 else w + 0.12, 0.06))

def wall_bars(P, a, b, n, sections):
    """Шведська стінка: секції 0,9 м — дві стійки, 14 круглих щаблів, кріплення до стіни."""
    a, b = Vector(a), Vector(b)
    t = (b - a).normalized()
    n = Vector(n)
    for s_ in range(sections):
        c = a + t * (s_ * 1.0 + 0.5) + n * 0.12
        for side in (-1, 1):
            q = c + t * side * 0.45
            P.box("Wallbar_Wood", (q.x, q.y, 1.4), (0.05 if abs(t.x) > 0.5 else 0.11, 0.11 if abs(t.x) > 0.5 else 0.05, 2.8),
                  bevel=0.008, segs=2)
            for zz in (0.3, 2.6):
                w_ = q - n * 0.06
                P.box("Chrome", (w_.x, w_.y, zz), (0.04, 0.04, 0.05))
        for k in range(14):
            zz = 0.15 + k * 0.185
            q0, q1 = c - t * 0.42, c + t * 0.42
            P.cyl("Wallbar_Wood", (q0.x, q0.y, zz), (q1.x, q1.y, zz), 0.017, segs=8)

def roof_truss(P, x, y0, y1, zb, zt, step=1.5):
    """Сталева ферма стелі (Warren): верхній і нижній пояси з куточка, розкоси, опорні пластини на стінах."""
    n = max(2, int((y1 - y0) / step))
    for zz, r in ((zb, 0.06), (zt, 0.07)):
        P.box("Truss_Steel", (x, (y0 + y1) / 2, zz), (0.14, y1 - y0, r * 2))
    for k in range(n + 1):
        y = y0 + (y1 - y0) * k / n
        P.cyl("Truss_Steel", (x, y, zb), (x, y, zt), 0.025, segs=6)
        if k < n:
            yn = y0 + (y1 - y0) * (k + 1) / n
            P.cyl("Truss_Steel", (x, y, zb if k % 2 == 0 else zt), (x, yn, zt if k % 2 == 0 else zb), 0.022, segs=6)

def highbay(P, x, y, z_top, drop=1.0):
    """Підвісний світильник: трос, корпус-дзвін (тіло обертання), лінза, захисна ґратка з прутів."""
    P.cyl("Light_Body", (x, y, z_top), (x, y, z_top - drop), 0.006, segs=4)
    zc = z_top - drop
    P.lathe("Lamp_Housing", (x, y, zc - 0.38), [(0.05, 0.38), (0.1, 0.34), (0.2, 0.18), (0.27, 0.04), (0.28, 0.0)], segs=20)
    P.cyl("Lamp_Lens", (x, y, zc - 0.36), (x, y, zc - 0.385), 0.25, segs=20)
    for k in range(8):                                            # ґратка
        a = 2 * math.pi * k / 8
        p0 = Vector((x + 0.29 * math.cos(a), y + 0.29 * math.sin(a), zc - 0.38))
        P.cyl("Chrome", p0, (x + 0.08 * math.cos(a), y + 0.08 * math.sin(a), zc - 0.62), 0.004, segs=4)
    P.cyl("Chrome", (x, y, zc - 0.625), (x, y, zc - 0.615), 0.09, segs=12)
    AREAS.append(((x, y, zc - 0.45), 0.5, (1.0, 0.97, 0.9), 320.0))

def backboard_ceiling(P, x, y, side):
    """Підвісний щит баскетболу, складений угору під стелю (як у референсі): рама, щит, кошик із сіткою."""
    z = 6.9
    P.cyl("Truss_Steel", (x, y - 0.6, z + 1.6), (x, y - 0.6, z + 0.2), 0.04, segs=8)
    P.cyl("Truss_Steel", (x, y + 0.6, z + 1.6), (x, y + 0.6, z + 0.2), 0.04, segs=8)
    P.cyl("Truss_Steel", (x - side * 1.0, y, z + 1.6), (x, y, z + 0.3), 0.035, segs=8)
    P.box("Backboard", (x, y, z), (1.05, 1.8, 0.04), rx=0.0)          # щит лежить горизонтально (складений)
    P.box("Line_Red", (x, y, z - 0.025), (0.45, 0.59, 0.005))
    c = Vector((x - side * 0.55, y, z - 0.1))
    prev = None
    for k in range(17):
        a = 2 * math.pi * k / 16
        q = c + Vector((0.23 * math.cos(a), 0.23 * math.sin(a), 0))
        if prev is not None:
            P.cyl("Rim_Orange", prev, q, 0.01, segs=6)
            P.cyl("Net_White", q, c + Vector((0.15 * math.cos(a), 0.15 * math.sin(a), -0.4)), 0.004, segs=3)
        prev = q

def double_door(P, pos, yaw, sign_key=True):
    """Подвійні сталеві двері: коробка, дві стулки з віконцями, засув «антипаніка», відбійні пластини, знак виходу."""
    f = lambda v: at(pos, yaw, v)
    P.box("Door_Steel", f((0, 0.06, 1.1)), (2.0, 0.12, 2.2))
    for sx in (-1, 1):
        P.box("Door_Steel", f((sx * 0.49, -0.01, 1.05)), (0.94, 0.05, 2.08))
        P.box("Window_Glass", f((sx * 0.49, -0.04, 1.55)), (0.3, 0.01, 0.55))
        P.box("Window_Frame", f((sx * 0.49, -0.045, 1.55)), (0.36, 0.008, 0.61))
        P.cyl("Chrome", f((sx * 0.08, -0.12, 1.0)), f((sx * 0.88, -0.12, 1.0)), 0.018, segs=8)
        for xx in (0.12, 0.84):
            P.box("Chrome", f((sx * xx, -0.07, 1.0)), (0.05, 0.08, 0.08))
        P.box("Chrome", f((sx * 0.49, -0.04, 0.15)), (0.9, 0.01, 0.25))
    P.box("Art_Exit", f((0, -0.05, 2.5)), (0.48, 0.02, 0.18))
    wall_label("sign.exit", "ВИХІД", f((0.07, -0.062, 2.5)), rot2((0, -1, 0), yaw), 0.08, color=(0.96, 1.0, 0.96), emit=2.0,
               width=0.3)

def gallery(P, x0, x1, y_wall, depth, z, rental=True):
    """Галерея на південній стіні (як «балкон» референсу): плита, фасад із банером оренди залу, поруччя з сіткою,
    консолі; під нею — шведська стінка."""
    y_f = y_wall + depth
    P.box("Concrete", ((x0 + x1) / 2, y_wall + depth / 2, z - 0.12), (x1 - x0, depth, 0.24))
    P.box("Wall_Beige", ((x0 + x1) / 2, y_f, z - 0.05), (x1 - x0, 0.14, 0.5), bevel=0.01, segs=2)
    P.box("Wall_Dado", ((x0 + x1) / 2, y_f + 0.075, z + 0.1), (x1 - x0, 0.01, 0.08))
    for x in range(int(x0) + 1, int(x1), 3):                         # консолі
        P.box("Truss_Steel", (x, y_wall + depth / 2, z - 0.45), (0.12, depth, 0.3))
    railing(P, (x0, y_f, z + 0.2), (x1, y_f, z + 0.2), h=0.9)
    for k in range(int((x1 - x0) / 0.15)):                           # сітка поруччя
        xx = x0 + 0.075 + k * 0.15
        P.cyl("Railing_Steel", (xx, y_f, z + 0.25), (xx, y_f, z + 1.05), 0.005, segs=3)
    if rental:
        w = 8.0
        P.box("Art_Rental", (0, y_f + 0.08, z - 0.05), (w, 0.01, w * 384 / 2048))
        wall_label("gym.rental.title", "ОРЕНДА СПОРТИВНИХ ПРИМІЩЕНЬ", (0.0, y_f + 0.085, z + 0.02), (0, 1, 0),
                   0.22, color=(0.96, 0.96, 0.92), align="CENTER", width=w * 0.6)
        wall_label("gym.rental.sub", "ФУТБОЛ · ВОЛЕЙБОЛ · БАСКЕТБОЛ · ЄДИНОБОРСТВА", (0.0, y_f + 0.085, z - 0.22),
                   (0, 1, 0), 0.11, color=(0.85, 0.93, 0.88), align="CENTER", width=w * 0.6, font="text")

def court_lines(P, cy, CW, CL):
    """Розмітка спортивного покриття: баскетбол (біла), волейбол (жовта), міні-футбол (червона), гнізда стійок."""
    z = 0.002
    for sy in (-1, 1):
        P.box("Line_White", (0, cy + sy * CW / 2, z), (CL, 0.05, 0.004))
    for sx in (-1, 1):
        P.box("Line_White", (sx * CL / 2, cy, z), (0.05, CW, 0.004))
        P.box("Line_White", (sx * (CL / 2 - 2.9), cy, z), (0.05, 4.9, 0.004))          # лінія штрафного
        for sy in (-1, 1):
            P.box("Line_White", (sx * (CL / 2 - 1.45), cy + sy * 2.45, z), (2.9, 0.05, 0.004))
        prev = None
        for k in range(25):                                                              # трьохочкова дуга
            a = -math.pi / 2 + math.pi * k / 24
            q = Vector((sx * (CL / 2 - 1.575) - sx * 6.75 * math.cos(a), cy + 6.75 * math.sin(a) * 0.98, z))
            if abs(q.y - cy) > CW / 2 - 0.9:
                q.y = cy + math.copysign(CW / 2 - 0.9, q.y - cy)
            if prev is not None:
                m = (prev + q) / 2
                P.box("Line_White", (m.x, m.y, z), ((q - prev).length + 0.02, 0.05, 0.004), rz=math.atan2(q.y - prev.y, q.x - prev.x))
            prev = q
    P.box("Line_White", (0, cy, z), (0.05, CW, 0.004))
    for k in range(48):
        a = 2 * math.pi * k / 48
        P.box("Line_White", (1.8 * math.cos(a), cy + 1.8 * math.sin(a), z), (0.25, 0.05, 0.004), rz=a + math.pi / 2)
    VW, VL = 9.0, 18.0                                                                   # волейбол
    for sy in (-1, 1):
        P.box("Line_Yellow", (0, cy + sy * VW / 2, z + 0.0005), (VL, 0.05, 0.004))
    for x in (-VL / 2, -3.0, 0.0, 3.0, VL / 2):
        P.box("Line_Yellow", (x, cy, z + 0.0005), (0.05, VW, 0.004))
    for sy in (-1, 1):                                                                   # гнізда стійок сітки
        P.cyl("Chrome", (0, cy + sy * (VW / 2 + 1.0), 0.0), (0, cy + sy * (VW / 2 + 1.0), 0.004), 0.07, segs=12)
    for sy in (-1, 1):                                                                   # міні-футбол
        P.box("Line_Red", (0, cy + sy * (CW / 2 + 0.6), z + 0.001), (CL + 1.2, 0.05, 0.004))
    for sx in (-1, 1):
        P.box("Line_Red", (sx * (CL / 2 + 0.6), cy, z + 0.001), (0.05, CW + 1.2, 0.004))

# ---------------------------------------------------------------- шоу

def box_truss(P, p0, p1, size=0.3, mat="Truss_Alu"):
    """Ферма 30 × 30: чотири пояси Ø48, розкоси Ø20 зигзагом на кожній грані, кінцеві пластини."""
    truss_beam(P, p0, p1, size, mat)

def goalpost_truss(P, x0, x1, y, h=5.0, pars=8, aim=(0, 0, 1.5)):
    """«Ворота»: дві вежі з ферми на опорних плитах і ригель над рингом із PAR (дешевий інді-варіант)."""
    with item("goalpost_truss"):
        for x in (x0, x1):
            P.box("Truss_Black", (x, y, 0.01), (0.6, 0.6, 0.02))
            box_truss(P, (x, y, 0.02), (x, y, h + 0.15), 0.3, "Truss_Alu")
            P.box("Truss_Alu", (x, y, h + 0.15), (0.32, 0.32, 0.32))                     # кутовий блок
        box_truss(P, (x0 + 0.16, y, h + 0.15), (x1 - 0.16, y, h + 0.15), 0.3, "Truss_Alu")
        for k in range(pars):
            x = x0 + 0.6 + k * (x1 - x0 - 1.2) / max(1, pars - 1)
            P.cyl("Light_Body", (x, y - 0.18, h), (x, y - 0.18, h - 0.12), 0.012, segs=6)   # хомут
            par_can(P, (x, y - 0.18, h - 0.25), (aim[0] + (x - (x0 + x1) / 2) * 0.25, aim[1] + (-1.2 if k % 2 else 1.2), aim[2]),
                    ("Light_Lens", "Light_Lens", "Light_Lens_Blue")[k % 3])

def led_bar(P, pos, yaw, length=1.0):
    """LED-планка на підлозі: корпус, лінза, ніжки — підсвічує дим знизу."""
    f = lambda v: at(pos, yaw, v)
    with item("led_bar"):
        P.box("Light_Body", f((0, 0, 0.06)), (length, 0.08, 0.08), rz=yaw, bevel=0.01)
        P.box("LEDBar_Lens", f((0, -0.041, 0.065)), (length - 0.04, 0.004, 0.04), rz=yaw)
        for sx in (-1, 1):
            P.box("Light_Body", f((sx * (length / 2 - 0.05), 0, 0.01)), (0.04, 0.16, 0.02), rz=yaw)

def fog_machine(P, pos, yaw):
    f = lambda v: at(pos, yaw, v)
    with item("fog_machine"):
        P.box("Fog_Black", f((0, 0, 0.12)), (0.5, 0.26, 0.2), rz=yaw)
        P.cyl("Chrome", f((0, -0.13, 0.14)), f((0, -0.2, 0.14)), 0.02, segs=8)
        P.cyl("Light_Body", f((-0.2, 0.1, 0.22)), f((-0.2, 0.1, 0.26)), 0.03, segs=8)
        P.box("Chrome", f((0, 0, 0.23)), (0.3, 0.03, 0.03), rz=yaw)

def co2_jet(P, pos):
    with item("co2_jet"):
        x, y = pos[0], pos[1]
        P.cyl("Light_Body", (x, y, 0.0), (x, y, 0.03), 0.18, segs=14)
        P.cyl("Chrome", (x, y, 0.03), (x, y, 0.55), 0.05, segs=10)
        P.cyl("Fog_Black", (x, y, 0.55), (x, y, 0.62), 0.07, 0.04, segs=10)

def rollup_banner(P, pos, yaw, title_key, title, sub_key, sub):
    """Банер-стенд 85 × 200 см: касета на підлозі, телескопічна щогла, полотно з графікою; написи — текстом."""
    f = lambda v: at(pos, yaw, v)
    with item("rollup"):
        P.box("Chrome", f((0, 0, 0.05)), (0.9, 0.22, 0.09), rz=yaw, bevel=0.02, segs=2)
        P.cyl("Chrome", f((0, 0.05, 0.1)), f((0, 0.05, 2.08)), 0.008, segs=6)
        P.box("Art_Rollup", f((0, 0, 1.1)), (0.85, 0.01, 2.0), rz=yaw)
    nf = rot2((0, -1, 0), yaw)
    wall_label(title_key, title, f((0, -0.006, 1.28)), nf, 0.11, color=(0.95, 0.95, 0.95), width=0.75)
    wall_label(sub_key, sub, f((0, -0.006, 0.55)), nf, 0.05, color=(0.91, 0.73, 0.24), width=0.75, font="text")

def projector_screen(P, pos, yaw):
    """Екран на штативі (як у референсі — заставки й промо) + проєктор на стійці перед ним."""
    f = lambda v: at(pos, yaw, v)
    with item("projector_screen"):
        for sx in (-1, 1):
            q = f((sx * 0.4, 0.25, 0))
            for k in range(3):
                a = k * 2 * math.pi / 3
                P.cyl("Light_Body", (q.x + 0.4 * math.cos(a), q.y + 0.4 * math.sin(a), 0), (q.x, q.y, 0.7), 0.012, segs=5)
        P.cyl("Light_Body", f((-0.4, 0.25, 0.7)), f((-0.4, 0.25, 2.9)), 0.02, segs=6)
        P.cyl("Light_Body", f((0.4, 0.25, 0.7)), f((0.4, 0.25, 2.9)), 0.02, segs=6)
        P.box("Screen_Frame", f((0, 0.25, 2.25)), (3.1, 0.06, 1.8), rz=yaw)
        P.decal("LED_Content", f((0, 0.2, 2.25)), rot2((1.45, 0, 0), yaw), Vector((0, 0, 0.82)))
    q = f((0, -4.5, 0))
    with item("projector"):
        P.cyl("Light_Body", (q.x, q.y, 0), (q.x, q.y, 1.3), 0.025, segs=6)
        for k in range(3):
            a = k * 2 * math.pi / 3
            P.cyl("Light_Body", (q.x + 0.35 * math.cos(a), q.y + 0.35 * math.sin(a), 0), (q.x, q.y, 0.5), 0.012, segs=5)
        P.box("Mixer_Body", (q.x, q.y, 1.42), (0.32, 0.26, 0.12), rz=yaw)
        c = f((0, -4.36, 1.42))
        P.cyl("Camera_Lens", c, f((0, -4.3, 1.42)), 0.035, segs=10)

def pipe_drape(P, x, y0, y1, h=3.2, gap=(-1.3, 1.3)):
    """Завіса на трубах (труби, бази, поперечина) з проходом посередині — закриває двері в закулісся."""
    with item("pipe_drape"):
        ys = [y0, gap[0], gap[1], y1]
        for y in (y0, gap[0], gap[1], y1, (y0 + gap[0]) / 2, (gap[1] + y1) / 2):
            P.box("Fog_Black", (x, y, 0.01), (0.45, 0.45, 0.02))
            P.cyl("Chrome", (x, y, 0.02), (x, y, h), 0.025, segs=8)
        P.cyl("Chrome", (x, y0, h), (x, y1, h), 0.02, segs=8)
        drape(P, (x - 0.04, y0, 0), (x - 0.04, gap[0], 0), h, "Drape_Black")
        drape(P, (x - 0.04, gap[1], 0), (x - 0.04, y1, 0), h, "Drape_Black")

def stanchions(P, pts, h=0.95, sag=0.12):
    """Стійки з мотузкою (огорожа інді-шоу): хромовані стовпчики на важких базах, червоний канат провисає."""
    with item("stanchions"):
        for (x, y) in pts:
            if abs(h - 0.95) < 1e-6 and P.hero("stanchion", "Hero_stanchion__chrome", (x, y, 0)):
                continue
            P.lathe("Chrome", (x, y, 0), [(0.17, 0.0), (0.17, 0.02), (0.06, 0.05), (0.03, 0.07), (0.025, h), (0.04, h + 0.02),
                                          (0.04, h + 0.07), (0.0, h + 0.09)], segs=12)
        for (a, b) in zip(pts, pts[1:]):
            A, B = Vector((a[0], a[1], h - 0.03)), Vector((b[0], b[1], h - 0.03))
            if (B - A).length > 3.0:
                continue
            prev = A
            for k in range(1, 9):
                u = k / 8
                q = A.lerp(B, u) - Vector((0, 0, sag * 4 * u * (1 - u)))
                P.cyl("Rope_Red", prev, q, 0.018, segs=6)
                prev = q

def sound_desk(P, pos, yaw):
    """Стіл звукаря: мікшерний пульт (фейдери, ручки), ноутбук, колонки-монітори, стілець; кабелі до колонок."""
    f = lambda v: at(pos, yaw, v)
    folding_table(P, pos, yaw, length=1.6, cloth="Cloth_Black")
    with item("mixer"):
        P.box("Mixer_Body", f((-0.2, 0, 0.8)), (0.62, 0.42, 0.05), rz=yaw)
        for k in range(12):
            P.box("Knob", f((-0.47 + k * 0.045, 0.06, 0.835)), (0.012, 0.09, 0.008), rz=yaw)
            for j in range(3):
                P.cyl("Knob", f((-0.47 + k * 0.045, -0.06 - j * 0.04, 0.825)), f((-0.47 + k * 0.045, -0.06 - j * 0.04, 0.84)), 0.009, segs=8)
    laptop(P, f((0.42, 0.05, 0)), yaw, 0.76)
    folding_chair(P, f((0, 0.65, 0)), yaw + math.pi, "Chair_Seat_Black", crowd=False)

def merch_table(P, pos, yaw, k=0):
    """Стіл мерчу: скатертина, дошка з футболками на мольберті, фото 8×10, пояси-репліки, фігурки, касова скринька,
    картка з цінами (написи — текстом)."""
    f = lambda v: at(pos, yaw, v)
    fr = rot2((1, 0, 0), yaw)
    folding_table(P, pos, yaw, length=1.83, cloth=("Cloth_Black", "Cloth_Red")[k % 2])
    with item("merch_board"):
        P.box("Pegboard", f((0, 0.5, 1.45)), (1.6, 0.03, 1.0), rz=yaw)
        for sx in (-0.75, 0.75):
            P.cyl("Light_Body", f((sx, 0.55, 0)), f((sx, 0.52, 1.95)), 0.015, segs=6)
            P.cyl("Light_Body", f((sx, 0.55, 0)), f((sx, 0.95, 0.0)), 0.012, segs=5)
            P.cyl("Light_Body", f((sx, 0.52, 1.0)), f((sx, 0.95, 0.0)), 0.012, segs=5)
        for j in range(3):                                               # футболки на дошці
            c = f((-0.5 + j * 0.5, 0.48, 1.45))
            P.box(("Shirt_A", "Shirt_C", "Shirt_B")[(j + k) % 3], c, (0.42 if abs(fr.x) > 0.5 else 0.02, 0.02 if abs(fr.x) > 0.5 else 0.42, 0.55), rz=0)
            P.box(("Shirt_A", "Shirt_C", "Shirt_B")[(j + k) % 3], c + Vector((0, 0, 0.18)), (0.62 if abs(fr.x) > 0.5 else 0.02, 0.02 if abs(fr.x) > 0.5 else 0.62, 0.14))
            P.decal("Art_Shirt_%d" % (j % 2), c - rot2((0, 0.012, 0), yaw), fr * 0.14, Vector((0, 0, 0.14)))
    with item("merch_items"):
        for j in range(6):                                               # фото 8×10 віялом
            P.decal("Art_Photo_%d" % (j % 4), f((-0.75 + j * 0.13, -0.1 + 0.02 * (j % 2), 0.765 + 0.001 * j)),
                    rot2((0.1, 0, 0), yaw + 0.15 * (j - 2.5)), rot2((0, 0.127, 0), yaw + 0.15 * (j - 2.5)))
        for j in range(2):                                               # пояси-репліки
            c = f((0.1 + j * 0.42, -0.08, 0.77))
            P.box("Belt_Leather", c, (0.36, 0.08, 0.01), rz=yaw)
            P.box("Belt_Gold", c + Vector((0, 0, 0.006)), (0.16, 0.12, 0.008), rz=yaw, bevel=0.003)
        for j in range(3):                                               # фігурки в коробках
            P.box("Cardboard", f((0.7 + j * 0.0, 0.15 - j * 0.0 - j * 0.11, 0.86)), (0.1, 0.06, 0.18), rz=yaw)
        P.box("Case_Black", f((-0.62, 0.22, 0.81)), (0.26, 0.18, 0.09), rz=yaw, bevel=0.01)   # касова скринька
        P.box("Art_Price", f((0.0, 0.3, 0.92)), (0.34, 0.01, 0.26), rz=yaw)
    nf = rot2((0, -1, 0), yaw)
    wall_label("merch.price.title", "МЕРЧ", f((0.0, 0.294, 1.0)), nf, 0.035, color=(0.08, 0.08, 0.08), width=0.3)
    for j, (kk, tx, pr) in enumerate((("merch.price.shirt", "Футболка", "450 ₴"), ("merch.price.photo", "Фото 8×10", "100 ₴"),
                                      ("merch.price.autograph", "Автограф", "150 ₴"), ("merch.price.selfie", "Фото з борцем", "200 ₴"))):
        z = 0.95 - 0.045 * (j + 1)
        wall_label(kk, tx, f((-0.15, 0.294, z)), nf, 0.018, color=(0.1, 0.1, 0.1), align="LEFT", width=0.18, font="text")
        wall_label(kk + ".price", pr, f((0.15, 0.294, z)), nf, 0.018, color=(0.66, 0.08, 0.1), align="RIGHT", width=0.1,
                   font="text")

def concession(P, pos, yaw):
    """Кіоск: попкорн-машина на візку, холодильник зі скляними дверима, стіл з піцою й серветками."""
    f = lambda v: at(pos, yaw, v)
    with item("popcorn"):
        c = f((0, 0, 0))
        for sx in (-1, 1):
            for sy in (-1, 1):
                w = f((sx * 0.3, sy * 0.22, 0.08))
                P.cyl("Tire", (w.x - 0.04, w.y, 0.08), (w.x + 0.04, w.y, 0.08), 0.08, segs=10)
        P.box("Line_Red", f((0, 0, 0.5)), (0.7, 0.5, 0.65), rz=yaw, bevel=0.02, segs=2)
        P.box("Window_Glass", f((0, 0, 1.15)), (0.62, 0.45, 0.6), rz=yaw)
        P.box("Popcorn", f((0, 0, 0.9)), (0.58, 0.41, 0.12), rz=yaw)
        P.lathe("Chrome", (c.x, c.y, 1.15), [(0.0, 0.0), (0.12, 0.02), (0.14, 0.14), (0.13, 0.16)], segs=14)
        P.box("Line_Red", f((0, 0, 1.5)), (0.72, 0.52, 0.1), rz=yaw, bevel=0.02, segs=2)
    with item("fridge"):
        q = f((1.1, 0.05, 0))
        P.box("Cooler_White", f((1.1, 0.05, 0.95)), (0.6, 0.6, 1.9), rz=yaw, bevel=0.03, segs=2)
        P.box("Fridge_Inner", f((1.1, -0.26, 0.98)), (0.5, 0.01, 1.6), rz=yaw)
        for j in range(5):
            for i_ in range(6):
                P.cyl(("Can_Red", "Can_Blue", "Bottle")[(i_ + j) % 3], f((0.88 + i_ * 0.085, -0.15, 0.32 + j * 0.3)),
                      f((0.88 + i_ * 0.085, -0.15, 0.48 + j * 0.3)), 0.03, segs=8)
            P.box("Chrome", f((1.1, -0.05, 0.3 + j * 0.3)), (0.5, 0.42, 0.01), rz=yaw)
    folding_table(P, f((-1.3, 0, 0)), yaw, length=1.4, cloth="Cloth_Blue")
    with item("pizza"):
        for j in range(4):
            P.box("Pizza_Box", f((-1.55, 0, 0.805 + j * 0.045)), (0.4, 0.4, 0.04), rz=yaw + 0.05 * j)
        P.box("Cup_White", f((-1.0, 0.1, 0.865)), (0.15, 0.12, 0.16), rz=yaw)

# ---------------------------------------------------------------- спортзал

# ---------------------------------------------------------------- оформлення шоу й деталі залу (власник 27.10.2026:
# «Хочу щоб все виглядало деталізованіше, дизайн був якийсь прикольний, але не втрачати оптимізацію»). Стиль шоу
# «РИНГ» — чорне, червоне, золото; усе дешеве: площини з графікою, злиті сітки, екземпляри.

MATS.update({"Pennant_Red": ((0.62, 0.05, 0.07), 0.0, 0.9, 0), "Pennant_Gold": ((0.85, 0.62, 0.15), 0.2, 0.7, 0),
             "Pennant_Black": ((0.03, 0.03, 0.035), 0.0, 0.9, 0), "Ball_Orange": ((0.75, 0.3, 0.08), 0.0, 0.7, 0),
             "Mat_GymBlue": ((0.12, 0.25, 0.6), 0.0, 0.7, 0), "Conduit": ((0.82, 0.82, 0.8), 0.0, 0.6, 0),
             "Alarm_Red": ((0.75, 0.05, 0.05), 0.2, 0.5, 0), "Ramp_Yellow": ((0.85, 0.68, 0.1), 0.0, 0.7, 0),
             "Neon_Red": ((1.0, 0.15, 0.12), 0.0, 0.3, 12.0), "Neon_Gold": ((1.0, 0.75, 0.3), 0.0, 0.3, 10.0)})
KIT_RULES[0:0] = [(("velour_velvet", "cloth"), ("Pennant_",)), (("fabric_leather_02", "vinyl"), ("Mat_GymBlue",)),
                  (("rubber_tiles", "vinyl"), ("Ramp_Yellow",))]

def bunting(P, a, b, z, sag=0.6, k0=0):
    """Гірлянда прапорців між фермами: шнур провисає, трикутники червоний/чорний/золотий."""
    A, B = Vector((a[0], a[1], z)), Vector((b[0], b[1], z))
    n = max(4, int((B - A).length / 0.32))
    side = (B - A).normalized()
    prev = A
    for k in range(1, n + 1):
        u = k / n
        q = A.lerp(B, u) - Vector((0, 0, sag * 4 * u * (1 - u)))
        P.cyl("Cable_Black", prev, q, 0.004, segs=3)
        c = (prev + q) / 2
        mat = ("Pennant_Red", "Pennant_Black", "Pennant_Gold")[(k + k0) % 3]
        P.quad(mat, [c - side * 0.12, c + side * 0.12, c + Vector((0, 0, -0.32))])
        P.quad(mat, [c + Vector((0, 0, -0.32)), c + side * 0.12, c - side * 0.12])
        prev = q

def hanging_flag(P, pos, n_):
    """Вертикальний прапор з поруччя галереї (ластівчин хвіст), лицем у зал."""
    n_ = Vector(n_).normalized()
    right = Vector((-n_.y, n_.x, 0))
    c = Vector(pos) + n_ * 0.02
    P.decal("Art_Flag", c, right * 0.45, Vector((0, 0, 1.1)))
    P.cyl("Chrome", c - right * 0.5 + Vector((0, 0, 1.12)), c + right * 0.5 + Vector((0, 0, 1.12)), 0.012, segs=6)

def show_poster(P, pos, n_, k, title="НАСТУПНЕ ШОУ", date="СУБОТА · 19:00"):
    n_ = Vector(n_).normalized()
    right = Vector((-n_.y, n_.x, 0))
    c = Vector(pos) + n_ * 0.012
    P.decal("Art_Poster_Show_%d" % (k % 3), c, right * 0.3, Vector((0, 0, 0.42)))
    wall_label("poster.next.title", title, c + Vector((0, 0, -0.22)), n_, 0.05, color=(0.98, 0.98, 0.96), width=0.55)
    wall_label("poster.next.date", date, c + Vector((0, 0, -0.36)), n_, 0.035, color=(0.08, 0.08, 0.08), width=0.55, font="text")

def ball_cage(P, pos):
    """Клітка з м'ячами на коліщатах (спортзал): каркас із прутів, м'ячі всередині."""
    x, y = pos[0], pos[1]
    with item("ball_cage"):
        w, d_, h = 1.0, 0.6, 1.0
        for sx in (-1, 1):
            for sy in (-1, 1):
                P.cyl("Chrome", (x + sx * w / 2, y + sy * d_ / 2, 0.1), (x + sx * w / 2, y + sy * d_ / 2, h), 0.012, segs=6)
                P.cyl("Tire", (x + sx * w / 2 - 0.03, y + sy * d_ / 2, 0.05), (x + sx * w / 2 + 0.03, y + sy * d_ / 2, 0.05), 0.05, segs=8)
        for zz in (0.12, 0.55, h):
            for (a, b) in (((-1, -1), (1, -1)), ((1, -1), (1, 1)), ((1, 1), (-1, 1)), ((-1, 1), (-1, -1))):
                P.cyl("Chrome", (x + a[0] * w / 2, y + a[1] * d_ / 2, zz), (x + b[0] * w / 2, y + b[1] * d_ / 2, zz), 0.008, segs=4)
        for k in range(8):
            xx = x - 0.32 + (k % 4) * 0.21 + 0.03 * h01(k, 501)
            yy = y - 0.12 + (k // 4) * 0.22
            P.lathe("Ball_Orange", (xx, yy, 0.12), [(0.0, 0.0), (0.08, 0.02), (0.115, 0.12), (0.08, 0.215), (0.0, 0.235)], segs=10)

def gym_mats(P, pos, yaw, n=5):
    """Стос гімнастичних матів у куті (сині, з ручками)."""
    f = lambda v: at(pos, yaw, v)
    with item("gym_mats"):
        for k in range(n):
            P.box("Mat_GymBlue", f((0.02 * (k % 2), 0, 0.05 + k * 0.1)), (1.9, 0.95, 0.095), rz=yaw + 0.02 * (k % 3), bevel=0.03, segs=2)

def wall_services(P, a, b, n_, z=2.3, every=3.0):
    """Кабель-канал уздовж стіни з розетками, пожежним сповіщувачем і гучномовцем через крок."""
    a, b = Vector(a), Vector(b)
    n_ = Vector(n_).normalized()
    t = (b - a).normalized()
    c = (a + b) / 2 + n_ * 0.03
    L = (b - a).length
    horiz = abs(t.x) > 0.5
    P.box("Conduit", (c.x, c.y, z), (L if horiz else 0.04, 0.04 if horiz else L, 0.06))
    s_ = every / 2
    k = 0
    while s_ < L:
        q = a + t * s_ + n_ * 0.035
        sz = (0.1 if horiz else 0.03, 0.03 if horiz else 0.1, 0.08)
        P.box("Conduit", (q.x, q.y, 0.35), sz)                          # розетка
        P.box("Conduit", (q.x, q.y, (0.35 + z) / 2), (0.03 if horiz else 0.02, 0.02 if horiz else 0.03, z - 0.35))
        if k % 3 == 1:
            P.box("Alarm_Red", (q.x, q.y, 1.4), (0.12 if horiz else 0.05, 0.05 if horiz else 0.12, 0.14))
        if k % 4 == 2:
            P.box("Speaker_Black", (q.x + n_.x * 0.05, q.y + n_.y * 0.05, 5.2), (0.3 if horiz else 0.18, 0.18 if horiz else 0.3, 0.42),
                  bevel=0.02)
        s_ += every
        k += 1

def cable_ramp(P, pos, yaw, length=0.9):
    """Кабель-переїзд (жовто-чорний горбик) — там, де кабель перетинає прохід."""
    f = lambda v: at(pos, yaw, v)
    with item("cable_ramp"):
        P.box("Ramp_Yellow", f((0, 0, 0.025)), (length, 0.28, 0.05), rz=yaw, bevel=0.02, segs=2)
        for k in range(3):
            P.box("Pennant_Black", f((-length / 2 + (k + 0.5) * length / 3, 0, 0.051)), (0.12, 0.28, 0.002), rz=yaw)

def gym_dressing(root, r, X0, X1, Y0, Y1, H, m, q, hp):
    dz = Parts("dressing", root)
    # над залом: гірлянди між фермами, банер на «воротах»
    for k in range(6):
        x = X0 + 3.0 + k * 5.0
        bunting(dz, (x, Y0 + 2.4), (x + 5.0, Y1 - 1.0), H - 1.45, sag=1.1, k0=k)
    for side in (-1, 1):
        y = side * 0.17
        dz.decal("Art_Banner_Promo", (0, y, 4.55), Vector((side * 1.6, 0, 0)), Vector((0, 0, 0.4)))
        wall_label("promo.truss.title", "РИНГ", (side * -0.55, y + side * 0.01, 4.62), (0, side, 0), 0.24, color=(0.96, 0.96, 0.96),
                   width=1.2)
    # галерея: прапори з поруччя по боках від банера оренди
    for x in (-12.5, -9.5, 6.0):
        hanging_flag(dz, (x, Y0 + 2.2 + 0.03, 3.15), (0, 1, 0))
    # вихід: світлова рамка навколо арки, світлова коробка з назвою, доріжка до рингу
    ax = X0 + 2.95
    for sy in (-1, 1):
        dz.cyl("Neon_Red", (ax + 0.18, sy * 1.28, 0.05), (ax + 0.18, sy * 1.28, 2.75), 0.018, segs=6)
    dz.cyl("Neon_Red", (ax + 0.18, -1.28, 2.75), (ax + 0.18, 1.28, 2.75), 0.018, segs=6)
    dz.box("Light_Body", (ax + 0.12, 0, 3.12), (0.16, 2.6, 0.55), bevel=0.02, segs=2)
    dz.decal("Art_Lightbox", (ax + 0.205, 0, 3.12), Vector((0, -1.25, 0)), Vector((0, 0, 0.25)))
    wall_label("promo.lightbox", "РИНГ", (ax + 0.21, 0, 3.12), (1, 0, 0), 0.26, color=(1.0, 0.9, 0.6), emit=6.0, width=1.6)
    for k in range(2):
        AREAS.append(((ax + 0.6, 0, 3.12), 1.2, (1.0, 0.25, 0.2), 120.0))
    run_len = (-q - 0.1) - (ax + 0.4)
    cx = (ax + 0.4 + (-q - 0.1)) / 2
    dz.box("Carpet_Black", (cx, 0, 0.004), (run_len, 1.6, 0.008))
    for k in range(int(run_len / 6.0) + 1):
        x0 = ax + 0.4 + k * 6.0
        L_ = min(6.0, (-q - 0.1) - x0)
        if L_ > 0.5:
            dz.decal("Art_Runner", (x0 + L_ / 2, 0, 0.0095), Vector((L_ / 2, 0, 0)), Vector((0, 0.8, 0)))
    # підсвітка стін: червоні PAR на підлозі
    for (x, y, tx, ty) in ((-9.0, Y1 - 0.6, -9.0, Y1), (9.0, Y1 - 0.6, 9.0, Y1), (X1 - 0.6, 12.6, X1, 12.6), (X1 - 0.6, 3.0, X1, 3.0),
                           (-6.0, Y0 + 0.7, -6.0, Y0), (4.0, Y0 + 0.7, 4.0, Y0)):
        with item("uplight"):
            dz.box("Light_Body", (x, y, 0.08), (0.3, 0.3, 0.04))
            par_can(dz, (x, y, 0.3), (tx, ty, 5.0), "Light_Lens_Red")
    # афіші наступних шоу біля входу глядачів і на заході
    for k, (pos, n_) in enumerate((((X1, -5.2, 1.9), (-1, 0, 0)), ((X1, -4.4, 1.9), (-1, 0, 0)), ((X1, 8.4, 1.9), (-1, 0, 0)),
                                   ((X0, 6.5, 1.9), (1, 0, 0)), ((X0, -6.0, 1.9), (1, 0, 0)))):
        show_poster(dz, pos, n_, k)
    # залізо залу: кабель-канали з розетками, сповіщувачами й гучномовцями; клітка з м'ячами; мати
    wall_services(dz, (X1, Y0 + 1.0, 0), (X1, Y1 - 1.0, 0), (-1, 0, 0))
    wall_services(dz, (X0 + 4.6, Y1, 0), (X1 - 1.0, Y1, 0), (0, -1, 0))
    ball_cage(dz, (X0 + 1.2, Y0 + 0.8, 0))
    gym_mats(dz, (X1 - 2.8, Y0 + 0.75, 0), 0.0)
    cable_ramp(dz, (-q - 1.2, 0, 0), math.pi / 2, 1.4)
    for k in range(5):                                                   # скотч-мітки на полотні й на підлозі
        floor_decal(dz, "Decal_Tape", (-q - 0.6 + 0.0 * k, -1.5 + 0.75 * k), 0.3, 0.3 * k)
    dz.finish()
    # речі натовпу: рюкзаки під стільцями, куртки на спинках (екземпляри)
    for k, (pm, pp_, py, pc) in enumerate(list(PLACE)):
        if pm != "chair_folding" or k in STAFF:
            continue
        u = h01(k, 511)
        col = ((0.05, 0.05, 0.06), (0.15, 0.2, 0.35), (0.35, 0.08, 0.08), (0.3, 0.3, 0.28), (0.12, 0.25, 0.14))[int(h01(k, 512) * 5) % 5]
        if u < 0.3:
            PLACE.append(("bag", pp_, py + (h01(k, 513) - 0.5) * 0.6, col))
        elif u < 0.48:
            PLACE.append(("jacket", pp_, py, col))

def build_gym(root):
    spec = dict(size_ft=16, mat_z=1.0, canvas="Canvas_White", apron="Apron_Black", band="Apron_Band_Red",
                post="Post_Black", pads=("Pad_Black",), steps=("NW", "SE"), steps_mat="Steps_Steel", mats="Mat_Blue",
                mats_extra=1.22, rope_colors=("White", "White", "White"))
    r = build_ring(root, spec)
    X0, X1, Y0, Y1, H = -18.0, 18.0, -12.0, 14.0, 9.0
    m = r["mats_half"]
    hp = r["half_plat"]

    # --- підлога: спортивне покриття з розміткою
    fl = Parts("gym", root)
    fl.box("Sport_Floor", ((X0 + X1) / 2, (Y0 + Y1) / 2, -0.05), (X1 - X0, Y1 - Y0, 0.1))
    court_lines(fl, 1.0, 15.0, 28.0)
    fl.finish()

    # --- стіни: цоколь, тиньк, пілястри; вікна з сітками й шторами на півночі (над трибунами) й сході; радіатори
    walls = {k_: Parts("wall_" + k_, root) for k_ in "NSEW"}
    tS, nS = gym_wall(walls["S"], (X0, Y0), (X1, Y0), H, (0, 1, 0))
    tN, nN = gym_wall(walls["N"], (X0, Y1), (X1, Y1), H, (0, -1, 0))
    tE, nE = gym_wall(walls["E"], (X1, Y0), (X1, Y1), H, (-1, 0, 0))
    tW, nW = gym_wall(walls["W"], (X0, Y0), (X0, Y1), H, (1, 0, 0))
    for x in (-12.0, -6.0, 0.0, 6.0, 12.0):
        window(walls["N"], (x + 3.0, Y1, 0), tN, nN, 2.4, 2.2, 5.9)
    for y in (-6.0, 0.0, 6.0):
        window(walls["E"], (X1, y + 3.0, 0), tE, nE, 2.4, 2.2, 5.9)
        radiator(walls["E"], (X1, y + 3.0, 0), tE, nE)
    for y in (-6.0, 6.0):
        window(walls["W"], (X0, y + 3.0, 0), tW, nW, 2.4, 2.2, 5.9)
    double_door(walls["E"], (X1 - 0.02, -3.5, 0), -math.pi / 2)                          # вхід глядачів
    double_door(walls["E"], (X1 - 0.02, 10.0, 0), -math.pi / 2)
    double_door(walls["W"], (X0 + 0.02, 0.0, 0), math.pi / 2)                            # у закулісся (за завісою)
    double_door(walls["S"], (12.0, Y0 + 0.02, 0), math.pi)
    for P_ in walls.values():
        P_.finish()

    # --- галерея на південній стіні з банером оренди, під нею шведська стінка; табло й годинник
    gal = Parts("wall_S_gallery", root)                                  # частина південної стіни: камера ховає разом
    gallery(gal, -14.0, 9.0, Y0, 2.2, 4.0)
    wall_bars(gal, (-13.0, Y0, 0), (-3.0, Y0, 0), (0, 1, 0), 9)
    wall_bars(gal, (2.5, Y0, 0), (8.5, Y0, 0), (0, 1, 0), 6)
    gal.finish()
    gal = Parts("wall_N_scoreboard", root)                                # табло й годинник — на північній стіні
    gal.box("Scoreboard", (0, Y1 - 0.12, 4.2), (3.2, 0.2, 1.2), bevel=0.02)
    gal.box("Art_Scoreboard", (0, Y1 - 0.225, 4.2), (3.0, 0.01, 1.1))
    for kk, tx, xx in (("gym.score.home", "ГОСПОДАРІ", -1.05), ("gym.score.guest", "ГОСТІ", 1.05)):
        wall_label(kk, tx, (xx, Y1 - 0.226, 4.62), (0, -1, 0), 0.07, color=(0.9, 0.9, 0.9), emit=1.5, width=0.85, font="text")
    gal.cyl("Light_Body", (14.5, Y1 - 0.05, 4.4), (14.5, Y1 - 0.15, 4.4), 0.24, segs=24)          # годинник у ґратці
    gal.cyl("Clock_Face", (14.5, Y1 - 0.151, 4.4), (14.5, Y1 - 0.155, 4.4), 0.21, segs=24)
    for k in range(12):
        a = 2 * math.pi * k / 12
        gal.box("Light_Body", (14.5 + 0.18 * math.cos(a), Y1 - 0.157, 4.4 + 0.18 * math.sin(a)), (0.012, 0.004, 0.035))
    for k in range(7):
        gal.cyl("Chrome", (14.25 + k * 0.083, Y1 - 0.2, 4.15), (14.25 + k * 0.083, Y1 - 0.2, 4.65), 0.004, segs=3)
    gal.finish()

    # --- стеля: ферми, підвісні світильники, повітропровід, складені щити баскетболу
    ceil = Parts("ceiling", root)
    ceil.box("Ceiling", ((X0 + X1) / 2, (Y0 + Y1) / 2, H + 0.05), (X1 - X0, Y1 - Y0, 0.1))
    for k in range(7):
        roof_truss(ceil, X0 + 3.0 + k * 5.0, Y0, Y1, H - 1.4, H - 0.15)
    for k in range(6):
        for j in range(3):
            highbay(ceil, X0 + 5.5 + k * 5.0, Y0 + 5.0 + j * 7.0, H - 1.4, drop=0.7)
    ceil.box("Duct_Steel", (0, Y0 + 3.4, H - 1.0), (X1 - X0 - 1.0, 0.8, 0.6))
    for k in range(10):
        ceil.box("Light_Body", (X0 + 2.5 + k * 3.4, Y0 + 3.79, H - 1.05), (0.6, 0.02, 0.3))
    for sx in (-1, 1):
        backboard_ceiling(ceil, sx * 13.6, 1.0, sx)
    ceil.finish()

    # --- висувні трибуни біля півночі: алюмінієві дошки, підніжки попереду, торцеві поручні
    bl = Parts("bleachers", root)
    rows, rd, rr = 6, 0.95, 0.42
    for k in range(rows):
        y = Y1 - 0.6 - (rows - 1 - k) * rd
        z = 0.45 + k * rr - 0.025
        bl.box("Bleacher_Wood", (0, y - rd / 2 + 0.2, z), (26.0, 0.32, 0.05), bevel=0.008, segs=2)
        bl.box("Bleacher_Wood", (0, y - rd / 2 - 0.16, z - 0.445), (26.0, 0.36, 0.04), bevel=0.006)
        bl.box("Bleacher_Frame", (0, y, z / 2), (26.0, 0.05, z))
        for s_ in range(46):
            seat_mark((-12.6 + s_ * 0.56, y - rd / 2 + 0.2), 0.0, z + 0.025, "лава")
    for x in (-13.2, 13.2):
        for k in range(rows):
            y = Y1 - 0.6 - (rows - 1 - k) * rd
            z = 0.45 + k * rr
            bl.cyl("Railing_Steel", (x, y - rd / 2, z - 0.42), (x, y - rd / 2, z + 0.5), 0.022, segs=8)
        bl.cyl("Railing_Steel", (x, Y1 - 0.6 - (rows - 1) * rd - rd / 2, 0.5), (x, Y1 - 0.6 - rd / 2, 0.45 + (rows - 1) * rr + 0.5), 0.022, segs=8)
    bl.finish()

    # --- ринг: стійки з мотузкою навколо килимків (замість огорожі), прохід із заходу й до столу хронометриста
    st = Parts("stanchions", root)
    q = m + 0.45
    pts = []
    for (a, b) in (((-q, q), (q, q)), ((q, q), (q, -q)), ((q, -q), (-q, -q)), ((-q, -q), (-q, q))):
        A, B = Vector(a), Vector(b)
        n_ = max(1, round((B - A).length / 1.8))
        for k in range(n_):
            p_ = A.lerp(B, k / n_)
            if p_.x < -q + 0.1 and abs(p_.y) < 1.4:                     # прохід борця
                continue
            if abs(p_.x + 1.2) < 1.2 and p_.y < -q + 0.1:               # прохід до столу хронометриста
                continue
            pts.append((p_.x, p_.y))
    stanchions(st, pts)
    st.finish()

    # --- місця: стільці впритул до стійок, з трьох боків; кути — за південним/північним блоками
    ch = Parts("chairs", root, lod=True)
    qt = q + 0.75
    towers = [Vector((-m - 0.5, 0, 0)), Vector((m + 0.5, 0, 0))]
    no_tow = lambda p_: any((Vector((p_.x, p_.y, 0)) - t_).length < 1.0 for t_ in towers)
    no_cam = lambda p_: no_tow(p_) or (abs(p_.x) < 1.4 and p_.y < -m - 5.0)
    side = lambda p_: no_tow(p_) or abs(p_.y) > m + 0.5
    chair_rows(ch, (0, -qt, 0), 0.0, 5, 22, gap_every=11, gap=1.2, skip=no_cam, focus=(0, 0))
    chair_rows(ch, (qt, 0, 0), 0.0, 4, 18, gap_every=9, gap=1.2, skip=side, focus=(0, 0))
    chair_rows(ch, (-qt, 0, 0), 0.0, 5, 18, gap_every=9, gap=3.0, skip=side, focus=(0, 0))
    chair_rows(ch, (0, qt, 0), 0.0, 2, 18, gap_every=9, gap=1.2, standing=0, skip=no_tow, focus=(0, 0))
    ch.finish()

    # --- світло (дешево, клубно): «ворота» над рингом з PAR, штативи на виході, LED-планки на підлозі
    lt = Parts("lighting", root)
    goalpost_truss(lt, -m - 0.5, m + 0.5, 0.0, h=5.0, pars=8, aim=(0, 0, r["mat_z"] + 0.6))
    lt.finish()

    # --- вихід (захід): завіса на трубах із проходом, арка, банер-стенди, екран і проєктор, дим, CO2, LED-планки
    ent = Parts("entrance", root)
    pipe_drape(ent, X0 + 2.4, -4.5, 4.5, 3.2, gap=(-1.6, 1.6))
    arch_curtain(ent, (X0 + 2.95, 0, 0), math.pi / 2)
    ent.box("Art_Banner_Promo", (X0 + 2.33, 0, 3.55), (0.01, 4.4, 1.1))
    wall_label("promo.title", "РИНГ", (X0 + 2.34, -1.0, 3.66), (1, 0, 0), 0.36, color=(0.96, 0.96, 0.96), align="LEFT", width=1.6)
    wall_label("promo.subtitle", "ЖИВЕ ШОУ", (X0 + 2.34, 0.75, 3.7), (1, 0, 0), 0.2, color=(0.91, 0.73, 0.24), align="LEFT",
               width=1.2)
    wall_label("promo.tagline", "НЕЗАЛЕЖНИЙ РЕСЛІНГ · ЩОМІСЯЦЯ", (X0 + 2.34, -1.0, 3.24), (1, 0, 0), 0.1,
               color=(0.85, 0.85, 0.85), align="LEFT", width=2.8, font="text")
    for sy in (-1, 1):
        rollup_banner(ent, (X0 + 3.1, sy * 2.75, 0), math.pi / 2 - sy * 0.35, "promo.rollup.title", "РИНГ",
                      "promo.rollup.sub", "ЖИВЕ ШОУ · ЩОМІСЯЦЯ")
        light_tree(ent, (X0 + 3.5, sy * 3.9, 0), (-8, 0, 1.5), h=2.8, n=3)
        led_bar(ent, (X0 + 4.6, sy * 1.3, 0), math.pi / 2)
        led_bar(ent, (X0 + 6.4, sy * 1.3, 0), math.pi / 2)
        fog_machine(ent, (X0 + 3.5, sy * 1.05, 0), math.pi / 2)
        co2_jet(ent, (X0 + 3.75, sy * 1.75, 0))
        speaker_tripod(ent, (X0 + 3.6, sy * 5.2, 0), math.pi / 2)
    projector_screen(ent, (X0 + 2.9, -7.4, 0), math.pi / 2 - 0.25)
    ent.finish()
    empty("anchor_curtain", root, (X0 + 2.0, 0, 0), yaw=-90)
    path_anchors(root, [(X0 + 2.6, 0), (X0 + 6.0, 0), (-8.0, 0), (-q - 0.4, 0), (-hp - 0.9, hp - 0.2)])

    # --- біля рингу (південь): стіл хронометриста й ведучого з дзвоном — збоку від сходів SE; звукар; хард-кем
    rs = Parts("ringside", root)
    ty = -hp - 0.48                                     # стільниця впритул до фартуха, стільці — ще на килимках
    mz_ = 0.03                                          # висота килимків
    folding_table(rs, (-1.2, ty, mz_), math.pi, cloth="Cloth_Black")
    ring_bell(rs, (-1.7, ty, 0), 0.78 + mz_)
    laptop(rs, (-0.8, ty, 0), 0.0, 0.76 + mz_)
    for x in (-1.7, -0.7):
        folding_chair(rs, (x, ty - 0.5, mz_), math.pi, "Chair_Seat_Black", crowd=False)
    sound_desk(rs, (X0 + 5.2, -6.2, 0), math.pi / 2)
    rs.box("Case_Black", (0, -m - 6.3, 0.2), (1.2, 1.2, 0.4))
    hd_camera(rs, (0, -m - 6.3, 0), 0.0, 0.4)
    speaker_tripod(rs, (m + 1.0, -m - 0.9, 0), math.radians(-135))
    speaker_tripod(rs, (-m - 1.0, m + 0.9, 0), math.radians(45))
    rs.finish()
    empty("anchor_timekeeper", root, (-1.2, ty - 0.5, 0.03), yaw=180)
    empty("anchor_bell", root, (-1.7, ty, 0.78))
    empty("anchor_hardcam", root, (0, -m - 6.3, 1.8), yaw=0)
    for i, (x, y) in enumerate(((m - 0.3, -m + 0.3), (-m + 0.3, -m + 0.3), (m - 0.3, m - 0.3), (-m + 0.3, m - 0.3))):
        empty("anchor_handheld_%d" % (i + 1), root, (x, y, 1.6))
    for i, (x, y, z, clip) in enumerate(((-3.15, -1.8, 0.03, "Cam_Stand_Film_Loop"), (3.15, 1.4, 0.03, "Cam_Kneel_Film_Loop"),
                                         (1.7, -3.15, 0.03, "Cam_Stand_Film_Loop"), (-2.4, 3.15, 0.03, "Cam_Kneel_Film_Loop"),
                                         (-7.0, 0.55, 0.0, "Cam_Walk_Gimbal_Loop"))):
        empty("anchor_camop_%d" % (i + 1), root, (x, y, z))         # оператори з камерами — біля рингу й у проході
        CAMOPS.append((x, y, z, clip))

    # --- торгівля біля входу глядачів (схід): мерч, кіоск, каса
    mc = Parts("merch", root)
    for k, y in enumerate((-9.8, -7.4)):
        merch_table(mc, (X1 - 1.4, y, 0), -math.pi / 2, k)
    concession(mc, (X1 - 0.9, 6.0, 0), -math.pi / 2)
    folding_table(mc, (X1 - 1.4, -1.2, 0), -math.pi / 2, length=1.4, cloth="Cloth_Blue")
    with item("ticket_desk"):
        mc.box("Case_Black", (X1 - 1.4, -1.0, 0.81), (0.26, 0.2, 0.09), bevel=0.01)
        mc.cyl("Shirt_B", (X1 - 1.35, -1.55, 0.765), (X1 - 1.35, -1.55, 0.81), 0.06, segs=12)
    wall_label("gym.tickets", "КАСА", (X1 - 1.4 - 0.405, -1.2, 0.5), (-1, 0, 0), 0.12, color=(0.95, 0.95, 0.95), width=0.9)
    wall_label("gym.snacks", "КІОСК", (X1 - 0.2, 6.0, 2.3), (-1, 0, 0), 0.18, color=(0.95, 0.85, 0.4), emit=0.8, width=1.2)
    mc.finish()
    empty("anchor_merch", root, (X1 - 2.3, -8.6, 0), yaw=90)
    empty("anchor_tickets", root, (X1 - 2.2, -1.2, 0), yaw=90)

    # --- зброя: стос стільців, драбина, бак, кендо-палиці — біля завіси, поза глядачами
    wp = Parts("weapons", root)
    chair_stack(wp, (X0 + 0.5, 5.6, 0), -math.pi / 2)
    ladder(wp, (X0 + 0.9, 8.4, 0.0), (X0 + 0.25, 8.4, 3.2))
    trash_can(wp, (m - 0.36, m - 0.36, 0.03), "Trash_Grey")                                   # на килимку в куті без сходів
    kendo_bin(wp, (-q + 0.34, -q + 0.34, 0))
    wp.finish()
    empty("anchor_weapon_chairs", root, (X0 + 0.9, 5.6, 0))
    empty("anchor_weapon_ladder", root, (X0 + 1.2, 8.4, 0))
    empty("anchor_weapon_trash", root, (m - 0.36, m - 0.36, 0.03))
    empty("anchor_weapon_kendo", root, (-q + 0.34, -q + 0.34, 0))

    # --- дрібниці: вогнегасники, щитки, кабелі зі скотчем, сміття
    wd = {k_: Parts("wall_%s_decor" % k_, root) for k_ in "NSEW"}
    for k_, pos, n_ in (("E", (X1, -6.0), (-1, 0, 0)), ("E", (X1, 8.4), (-1, 0, 0)), ("N", (-14.5, Y1), (0, -1, 0)),
                        ("W", (X0, -4.0), (1, 0, 0)), ("S", (10.5, Y0), (0, 1, 0))):
        extinguisher(wd[k_], (pos[0], pos[1], 0), n_)
    wall_panel(wd["N"], (-16.0, Y1, 0), (0, -1, 0))
    wall_panel(wd["E"], (X1, 12.5, 0), (-1, 0, 0), 0.8, 0.5, 2.6)
    for P_ in wd.values():
        P_.finish()
    dt = Parts("details", root)
    cable(dt, [(-1.2, ty), (-hp - 0.3, -hp - 0.3)])
    cable(dt, [(X0 + 5.2, -6.2), (X0 + 3.9, -5.6), (X0 + 3.0, 4.6), (X0 + 3.9, 5.6)])
    cable(dt, [(X0 + 5.2, -6.2), (-6.0, -q - 0.2), (m + 1.0, -m - 0.9)])
    for k in range(3):
        floor_decal(dt, "Decal_Grime", (X1 - 2.0, -9.5 + 3.0 * k), 2.0, k)
    dt.finish()
    lit = Parts("details", root)
    litter(lit, 0, -qt - 3.0, 12, 5, 30, salt=1)
    litter(lit, qt + 3, 0, 4, 10, 20, salt=2)
    lit.finish()

    # --- закулісся за західною стіною (як і раніше)
    BX0, BX1 = X0 - 14.0, X0
    bk = Parts("backstage", root)
    bk.box("Floor_Tile", ((BX0 + BX1) / 2, 0, -0.05), (BX1 - BX0, 24.0, 0.1))
    bkw = Parts("wall_backstage", root)
    bkw.box("Block_Wall", ((BX0 + BX1) / 2, 12.15, 1.5), (BX1 - BX0, 0.3, 3.0))
    bkw.box("Block_Wall", ((BX0 + BX1) / 2, -12.15, 1.5), (BX1 - BX0, 0.3, 3.0))
    bkw.box("Block_Wall", (BX0 - 0.15, 0, 1.5), (0.3, 24.3, 3.0))
    bkw.box("Block_Wall", (X0 - 3.6, 6.5, 1.5), (0.2, 11.0, 3.0))
    bkw.box("Block_Wall", (X0 - 3.6, -7.0, 1.5), (0.2, 10.0, 3.0))
    bkw.finish()
    door(bk, (X0 - 3.6, -1.5, 0), math.pi / 2, mat="Door_Grey")
    lockers(bk, (BX0 + 0.3, 6.0, 0), -math.pi / 2, 14, "Locker_Blue")
    lockers(bk, ((BX0 + X0 - 3.6) / 2, 11.75, 0), math.pi, 18, "Locker_Blue")
    bench(bk, ((BX0 + X0 - 3.6) / 2, 6.5, 0), 0.0, 4.0)
    bench(bk, ((BX0 + X0 - 3.6) / 2, 3.5, 0), 0.0, 4.0)
    mirror_wall(bk, ((BX0 + X0 - 3.6) / 2, -11.75, 0), 0.0, 4, bulbs=False)
    water_cooler(bk, (X0 - 4.1, 10.5, 0))
    folding_table(bk, (BX0 + 1.5, -4.0, 0), math.pi / 2, cloth=None)
    massage_table(bk, (BX0 + 3.5, -8.0, 0), 0.0)
    folding_table(bk, (X0 - 1.6, 2.5, 0), math.pi / 2)
    monitor(bk, (X0 - 1.6, 2.5, 0), math.pi / 2, z=0.76)
    water_cooler(bk, (X0 - 0.6, -4.0, 0))
    for k in range(4):
        road_case(bk, (X0 - 1.2, -9.5 + 1.0 * k, 0), math.pi / 2, (0.9, 0.6, 0.7))
    bk.finish()
    bkc = Parts("ceiling_backstage", root)
    bkc.box("Ceiling", ((BX0 + BX1) / 2, 0, 3.05), (BX1 - BX0, 24.0, 0.1))
    for k in range(5):
        bkc.box("Ceiling_Light", ((BX0 + BX1) / 2, -9.0 + 4.5 * k, 2.95), (1.2, 0.3, 0.08))
    bkc.finish()
    empty("anchor_backstage_gorilla", root, (X0 - 1.2, 1.2, 0), yaw=-90)
    empty("anchor_backstage_lockers", root, ((BX0 + X0 - 3.6) / 2, 9.0, 0))
    empty("anchor_backstage_bench", root, ((BX0 + X0 - 3.6) / 2, 5.0, 0))
    empty("anchor_backstage_mirror", root, ((BX0 + X0 - 3.6) / 2, -10.5, 0))
    empty("anchor_backstage_trainer", root, (BX0 + 3.5, -7.0, 0))
    empty("anchor_backstage_interview", root, (X0 - 1.8, -6.5, 0))
    empty("anchor_backstage_exit", root, (BX0 + 0.6, -10.5, 0))
    gym_dressing(root, r, X0, X1, Y0, Y1, H, m, q, hp)
    cams(root, [("cam_entrance", (-4.0, -3.0, 2.2)), ("cam_crowd_low", (0.0, -4.0, 0.6)),
                ("cam_high_wide", (10.0, -10.0, 7.0)), ("cam_backstage", (X0 - 6.0, 0.0, 2.6))])
    return dict(id="gym", title="Спортзал спорткомплексу (інді)", ring=r, spec=spec, ropes=("White", "White", "White"),
                capacity=len(SEATS), ambient=0.3)

BUILDERS["gym"] = build_gym
