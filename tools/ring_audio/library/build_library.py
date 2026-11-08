# Бібліотека звуку «Рингу»: каталог слотів (що звучить у грі) і аудіо для сторінки схвалення.
# WAV -> MP3 (lameenc), OGG/MP3 — як є. Запуск:
#   python build_library.py <корінь репо> <тека виходу> [тека з пакетом lameenc] [тека рендерів раундів]
# lameenc — службовий пакет лише для сторінки (pip install --target <тека> lameenc), у гру не йде.
# Тека виходу = сторінка: скопіювати туди index.html і опублікувати (index.html + catalog.json + audio/*).
# Раунди нових варіантів (на кожну відмову — 3 нові, рішення власника 08.11.2026): опис — rounds/round_<N>.json поруч
# (id, слот, підпис, що інакше, як відтворити); WAV — у теці рендерів (генератори tools/ring_audio/ring_*.py пишуть <id>.wav).
import glob, io, json, os, shutil, struct, sys, wave

if len(sys.argv) > 3 and sys.argv[3]:
    sys.path.insert(0, sys.argv[3])
import lameenc  # noqa: E402
import numpy as np  # noqa: E402

REPO, OUT = sys.argv[1], sys.argv[2]
RENDERS = sys.argv[4] if len(sys.argv) > 4 else None
HERE = os.path.dirname(os.path.abspath(__file__))
RING = 'Assets/ThirdParty/CC0/Audio/Ring/'
MENU = 'Assets/Art/Ring/Audio/Menu/'
# Нові звуки подій матчу (Gameplay/Ring/Models/RingSoundMap.NewSlots): у гру — лише схвалений варіант, під назвою слота.
EVENTS = 'Assets/Art/Ring/Sound/Resources/RingSound/'
OGA = 'opengameart.org, CC0'
OWN = 'власна генерація (tools/ring_audio), без атрибуції'

