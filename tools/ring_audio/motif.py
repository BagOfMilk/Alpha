"""Лейтмотив і гармонія меню «Рингу» як дані. Власний оригінальний матеріал.

Тональність — фа мінор (натуральний: F G Ab Bb C Db Eb; Mi-бекар дозволено над C7#9).
Розмір 4/4, 16 кроків (шістнадцятих) на такт. Номери тактів тут 0-based
(у документації — 1-based).
"""

KEY_NAME = "фа мінор (F minor)"
SCALE_PCS = (5, 7, 8, 10, 0, 1, 3)  # F G Ab Bb C Db Eb

NAMES = ["C", "Db", "D", "Eb", "E", "F", "Gb", "G", "Ab", "A", "Bb", "B"]


def name(m):
    return f"{NAMES[m % 12]}{m // 12 - 1}"


# Акорди (MIDI, C4 = 60)
CHORDS = {
    "Fm9": [53, 56, 60, 63, 67],        # F3 Ab3 C4 Eb4 G4
    "Dbmaj9": [49, 53, 56, 60, 63],     # Db3 F3 Ab3 C4 Eb4
    "Bbm9": [46, 49, 53, 56, 60],       # Bb2 Db3 F3 Ab3 C4
    "Eb13": [51, 55, 61, 65, 72],       # Eb3 G3 Db4 F4 C5
    "C7#9": [48, 52, 58, 63],           # C3 E3 Bb3 Eb4(D#4)
}
ROOTS = {"Fm9": 41, "Dbmaj9": 37, "Bbm9": 34, "Eb13": 39, "C7#9": 36}  # басові корені
THIRDS = {"Fm9": 3, "Dbmaj9": 4, "Bbm9": 3, "Eb13": 4, "C7#9": 4}


def chord_at(bar, step=0):
    """Акорд на 4-тактовому колі (login/loading): Fm9 | Dbmaj9 | Bbm9 + Eb13 | C7#9."""
    k = bar % 4
    if k == 0:
        return "Fm9"
    if k == 1:
        return "Dbmaj9"
    if k == 2:
        return "Bbm9" if step < 8 else "Eb13"
    return "C7#9"


def chord_at_slow(bar, step=0):
    """Той самий цикл, розтягнутий удвічі (settings): акорд на 2 такти."""
    k = (bar // 2) % 4
    if k == 0:
        return "Fm9"
    if k == 1:
        return "Dbmaj9"
    if k == 2:
        return "Bbm9" if bar % 2 == 0 else "Eb13"
    return "C7#9"


# Лейтмотив: 8 тактів, записи (крок, MIDI, довжина у шістнадцятих)
F5, C5, Eb5, Ab5, G5, Db5, Bb5, E5, C6, Db6, Eb6 = 77, 72, 75, 80, 79, 73, 82, 76, 84, 85, 87
MOTIF = [
    [(0, 72, 3), (3, 75, 1), (4, 77, 2), (8, 80, 2), (10, 79, 1), (11, 77, 3), (14, 75, 2)],  # такт 1
    [(0, 77, 6), (8, 72, 2), (10, 75, 2), (12, 73, 4)],                                      # такт 2
    [(0, 75, 3), (3, 77, 1), (4, 80, 2), (8, 82, 2), (10, 80, 1), (11, 79, 3), (14, 77, 2)],  # такт 3
    [(0, 82, 2), (2, 80, 2), (4, 79, 4), (8, 76, 2), (10, 77, 6)],                           # такт 4
    [(8, 84, 2), (10, 82, 1), (11, 80, 1), (12, 77, 4)],                                      # такт 5 (відповідь)
    [(0, 80, 3), (3, 82, 1), (4, 84, 4), (8, 87, 2), (10, 85, 2), (12, 84, 4)],               # такт 6
    [(0, 82, 2), (2, 80, 2), (4, 77, 4), (8, 79, 2), (10, 80, 2), (12, 82, 4)],               # такт 7
    [(0, 84, 6), (8, 82, 2), (10, 80, 2), (12, 79, 2), (14, 76, 2)],                          # такт 8 (розв'язка у такт 1)
]
MOTIF_STABS = {4: (2, 6)}  # такт 5: акордові удари духових на кроках 2 і 6


def motif_notes(bars=range(8)):
    for b in bars:
        for step, midi, ln in MOTIF[b]:
            yield b, step, midi, ln


# діатонічна гармонізація
_SCALE = [m for m in range(24, 110) if m % 12 in SCALE_PCS]


def below(n, steps):
    """Діатонічна нота на `steps` ступенів нижче (хроматичний зсув зберігається)."""
    idx = max(i for i, s in enumerate(_SCALE) if s <= n)
    return _SCALE[idx - steps] + (n - _SCALE[idx])


def harmonise(n, voicing=(0, 2, 5)):
    """Мелодія + терція вниз + секста вниз (за замовчуванням)."""
    return tuple(sorted(below(n, s) if s else n for s in voicing))


def fold(notes, lo, hi):
    """Згорнути ноти акорду у діапазон [lo, hi], прибрати дублі висот, відсортувати."""
    out = set()
    for n in notes:
        m = n
        while m < lo:
            m += 12
        while m > hi:
            m -= 12
        out.add(m)
    return tuple(sorted(out))


def notation(bar):
    """Текстовий запис такту лейтмотиву: ім'я нота@крок(довжина)."""
    parts = [f"{name(m)}@{s}({l})" for s, m, l in MOTIF[bar]]
    if bar in MOTIF_STABS:
        parts.insert(0, "акордові удари@" + "+".join(str(s) for s in MOTIF_STABS[bar]))
    return "  ".join(parts)