# (кліп, файл, назва, де звучить, джерело)
GROUPS = [
    ('music', 'Музика', [
        ('music_rock_theme_loop', RING + 'music_rock_theme_loop.wav', 'Тема зали й вступу', 'Вступ гри, зала між екранами (петля)', 'Umplix · «Rock Theme Song» · ' + OGA),
        ('music_rock_city_ransom', RING + 'music_rock_city_ransom.ogg', 'Вихід героя', 'Екран виходу гравця', 'Ragnar Random · «Rock Music» · ' + OGA),
        ('music_hammer_of_the_berserker', RING + 'music_hammer_of_the_berserker.ogg', 'Вихід Молота', 'Екран виходу Молота (головний суперник)', 'Ragnar Random · «Rock Music» · ' + OGA),
        ('music_downtown_destruction', RING + 'music_downtown_destruction.ogg', 'Вихід борців федерації', 'Екрани виходу інших суперників', 'Ragnar Random · «Rock Music» · ' + OGA),
        ('music_login', MENU + 'music_login.wav', 'Меню: вхід', 'Титул (головне меню), петля 80 с', OWN),
        ('music_loading', MENU + 'music_loading.wav', 'Меню: завантаження', 'Екран завантаження, петля 40 с', OWN),
        ('music_settings', MENU + 'music_settings.wav', 'Меню: налаштування', 'Екран налаштувань, петля 55 с', OWN),
    ]),
    ('hall', 'Зала й ринг', [
        ('crowd_shouting', RING + 'crowd_shouting.ogg', 'Гул натовпу', 'Фон зали, гучність за ажіотажем (петля)', 'starninjas · ' + OGA),
        ('applause_hall', RING + 'applause_hall.wav', 'Вибух залу', 'Великий момент: фінішер, перемога', 'expl0it3r · ' + OGA),
        ('bell_01', RING + 'bell_01.ogg', 'Дзвін: початок', 'Початок матчу', 'rubberduck · «100 CC0 SFX» · ' + OGA),
        ('bell_02', RING + 'bell_02.ogg', 'Дзвін: кінець', 'Кінець матчу (тричі)', 'rubberduck · «100 CC0 SFX» · ' + OGA),
        ('gong_01', RING + 'gong_01.ogg', 'Гонг', 'Вступ і удар екрана виходу', 'rubberduck · «100 CC0 SFX» · ' + OGA),
        ('mat_slap', RING + 'mat_slap.mp3', 'Долоня рефері', 'Відлік утримання: «раз, два, три»', 'qubodup · ' + OGA),
        ('land', RING + 'land.wav', 'Приземлення', 'Стрибок з верхнього каната', 'macro · ' + OGA),
    ]),
    ('impact', 'Удари й падіння', [
        ('hit_01', RING + 'hit_01.ogg', 'Удар 1', 'Удари (передпліччя, ногою, брудний прийом) — по колу', 'rubberduck · «100 CC0 SFX» · ' + OGA),
        ('hit_02', RING + 'hit_02.ogg', 'Удар 2', 'Удари — по колу', 'rubberduck · «100 CC0 SFX» · ' + OGA),
        ('hit_03', RING + 'hit_03.ogg', 'Удар 3', 'Удари — по колу', 'rubberduck · «100 CC0 SFX» · ' + OGA),
        ('hit_04', RING + 'hit_04.ogg', 'Удар 4', 'Удари — по колу', 'rubberduck · «100 CC0 SFX» · ' + OGA),
        ('slam_01', RING + 'slam_01.ogg', 'Падіння 1', 'Кидки й падіння на мат — по колу', 'rubberduck · «100 CC0 SFX» · ' + OGA),
        ('slam_02', RING + 'slam_02.ogg', 'Падіння 2', 'Кидки й падіння на мат — по колу', 'rubberduck · «100 CC0 SFX» · ' + OGA),
        ('slam_03', RING + 'slam_03.ogg', 'Падіння 3', 'Кидки й падіння на мат — по колу', 'rubberduck · «100 CC0 SFX» · ' + OGA),
        ('slam_04', RING + 'slam_04.ogg', 'Падіння 4', 'Кидки й падіння на мат — по колу', 'rubberduck · «100 CC0 SFX» · ' + OGA),
        ('slam_05', RING + 'slam_05.ogg', 'Падіння 5', 'Кидки й падіння на мат — по колу', 'rubberduck · «100 CC0 SFX» · ' + OGA),
    ]),
    ('ui', 'Звуки меню', [
        ('ui_move', MENU + 'ui_move.wav', 'Курсор', 'Перехід між пунктами меню', OWN),
        ('ui_confirm', MENU + 'ui_confirm.wav', 'Підтвердити', 'Вибір пункту', OWN),
        ('ui_back', MENU + 'ui_back.wav', 'Назад', 'Вихід з підекрана, закриття паузи', OWN),
        ('ui_error', MENU + 'ui_error.wav', 'Помилка', 'Зіпсований сейв', OWN),
        ('ui_toggle', MENU + 'ui_toggle.wav', 'Перемикач', 'Так/ні в налаштуваннях', OWN),
        ('ui_slider', MENU + 'ui_slider.wav', 'Крок повзунка', 'Гучність у налаштуваннях', OWN),
        ('ui_tab', MENU + 'ui_tab.wav', 'Вкладка', 'Перемикання вкладок налаштувань', OWN),
        ('ui_transition', MENU + 'ui_transition.wav', 'Розріз', 'Перехід між екранами', OWN),
        ('ui_load_done', MENU + 'ui_load_done.wav', 'Завантажено', 'Шкала завантаження дійшла до 100 %', OWN),
        ('ui_login_ok', MENU + 'ui_login_ok.wav', 'Вхід', 'Успішне «Продовжити» на титулі', OWN),
    ]),
    # Таблиця «подія → звук» (docs/ring/audio/EVENT_SOUND.md, критерій М1): події, що досі мовчали. Поточного файлу немає.
    ('events', 'Нові звуки подій матчу', [
        ("lockup", EVENTS + 'lockup.wav', 'Обмін: зчепились', 'Обмін на старті й після реверсу', OWN),
        ("pin_cover", EVENTS + 'pin_cover.wav', 'Утримання: накриття', 'Борець накриває суперника на утримання', OWN),
        ("hold_strain", EVENTS + 'hold_strain.wav', 'Больовий', 'Больовий прийом і витримка больового', OWN),
        ("rope_rebound", EVENTS + 'rope_rebound.wav', 'Канати', 'Кидок на канати, відскок', OWN),
        ("corner_hit", EVENTS + 'corner_hit.wav', 'Кут', 'Кидок у кут', OWN),
        ("floor_thud", EVENTS + 'floor_thud.wav', 'За рингом', 'Вихід за ринг: пара падає на підлогу', OWN),
        ("canvas_shuffle", EVENTS + 'canvas_shuffle.wav', 'Підвівся', 'Підвестися, відновитися, перевести подих, закотитися в ринг', OWN),
        ("qte_prompt", EVENTS + 'qte_prompt.wav', 'QTE: з’явилось', 'Під час прийому з’являється QTE', OWN),
        ("qte_hit", EVENTS + 'qte_hit.wav', 'QTE: вдало', 'Вдале QTE — ефектніший показ, +1 ажіотажу', OWN),
        ("qte_miss", EVENTS + 'qte_miss.wav', 'QTE: пропущено', 'QTE пропущено', OWN),
        ("whiff", EVENTS + 'whiff.wav', 'Промах', 'Удар чи брудний прийом не влучив: суперник ухилився', OWN),
        ("barrier_hit", EVENTS + 'barrier_hit.wav', 'За рингом: удар у бар’єр', 'Удар за рингом відкидає суперника на сталевий бар’єр', OWN),
        ("barrier_slam", EVENTS + 'barrier_slam.wav', 'За рингом: кидок у бар’єр', 'Кидок за рингом у бар’єр; промах предметом — сам у бар’єр', OWN),
        ("weapon_chair", EVENTS + 'weapon_chair.wav', 'Стілець', 'Предмет за рингом, 7–9: удар складаним стільцем', OWN),
        ("weapon_table", EVENTS + 'weapon_table.wav', 'Стіл', 'Предмет за рингом, 10+: кидок на стіл, стіл ламається', OWN),
        ("count_out", EVENTS + 'count_out.wav', 'Відлік за рингом', 'Рефері рахує, поки пара за рингом: звук на кожну цифру', OWN),
        ("crowd_surge", EVENTS + 'crowd_surge.wav', 'Зал здіймається', 'Склейка: пара вилетіла за ринг чи повернулась', OWN),
    ]),
]


def wav_to_mp3(src, dst):
    with wave.open(src, 'rb') as w:
        ch, sw, sr, n = w.getnchannels(), w.getsampwidth(), w.getframerate(), w.getnframes()
        raw = w.readframes(n)
    if sw == 2:
        pcm = np.frombuffer(raw, dtype='<i2').reshape(-1, ch)
    elif sw == 3:
        b = np.frombuffer(raw, dtype=np.uint8).reshape(-1, 3)
        v = (b[:, 0].astype(np.int32) | (b[:, 1].astype(np.int32) << 8) | (b[:, 2].astype(np.int32) << 16))
        v = np.where(v & 0x800000, v - 0x1000000, v)
        pcm = (v >> 8).astype(np.int16).reshape(-1, ch)
    elif sw == 1:
        pcm = ((np.frombuffer(raw, dtype=np.uint8).astype(np.int16) - 128) << 8).reshape(-1, ch)
    else:
        raise ValueError('ширина семпла %d' % sw)
    if ch > 2:
        pcm = pcm[:, :2]
        ch = 2
    enc = lameenc.Encoder()
    enc.set_bit_rate(192 if n / sr > 5 else 160)
    enc.set_in_sample_rate(sr)
    enc.set_channels(ch)
    enc.set_quality(2)
    data = enc.encode(np.ascontiguousarray(pcm).tobytes()) + enc.flush()
    with open(dst, 'wb') as f:
        f.write(data)
    return n / sr


def ogg_duration(path):
    b = open(path, 'rb').read()
    i = b.find(b'\x01vorbis')
    rate = struct.unpack('<I', b[i + 12:i + 16])[0] if i >= 0 else 44100
    j = b.rfind(b'OggS')
    gran = struct.unpack('<q', b[j + 6:j + 14])[0]
    return gran / float(rate)


def rounds():
    """Варіанти раундів 1+ за слотами: {слот: [опис, …]} з rounds/round_<N>.json."""
    by = {}
    for path in sorted(glob.glob(os.path.join(HERE, 'rounds', 'round_*.json'))):
        r = json.load(io.open(path, encoding='utf-8'))
        for c in r['candidates']:
            c = dict(c, round=r['round'])
            by.setdefault(c['slot'], []).append(c)
    return by


def main():
    audio = os.path.join(OUT, 'audio')
    os.makedirs(audio, exist_ok=True)
    extra = rounds()
    groups, total = [], 0
    for gid, gname, items in GROUPS:
        slots = []
        for clip, rel, title, where, source in items:
            src = os.path.join(REPO, rel)
            ext = os.path.splitext(rel)[1].lower()
            cid = clip + '.v0'
            cands = []
            if os.path.exists(src):
                if ext == '.wav':
                    out = cid + '.mp3'
                    dur = wav_to_mp3(src, os.path.join(audio, out))
                else:
                    out = cid + ext
                    shutil.copyfile(src, os.path.join(audio, out))
                    dur = ogg_duration(src) if ext == '.ogg' else None
                total += os.path.getsize(os.path.join(audio, out))
                cands.append({'id': cid, 'label': 'Поточний у грі', 'round': 0, 'audio': 'audio/' + out,
                              'seconds': round(dur, 2) if dur else None, 'source': source})
            for c in extra.get(clip, []):
                if not RENDERS:
                    raise SystemExit('є раунди, але не дано теки рендерів (4-й аргумент)')
                out = c['id'] + '.mp3'
                dur = wav_to_mp3(os.path.join(RENDERS, c['wav']), os.path.join(audio, out))
                total += os.path.getsize(os.path.join(audio, out))
                cands.append({'id': c['id'], 'label': c['label'], 'round': c['round'], 'audio': 'audio/' + out,
                              'seconds': round(dur, 2), 'source': c['source'], 'design': c.get('design', '')})
            slots.append({'clip': clip, 'title': title, 'where': where, 'file': rel, 'candidates': cands})
        groups.append({'id': gid, 'title': gname, 'slots': slots})
    known = {s['clip'] for g in groups for s in g['slots']}
    stray = sorted(set(extra) - known)
    if stray:
        raise SystemExit('варіанти для невідомих слотів: ' + ', '.join(stray))
    cat = {'version': 2, 'updated': '2026-11-08', 'groups': groups}
    with io.open(os.path.join(OUT, 'catalog.json'), 'w', encoding='utf-8') as f:
        json.dump(cat, f, ensure_ascii=False, indent=1)
    print('слотів', sum(len(g['slots']) for g in groups), 'аудіо', round(total / 1e6, 1), 'МБ')


main()
