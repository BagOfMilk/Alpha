# Граф станів, кросфейди, маски шарів, ролі кліпів: дані для сторінки (graph.json) і для Animator у задачі 3 (CAST_ANIM.md поруч із моделями).
#   python tools/blender/ring_cast/cast_graph.py <тека кліпів> <тека даних сторінки> <шлях до CAST_ANIM.md>
# Кросфейди не задаються наосліп: тривалість рахується з різниці поз кінця одного кліпу й початку другого (висота таза, кисті, кути).
import json, os, sys
import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from clips_lib import *
import cast_spec as CS

CLIPS, DATA, DOC = sys.argv[1:4]
PFX = CS.PREFIX

UPPER = ["Spine", "Spine1", "Spine2", "Neck", "Head"]
for s in ("Left", "Right"):
    UPPER += [s + "Shoulder", s + "Arm", s + "ForeArm", s + "Hand"]
    for f in ("Thumb", "Index", "Middle", "Ring", "Pinky"):
        UPPER += ["%sHand%s%d" % (s, f, i) for i in (1, 2, 3)]
MASKS = {
    "full": [PFX + n for n in CS.MIXAMO_PARENT],
    "upper_body": [PFX + n for n in UPPER],
    "right_arm": [PFX + n for n in UPPER if n.startswith("Right")],
    "left_arm": [PFX + n for n in UPPER if n.startswith("Left")],
    "arms": [PFX + n for n in UPPER if n.startswith(("Right", "Left"))],
    "lower_body": [PFX + n for n in CS.MIXAMO_PARENT if n not in UPPER],
}

# Стани: стан -> циклічний кліп (для обох статей ті самі імена)
STATES = {
    "idle": dict(clip="U_Idle_Loop", label="Стоїть вільно"),
    "walk": dict(clip="U_Walk_Loop", label="Ходьба"),
    "jog": dict(clip="Run_Jog_Loop", label="Підтюпцем"),
    "run": dict(clip="Run_Loop", label="Біг по рингу"),
    "stance": dict(clip="Ring_Stance_Loop", label="Стійка захисту"),
    "stance_walk": dict(clip="Move_Walk_Fwd_Loop", label="Хода в стійці"),
    "crouch": dict(clip="U_Crouch_Idle_Loop", label="Присід"),
    "kneel": dict(clip="Kneel_Idle_Loop", label="На колінах"),
    "sit_mat": dict(clip="Sit_Mat_Idle_Loop", label="Сидить на маті"),
    "lying_back": dict(clip="Lying_Back_Loop", label="Лежить на спині"),
    "lying_front": dict(clip="Lying_Front_Loop", label="Лежить на животі"),
    "corner_lean": dict(clip="Move_Corner_Lean_Loop", label="Спирається в куті"),
    "top_rope": dict(clip="Top_Rope_Perch_Loop", label="На верхньому канаті"),
    "promo": dict(clip="promo_idle", label="Промо з мікрофоном"),
    "entrance_walk": dict(clip="Entrance_Walk_Loop", label="Вихід: хода"),
    "held_stand": dict(clip="Held_Stand_Loop", label="У захваті стоячи"),
    "held_sit": dict(clip="Held_Sit_Loop", label="У захваті сидячи"),
    "held_kneel": dict(clip="Held_Kneel_Loop", label="У захваті на колінах"),
}

# Ребра-переходи: (звідки, куди, кліп-перехід або None). Кросфейд кожного стику рахується нижче.
EDGES = [
    ("idle", "walk", None), ("walk", "idle", None), ("walk", "jog", None), ("jog", "walk", None), ("jog", "run", None), ("run", "jog", None),
    ("idle", "stance", "Tr_Idle_To_Stance"), ("stance", "idle", "Tr_Stance_To_Idle"),
    ("stance", "stance_walk", None), ("stance_walk", "stance", None),
    ("stance", "crouch", "Tr_Stance_To_Crouch"), ("crouch", "stance", "Tr_Crouch_To_Stance"),
    ("crouch", "kneel", "Tr_Crouch_To_Kneel"), ("kneel", "crouch", "Tr_Kneel_To_Crouch"),
    ("stance", "kneel", "Tr_Stance_To_Kneel"), ("kneel", "stance", "Tr_Kneel_To_Stance"),
    ("kneel", "sit_mat", "Tr_Kneel_To_Sit"), ("sit_mat", "kneel", "Tr_Sit_To_Kneel"),
    ("stance", "run", "Run_Start"), ("run", "stance", "Run_Stop"),
    ("stance", "lying_back", "Bump_Back"), ("lying_back", "stance", "GetUp_Back"),
    ("stance", "lying_front", "Bump_Front"), ("lying_front", "lying_back", "Roll_Front_To_Back"),
    ("stance", "corner_lean", "Move_Corner_Enter"), ("corner_lean", "stance", "Move_Corner_Exit"),
    ("stance", "top_rope", "Corner_Climb_Top"), ("top_rope", "lying_front", "Top_Dive_Crossbody"),
    ("idle", "promo", "Tr_Idle_To_Promo"), ("promo", "idle", "Tr_Promo_To_Idle"),
    ("idle", "entrance_walk", "Entrance_Curtain_Face_Point"), ("entrance_walk", "stance", "Entrance_Through_Ropes"),
    ("stance", "held_stand", "Tr_To_Held_stand"), ("held_stand", "stance", "Tr_From_Held_stand"), ("kneel", "held_kneel", "Tr_To_Held_kneel"), ("held_kneel", "kneel", "Tr_From_Held_kneel"),
    ("sit_mat", "held_sit", "Tr_To_Held_sit"), ("held_sit", "sit_mat", "Tr_From_Held_sit"),
]

# Послідовність бою для режиму переходів (ланцюг із брифу). me/partner — кліпи; move — чи рухається борець у стані (м/с із кліпу).
BOUT = [
    dict(id="idle", label="Idle", me="U_Idle_Loop", partner="Ring_Stance_Loop", hold=1.2),
    dict(id="walk", label="Ходьба", me="U_Walk_Loop", partner="Ring_Stance_Loop", hold=1.6, approach=True),
    dict(id="to_stance", label="У стійку", me="Tr_Idle_To_Stance", partner="Ring_Stance_Loop"),
    dict(id="stance", label="Стійка", me="Ring_Stance_Loop", partner="Ring_Stance_Loop", hold=0.8),
    dict(id="strike", label="Удар", pair="Forearm_Strike", me_role="A"),
    dict(id="hit_back", label="Удар у відповідь", pair="Forearm_Strike", me_role="B"),
    dict(id="bump", label="Бамп", me="Bump_Back", partner="Ring_Stance_Loop"),
    dict(id="down", label="Лежить", me="Lying_Back_Loop", partner="Ring_Stance_Loop", hold=0.8),
    dict(id="getup", label="Встати", me="GetUp_Back", partner="Ring_Stance_Loop"),
    dict(id="reversal", label="Реверс", pair="Chain_Wristlock_Spin", me_role="A"),
    dict(id="whip", label="Кидок у канат", pair="Whip_Ropes_Left", me_role="B"),   # повз ліве плече: бігун повертається на лінію, з якої суперникові до ухилу ~0,2 м (сторінка рахує обидва боки й бере кращий)
    dict(id="rebound", label="Від канатів", me="Rope_Back_L", partner="Ring_Stance_Loop"),
    dict(id="dodge", label="Ухил", pair="Center_Duck", me_role="A"),
    dict(id="rebound2", label="Знову від канатів", me="Rope_Back_R", partner="Ring_Stance_Loop"),   # звичайний відскок: Rope_Duck_Return — коли пригинається сам бігун
    # після ухилу суперник ліворуч від бігуна; на удержання — лише удари, що кладуть на спину з того боку (клоузлайн праворуч — 1,5 м убік)
    dict(id="hit", label="Удар плечем", choice=["Center_ShoulderBlock", "Center_ShoulderBlock_Heavy"], me_role="A"),
    dict(id="pin", label="Удержання", pair="Cover", me_role="A"),
]

# Приклади міксів: верх поверх низу
MIX_EXAMPLES = [
    dict(label="Б'є на ходу", base="U_Walk_Loop", upper="U_Punch_Cross", who=None),
    dict(label="Удар передпліччям на ходу в стійці", base="Move_Walk_Fwd_Loop", upper="Forearm_Strike_A", who=None),
    dict(label="Менеджерка бризкає на місці", base="U_Idle_Loop", upper="spray_water", who="lina"),
    dict(label="Менеджерка бризкає на ходу", base="U_Walk_Loop", upper="spray_water", who="lina"),
    dict(label="Реакція на удар поверх ходьби", base="U_Walk_Loop", additive="U_Hit_Chest", who=None),
    dict(label="Реакція на удар поверх стійки", base="Ring_Stance_Loop", additive="U_Hit_Head", who=None),
    dict(label="Захват поверх стійки", base="Ring_Stance_Loop", upper="Layer_Held_Arms_Loop", who="komet"),
    dict(label="Ходьба з поясом", base="U_Walk_Loop", upper="belt_hold", who="stalevyi"),
    dict(label="Ходьба зі стільцем", base="U_Walk_Loop", upper="chair_idle_Loop", who="dzhura"),
    dict(label="Промо на ходу", base="U_Walk_Loop", upper="promo_idle", who="komet"),
]

# Кліпи за роллю: борець — усе про бій; менеджерка — лише побутове. Судді немає серед вісьмох, його кліпи лишаються в бібліотеці.
MANAGER_KEEP = ("U_Idle_Loop", "U_Walk_Loop", "U_Walk_Formal_Loop", "U_Idle_Talking_Loop", "U_Idle_FoldArms_Loop", "U_Idle_TalkingPhone_Loop", "U_Idle_No_Loop", "U_Yes",
                "U_Jog_Fwd_Loop", "U_Hit_Chest", "U_Hit_Head", "U_Hit_Knockback", "U_Crouch_Idle_Loop", "U_Sitting_Enter", "U_Sitting_Idle_Loop", "U_Sitting_Exit", "U_Sitting_Talking_Loop",
                "U_Interact", "U_PickUp_Table", "U_Fixing_Kneeling", "spray_idle_Loop", "spray_water", "Bump_Back", "GetUp_Back", "Lying_Back_Loop", "promo_idle", "Promo_Mic_Point", "Promo_Mic_Stare_Loop",
                "Tr_Idle_To_Stance", "Tr_Stance_To_Idle", "Ring_Stance_Loop", "Tr_Idle_To_Promo", "Tr_Promo_To_Idle", "Tr_To_Held_stand", "Tr_From_Held_stand", "Tr_To_Held_kneel", "Tr_From_Held_kneel", "Tr_To_Held_sit", "Tr_From_Held_sit", "Tr_Stance_To_Kneel", "Tr_Kneel_To_Stance", "Kneel_Idle_Loop", "Tr_Kneel_To_Sit", "Tr_Sit_To_Kneel", "Sit_Mat_Idle_Loop", "Held_Stand_Loop", "Held_Sit_Loop", "Held_Kneel_Loop", "Layer_Held_Arms_Loop",
                "Entrance_Walk_Loop", "Entrance_Walk_Focused_Loop", "Entrance_Climb_Steps", "Entrance_Through_Ropes")
EXCLUDE_WRESTLER_PREFIX = ("Ref_", "spray_")
PROP_CLIP_OWNER = {"belt": "stalevyi", "chair": "dzhura", "case": "baron", "kendo": "baron", "mic": "komet", "spray": "lina"}

# Чого бракує: позначається на сторінці. Прямо за брифом і за таблицею дослідження анімацій.
MISSING = [
    dict(name="Рагдол після фінішера", note="За брифом падіння після фінішера робить рагдол рушія, кліпа не буде. На сторінці — Bump_Back + Lying_Back_Loop як заміна."),
    dict(name="Фінішери за ґіміком", note="Є лише спільні удари (Center_Lariat_Heavy тощо). Підписні фінішери не зроблені сесією анімацій."),
    dict(name="Больовий із реальною підсадкою", note="Є Chain_Headlock* і власні пози захвату. Парних больових із здачею немає."),
    dict(name="Кидки й класика (суплекс, пауербомба, DDT, армдраг)", note="У плані сесії анімацій (ітерація 2, черга 3), кліпів ще немає. Є лише Bodyslam."),
    dict(name="Робота в куті (удари, удар із розбігу, підсадка)", note="У плані сесії анімацій (черга 2). Зараз є Whip_Corner_*, Move_Corner_* і чорновий Corner_Strike."),
    dict(name="Стрибки від канатів і другого каната", note="У плані сесії анімацій (черга 4). Зараз — чорнові Top_Dive_Crossbody і Top_Dive_Elbow."),
    dict(name="Парний удар стільцем / палицею / кейсом по суперникові", note="Є одинокі chair_swing, kendo_swing, case_swing; реакція — звичайний бамп."),
]


def load(sex):
    d = np.load(os.path.join(CLIPS, "clips_%s.npz" % sex))
    meta = json.load(open(os.path.join(CLIPS, "clips_%s.json" % sex), encoding="utf-8"))
    names = [str(x) for x in d["kit_names"]]
    sk = Skeleton(names, d["parents"], d["rest"])
    return d, meta, sk


def pose_metric(sk, a_q, a_h, b_q, b_h):
    """Різниця двох поз: зсув таза по висоті, найбільший зсув кистей відносно таза, найбільший кут ключових суглобів."""
    wa_q, wa_p = sk.fk(a_q[None], a_h[None])
    wb_q, wb_p = sk.fk(b_q[None], b_h[None])
    dz = abs(float(a_h[2] - b_h[2]))
    hand = 0.0
    for n in ("hand_l", "hand_r", "foot_l", "foot_r"):
        j = sk.i(n)
        da = wa_p[0, j] - wa_p[0, 0]
        db = wb_p[0, j] - wb_p[0, 0]
        if n.startswith("hand"):
            hand = max(hand, float(np.linalg.norm(da - db)))
    ang = 0.0
    for n in ("pelvis", "spine_03", "head", "upperarm_l", "upperarm_r", "lowerarm_l", "lowerarm_r", "thigh_l", "thigh_r", "calf_l", "calf_r"):
        j = sk.i(n)
        d = abs(float(np.sum(wa_q[0, j] * wb_q[0, j])))
        ang = max(ang, float(np.degrees(2 * np.arccos(min(d, 1.0)))))
    return dz, hand, ang


def crossfade(dz, hand, ang):
    """Тривалість кросфейду за різницею поз: мало різниться — коротко; багато — довше, але не довше 0,45 с."""
    return float(np.clip(0.07 + dz * 0.8 + hand * 0.18 + ang * 0.0016, 0.07, 0.45))


def build():
    out = dict(masks=MASKS, states=STATES, bout=BOUT, mix_examples=MIX_EXAMPLES, missing=MISSING, edges={}, ring=dict(size=6.096, cell=1.5, note="Ринг 6×6 м — ігровий ring_canvas_20 (20 футів): venue_arena.fbx. Сторінка бере ринг із venue_*.fbx (ring_web.py)."))
    doc_edges = {}
    for sex in ("m", "f"):
        d, meta, sk = load(sex)
        def end_of(c):
            return d["q/" + c][-1].astype(np.float64), d["h/" + c][-1].astype(np.float64)
        def start_of(c):
            return d["q/" + c][0].astype(np.float64), d["h/" + c][0].astype(np.float64)
        edges = []
        for a, b, via in EDGES:
            ca, cb = STATES[a]["clip"], STATES[b]["clip"]
            if via and via not in meta:
                # кліпа немає: перехід прямо через GetUp (лежання на животі -> спина вже є в Sell-кліпах)
                via = None
            if via:
                q1, h1 = end_of(ca); q2, h2 = start_of(via)
                m1 = pose_metric(sk, q1, h1, q2, h2)
                q3, h3 = end_of(via); q4, h4 = start_of(cb)
                m2 = pose_metric(sk, q3, h3, q4, h4)
                edges.append(dict(**{"from": a, "to": b, "via": via, "fade_in": round(crossfade(*m1), 3), "fade_out": round(crossfade(*m2), 3), "metric_in": [round(x, 3) for x in m1], "metric_out": [round(x, 3) for x in m2]}))
            else:
                q1, h1 = end_of(ca); q2, h2 = start_of(cb)
                m1 = pose_metric(sk, q1, h1, q2, h2)
                edges.append(dict(**{"from": a, "to": b, "via": None, "fade": round(crossfade(*m1), 3), "metric": [round(x, 3) for x in m1], "warn": bool(m1[0] > 0.25 or m1[1] > 0.7)}))
        out["edges"][sex] = edges
    json.dump(out, open(os.path.join(DATA, "graph.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=0)
    return out


def role_clips(meta, spec):
    if spec["role"] == "manager":
        return [c for c in meta if c in MANAGER_KEEP]
    res = []
    for c, m in meta.items():
        if c.startswith(EXCLUDE_WRESTLER_PREFIX):
            continue
        prop = m.get("prop")
        if prop and prop != "mic" and prop not in spec["props"]:
            continue
        res.append(c)
    return res


def write_doc(out):
    d, meta, sk = load("m")
    L = []
    L.append("# Анімації персонажів «Рингу»: граф станів, кросфейди, шари, цикли\n")
    L.append("Згенеровано `tools/blender/ring_cast/cast_graph.py` з даних кліпів; руками не правити. За цим файлом збирається Animator у задачі 3.\n")
    L.append("## Скелет і кліпи\n")
    L.append("- Кістки: імена й ієрархія `mixamorig:` (Y Bot), корінь — `mixamorig:Hips`, без кінцевих `*_End` і `*4`. 52 кістки.")
    L.append("- Поза зв'язування — A-поза, бо вона менше ламає плечі; у Unity Avatar імпортується як Humanoid.")
    L.append("- Два набори кліпів: `RING_Cast_M.fbx` і `RING_Cast_F.fbx` (та сама ієрархія, жіноча манера рухів). 30 кадрів/с.")
    L.append("- Таз у кліпах стоїть на місці: зміщення знято в окрему криву (`root_motion`). Код рухає персонажа за цією кривою, після кліпу він стоїть там, де її кінець.\n")
    L.append("## Склад\n")
    L.append("| Літера | Персонаж | Стать | Зріст, м | Архетип | Реквізит |")
    L.append("|---|---|---|---|---|---|")
    for cid in CS.ORDER:
        s = CS.CAST[cid]
        L.append("| %s | %s | %s | %.2f | %s | %s |" % (s["letter"] or "—", s["name"], "ч" if s["sex"] == "m" else "ж", s["height"], s["archetype"], ", ".join(s["props"]) or "—"))
    L.append("\nЗріст — ПЛЕЙСХОЛДЕР: у `docs/ring/gdd.md` чисел немає. Босс 2,40 м: пропорції ті самі, об'єм більший; листа розворотів у репозиторії немає.\n")
    L.append("## Стани\n")
    L.append("| Стан | Циклічний кліп | Опис |")
    L.append("|---|---|---|")
    for k, v in STATES.items():
        L.append("| `%s` | `%s` | %s |" % (k, v["clip"], v["label"]))
    L.append("\n## Переходи (чоловічий набір; жіночий — у `graph.json`)\n")
    L.append("Кросфейд, с: рахується з різниці поз на стику. Де висота таза розходиться більш ніж на 0,25 м або кисті більш ніж на 0,7 м, стоїть клип-перехід, а не довгий кросфейд.\n")
    L.append("| Звідки | Куди | Клип-перехід | Кросфейд у нього, с | Кросфейд із нього, с |")
    L.append("|---|---|---|---|---|")
    for e in out["edges"]["m"]:
        if e.get("via"):
            L.append("| `%s` | `%s` | `%s` | %.2f | %.2f |" % (e["from"], e["to"], e["via"], e["fade_in"], e["fade_out"]))
        else:
            L.append("| `%s` | `%s` | — | %.2f | — |" % (e["from"], e["to"], e["fade"]))
    L.append("\n## Дії зі стійки\n")
    L.append("Кліп має вузли початку й кінця. Парні кліпи `_A` і `_B` стартують разом; зсув і поворот B відносно A — у `clips_x.json` (`offset`, `rot`).\n")
    acts = [(c, m) for c, m in meta.items() if m.get("node_from") == "stance" and not m["loop"] and not c.startswith("Tr_")]
    L.append("| Кліп | До вузла | Тривалість, с | Рух, м | Контакт, с |")
    L.append("|---|---|---|---|---|")
    for c, m in sorted(acts):
        ev = m.get("events") or []
        ct = next((e["t"] for e in ev if e["event"] == "contact"), None)
        L.append("| `%s` | %s | %.2f | %.2f | %s |" % (c, m.get("node_to") or "—", m["duration"], float(np.hypot(*m["net"][:2])), ("%.2f" % ct) if ct is not None else "—"))
    L.append("\n## Маски шарів\n")
    for k, v in MASKS.items():
        L.append("- `%s`: %d кісток%s" % (k, len(v), "" if k not in ("upper_body",) else " (від `Spine` угору; таз і ноги лишаються базовому шару)"))
    L.append("\nШари: базовий — ноги й таз (ходьба, стійка, idle); верхній — дія руками за маскою `upper_body` або `right_arm`; реакції — адитивні (`AdditiveAnimationBlendMode` у three.js, Additive layer в Animator; опорний кадр — нульовий). Поза в захваті — шар `upper_body` (`Layer_Held_Arms_Loop`).\n")
    L.append("## Цикли\n")
    L.append("Перший кадр дорівнює останньому; кадр 0 локомоції — приземлення лівої стопи, тому при бленді ноги не плутаються. Швидкість — у м/с, код рухає персонажа саме так, тоді стопи не ковзають.\n")
    L.append("| Кліп | Тривалість, с | Швидкість, м/с | Фаза правої стопи |")
    L.append("|---|---|---|---|")
    for c, m in sorted(meta.items()):
        if m["loop"]:
            ph = (m.get("phase") or {}).get("phase_right")
            L.append("| `%s` | %.2f | %.2f | %s |" % (c, m["duration"], m["speed"], "—" if ph is None else "%.2f" % ph))
    L.append("\n## Кліпи зі зміщенням\n")
    L.append("| Кліп | Зсув x, м | Зсув y (−y уперед), м | Поворот, рад |")
    L.append("|---|---|---|---|")
    for c, m in sorted(meta.items()):
        if not m["loop"] and (np.hypot(*m["net"][:2]) > 0.02 or abs(m["net"][2]) > 0.05):
            L.append("| `%s` | %.2f | %.2f | %.2f |" % (c, m["net"][0], m["net"][1], m["net"][2]))
    L.append("\n## Ще не готове\n")
    for x in MISSING:
        L.append("- **%s.** %s" % (x["name"], x["note"]))
    L.append("\n## Стопи й сходинки\n")
    L.append("Foot IK у Unity: Humanoid + `Foot IK` у стані; для сходинок і нерівностей — `OnAnimatorIK` із променем униз під кожну стопу, таз опускається на різницю. На сторінці є той самий двокістковий розв'язок.\n")
    L.append("Ковзання в опорі прибране в самих кліпах руху (`clean_feet` у `clips_lib.py`): стопа нерухома у світі, а швидкість кліпа на місці дорівнює швидкості стопи в опорі. Рухайте персонажа рівно зі `speed` із цього графа, інакше ковзання повернеться.\n")
    L.append("\n## Ринг і місця кліпів\n")
    L.append("Сторінка показує справжній ринг гри: об'єкти `ring_*` із `Assets/Art/Ring/venue_<арена>.fbx`, канати — `RopeSim` за якорями `anchor_rope_*` (`tools/ring_preview/ropes.js` — той самий розрахунок, що `RingRopes.cs`). Розміри беруться з якорів, не з констант.\n")
    L.append("| Арена | Футів | Висота мату, м | Стійка від центру, м | Лінія каната від центру, м | Край платформи, м |")
    L.append("|---|---|---|---|---|---|")
    L.append("| `arena` | 20 | 1,20 | 2,828 | 2,748 | 3,048 |")
    L.append("| `hall` | 18 | 1,10 | 2,523 | 2,443 | 2,743 |")
    L.append("| `gym` (зараз у грі) | 16 | 1,00 | 2,218 | 2,138 | 2,438 |")
    L.append("\nКанати скрізь на 0,48 / 0,86 / 1,24 м над матом. Сітка 1,5 м на ринг 16 футів уміщає лише 3 клітини, на 20 футів — 4 (6 м), і крайні клітини заходять за канати: бійцівський квадрат 5,5 м.\n")
    L.append("Де ставити кліпи (за ними задача 3 будує Animator; hs — зріст постаті відносно скелета, бо Humanoid масштабує корінь):\n")
    L.append("| Кліп | Початок | Що доробити в рушії |")
    L.append("|---|---|---|")
    L.append("| `Rope_*`, `Move_Rope_Rebound` | лінія каната кліпу — на лінії каната рингу: 1,83 hs від старту кліпу (генератор: ROPE_D + 0,12); для `Move_Rope_Rebound` — найдальша точка кореня + 0,10 hs. Таз у найглибшій точці — від 0 до 0,28 hs перед канатом (за варіантом), спина й кисті — на канаті | канат прогинає фізика; входити в кліп посеред бігу — лише до розвороту спиною (поворот кореня менше 7°) |")
    L.append("| `Move_Corner_*`, `Whip_Corner_*_B` | таз за 0,42 hs від стійки по діагоналі | IK кистей на верхній канат: кліп тримає їх на висоті 1,24 hs |")
    L.append("| `Corner_Climb_Top`, `Top_Rope_Perch_Loop`, `Top_Dive_*` | таз за 0,76 hs від стійки | зсув постаті по висоті: 1,31 − 1,145 hs (чоловічі кліпи), 1,31 − 1,034 hs (жіночі) |")
    L.append("| `Entrance_Pose_Turnbuckle_*` | за 0,9 hs від перетину канатів кута, обличчям до стійки | зсув по висоті 0,86 (1 − hs) за першу третину кліпу |")
    L.append("| `Entrance_Through_Ropes` | на 0,18 hs зовні від лінії каната | |")
    L.append("| `Entrance_Vault_Over_Top` | на 0,32 hs зовні, але не далі 0,06 м від краю платформи | фартух лише 0,30 м завширшки, кліп задуманий на 0,50 м |")
    L.append("| `Entrance_Roll_In` | на підлозі, 0,36 м від краю платформи | постать із підлоги на мат: висота від −мат до −hs |")
    L.append("| `Entrance_Climb_Steps` | якір `anchor_steps_*` на підлозі, обличчям до центру | те саме; сходи під висоту мату: `ring_steps_100/110/120` |")
    L.append("")
    L.append("## Ланцюг бою: постановка й стики")
    L.append("")
    L.append("Ланцюг бою сторінка розраховує наперед (`tools/ring_preview/director.js`): кожен борець — послідовність відрізків «кліп · з якої миті кліпу · коли · від якої бази · з якими поправками», а програвання кінематичне — борець у кожну мить там, де його поставив план. Правила, які Animator задачі 3 має повторити:")
    L.append("")
    L.append("- прийоми й пари — біля центру; пару ставить геометрія в мить контакту (подія `contact`, без неї — мить найближчого проходу), відстань — за середнім зростом пари;")
    L.append("- `Center_*`: жертва стоїть у центрі обличчям до бігуна, нападник вбігає з виходу з канатів; перші ~0,3 с кліпу B стоїть, тож B, що трохи спізнюється, може почати кліп пізніше на ту саму різницю (синхронно за кліповим часом);")
    L.append("- `Whip_Ropes*_B` закінчується бігом: кинутий продовжує в `Rope_*` з тієї ж швидкості й кроку (стик ≈ 2,5 м/с), лінія каната кліпу лягає на канат; із каната — у `Center_*_A` з виходу з канатів, після ухилу — до протилежних канатів без гальмування;")
    L.append("- стики — у мить, коли поза стоп, кистей, голови й висота таза разом зі швидкостями найближчі (зіставлення по кадрах); кросфейд змішує і позу, і швидкість кореня;")
    L.append("- хто переходить на інше місце — переходить кроками: `Move_Turn_*` (до 1,8× швидше, якщо часу мало), `Move_Walk_*`, `U_Walk_Loop`; лише залишок у кілька сантиметрів — поправкою, розподіленою на рухомий відрізок;")
    L.append("- кидок у канат має два боки, `Whip_Ropes` (повз праве плече) і `Whip_Ropes_Left`: бік обирається так, щоб жертві до ухилу було найближче; другий відскок — звичайний, а якщо жертва не встигає розвернутись — повільніший біля канатів (`Rope_Roar`, `Rope_Heavy`, `Rope_Hesitate`);")
    L.append("- після ухилу жертва ліворуч від бігуна, тож удар перед удержанням — з того боку й на спину: `Center_ShoulderBlock`, `Center_ShoulderBlock_Heavy`;")
    L.append("- на малому рингу для високої пари (Молот на 16 і 18 футах) — скорочений ланцюг: кидок у канат → відскок → одразу удар.")
    L.append("")
    L.append("## Рух по рингу: цикли й стани руху")
    L.append("")
    L.append("- біг — від каната до каната вздовж осі через центр (z чи x); відскок розвертає не рівно на 180°, тож курс вирівнюється дугою самого бігу (на вставці між канатами й на вибігу після відштовхування; шлях гнеться разом із тілом, без ковзання вбік), а бічний зсув знімається прицілом на лінію через центр (до 0,22 рад); лінію каната кліпу ставимо на площину каната вздовж осі рингу, навіть якщо бігун заходить під кутом; з довільної точки — дугою радіуса 1 м на ту вісь, де після дуги попереду найбільше місця (не менше 1,3 м) і найменший зсув від центру, а дуга не підходить до канатів ближче 0,45 м;")
    L.append("- розгін (`Run_Start`) — з осі через центр, відступивши рівно стільки, щоб після нього до каната лишилося місце на вхід у відскок до розвороту (до 0,2 м добирає поправка); вісь — та, куди дійти найшвидше (до 1,15 м — кроками в стійці, без розворотів); якщо розгін не вміщається (високий борець на малому рингу), він обривається там, де це місце ще є;")
    L.append("- біг спиною (`Run_Backpedal_Loop`) канатами не бігає: кліпи відскоку починаються бігом обличчям до каната; він, як хода, — колом;")
    L.append("- хода — колом навколо центру радіуса «лінія каната − 0,95 м» (від 1,1 до 1,8 м); з будь-якої точки — керованими дугами (радіус не менше 0,8 м) на це коло, у той бік, куди борець уже йде;")
    L.append("- кружляння в стійці — навколо центру рингу (центр кола — з кривої самого кліпу);")
    L.append("- вихід зі стану руху — у найближчу мить, коли борець біжить чи йде сам, а не лежить спиною на канаті (відскок уже відштовхнув, +0,12 с); кліп, якому треба місце попереду (зупинка з бігу), — ще й коли попереду є його довжина + 0,45 м.")
    L.append("")
    L.append("## Такт матчу: текст на тілі")
    L.append("")
    L.append("Виноска, звук, спалах і рядок журналу події матчу йдуть на такті — на мітці кліпа, який грає крок цієї події (`Gameplay/Ring/Stage/RingBeats.cs`, режисер `RingMatchDirector`). Відлік — від миті, коли кліп справді стартував (після підходу), у часі гри (з `PlaySpeed` і сповільненням):")
    L.append("")
    L.append("- мітки удару, з першого кроку, що їх має: `contact`; перший `rope_hit` (спина в канаті); `corner_hit`; виміряні миті для кліпів без мітки (сторінка анімацій, 08.11.2026, кожну перевірено кадрами): `Bodyslam` 1,65 (кинутий лягає), `Bump_Back` 0,52, `Cover` 0,47, `Center_Sidestep` 0,75 (рука над головою), `Chain_Headlock` і `Chain_Headlock_Crank` 0,50, `Chain_ShoveOff` 0,25, `GetUp_Back` 1,92 (уже стоїть); варіанти `Lockup_*` беруть мітку бази `Lockup` (0,616); крок-наслідок (суперник підвівся під час кривляння, попередження рефері) такту не несе;")
    L.append("- якщо мітки удару немає — мітки дії: `release` (кидок відпустив), `rope_lean`, `raise` чи `bell` (рефері);")
    L.append("- запасне правило — 0,4 с від старту кліпа першого кроку, але не пізніше 0,45 його довжини;")
    L.append("- утримання: «раз» і «два» — на долонях рефері (`count_1`, `count_2` його кліпа), розв'язка — `kickout` (вирвався) чи `count_3`; рядок журналу — на розв'язці, не раніше «два»;")
    L.append("- фінішер: сповільнення 0,4× стартує за 0,15 с до контакту й накриває його;")
    L.append("- прийом із розбігом (`Center_*`: нападник стартує за 2,2–3,5 м, а від центру до каната рингу 16 футів — 2,1 м) стартує посеред бігу, коли до суперника 1,5 м; суперник — у центрі, тож контакт у центрі.")
    L.append("")
    L.append("## Рух у грі (Unity): хода, переходи, канати")
    L.append("")
    L.append("Ті самі правила, що в режисера сторінки, у грі веде `RingMatchDirector` поверх `RingActor` (08.11.2026, пакет «Відео огляд», п. 8):")
    L.append("")
    L.append("- перестановки між подіями — кроками, не ковзанням: ходовий кліп — той, чий хід (з кривої таза самого кліпа на цьому тілі) ближчий до напрямку цілі відносно погляду: `Move_Walk_Fwd_Loop`, `Move_Walk_Back_Loop`, `Move_Circle_L_Loop`, `Move_Circle_R_Loop`; рефері — `Ref_Walk_Loop`; швидкість ×1–2,6 (рефері ×1–1,8), щоб дійти за ~0,7 с; таз несе сам кліп, слот лише повертається навколо таза — кермо дугою, до 120°/с;")
    L.append("- розворот на місці від 45° — `Move_Turn_L90`, `Move_Turn_R90`, `Move_Turn_180`, решта — переходом у стійку з новим курсом; рефері, що після відліку на колінах, спершу встає (`Ref_Kneel_Up`);")
    L.append("- після події борці в центрі лишаються там, де закінчили (лише відстань 1,2–2,6 м і 0,6 м від канатів); рефері лишається, якщо не стоїть між ними чи перед ними з боку камери; пара прийому — у центрі рингу, вздовж лінії між борцями;")
    L.append("- перехід між кліпами: таз на екрані — змішування двох передбачень (старий рух триває колишньою швидкістю; новий кліп іде від точки входу за своїм справжнім часом), курс — коротшим шляхом до курсу нового кліпа, наприкінці — точно задумане вирівнювання; слот щокадру ставиться під виміряну позу (змішаний таз гуманоїда — не лінійна суміш тазів кліпів); новий кліп, що не зрушив на першому кадрі, підштовхується; новий перехід посеред незавершеного не обриває позу (мікшер на три входи);")
    L.append("- петлі з запеченим ходом (біг, хода, кружляння) при перемотуванні переносять слот на хід петлі — таз іде далі без стрибка;")
    L.append("- вибірка кліпа для розрахунку (тимчасовий граф на тому ж аніматорі) зберігає й повертає кістки: без цього поза до наступного кадру лишалась у вибірці;")
    L.append("- кидок у канати: біг уздовж ±X (видно збоку з боку залу), бік — де обом борцям повертатись до кидка найменше; лінія каната кліпа відскоку — від таза першого кадру (1,83 одиниці ригу); мить входу в кліп відскоку — щокадру з того, де таз, з прогнозом щонайменше на 0,1 с (ранній вхід розв'язується підбором миті в кліпі, пізній — ні); вхід — коротким переходом 0,12 с за ходом кліпа; з бігу кидка — одразу в канат, якщо вставка бігу не потрібна; у кут — дальній кут, де обом повертатись найменше;")
    L.append("- пара прийому в центрі: весь шлях обох у кліпах (від миті старту) — у рингу, на 0,25 м від лінії канатів, інакше пара зсувається всередину; хто біжить на канати під час прийому в центрі (розбіг + пробіг довші за ринг), — за 0,3 м від лінії зупиняється в стійку; хто лежить — лишається, де впав (пару не центрує, до нього підходить нападник), а спершу долежує свій кліп падіння; зсув усередину — лише на мінімум, коли інакше нападник ішов би крізь канати; нападник, що ще встає, спершу встає;")
    L.append("- «стоїть / не стоїть» — за висотою тазу відносно випрямленої пози цієї постаті (0,68), не в метрах: у нижчих борців таз у стійці 0,63 м;")
    L.append("- кіноплан фінішера: рефері йде за пару від камери; якщо в мить склейки він між камерою й парою — камера бере протилежний бік, а він зупиняється; голови борців і рефері — нижче 27 % висоти кадру (слово «ФІНІШЕР!» — у верхніх 8–22 %, Статут вигляду FX-05); не вміщається — ракурс ±25°, потім протилежний бік;")
    L.append("- `Rope_Back_Stop` наприкінці (~1,23 с) стрибає тазом назад на ~0,77 м за кадр — режисер обриває кліп перед стрибком і переходить у стійку (вада кліпа, передано сесії анімацій);")
    L.append("- фаз-тест сцени (`RingStageWatch`): телепорти на очах, перетини канатів нижче верхнього, ковзання стоп — підсумок у звіті огляду (`-ring-shots`) і демо (`-ring-demo`); налагодження руху — `-ring-demo <тека> -ring-locotest 1`, камера на ноги — `-ring-demo-feet 1`.")
    L.append("")
    L.append("\n## Збірка й перевірка\n")
    L.append("- Сторінка: `python tools/ring_preview/run.py` (без інтернету).")
    L.append("- Усе з нуля, ≈ 8 хв: `sh tools/blender/ring_cast/build_all.sh <робоча тека> [персонажі]`; `SKIP_CLIPS=1` не чіпає кліпи. Спершу кеш кліпів: `extract_clips.py`.")
    L.append("- Вихідні `.blend` у `ArtSource/Ring/Cast/` (поза git, 1,5 ГБ): їх дає та сама збірка.")
    L.append("- Кліпи числами: `python tools/blender/ring_cast/clips_qa.py <кліпи>` (шви, стрибки, стопи, ковзання); пари: `pair_check.py`; одяг: `cast_cloth_check.py`.")
    L.append("- Імпорт у Unity: `powershell -File tools/blender/ring_cast/unity_check/run_check.ps1 -Work <тимчасова тека>` (окремий проєкт, без вікна): Humanoid, Avatar, програвання кліпів на кожній моделі.")
    L.append("- Кожна модель має власний Avatar з однаковою Humanoid-розміткою, а не один спільний файл: пропорції різні. Кліпи з `RING_Cast_M.fbx` і `RING_Cast_F.fbx` лягають на всі моделі через Humanoid.")
    open(DOC, "w", encoding="utf-8").write("\n".join(L))


if __name__ == "__main__":
    os.makedirs(DATA, exist_ok=True)
    out = build()
    write_doc(out)
    # Метадані кліпів і дані кастингу для сторінки
    cast = {"order": CS.ORDER, "characters": {}}
    root_scale = {}
    for sex in ("m", "f"):
        d, meta, sk = load(sex)
        for c, m in meta.items():
            r = d["r/" + c].astype(np.float64)
            if np.abs(r).max() > 1e-4:
                m["root"] = [[round(float(x), 4) for x in row] for row in r]
        json.dump(meta, open(os.path.join(DATA, "clips_%s.json" % sex), "w", encoding="utf-8"), ensure_ascii=False)
        root_scale[sex] = float(sk.rest_p[0, 2])
    for cid in CS.ORDER:
        spec = dict(CS.CAST[cid])
        meta = json.load(open(os.path.join(DATA, "clips_%s.json" % spec["sex"]), encoding="utf-8"))
        spec["clips"] = role_clips(meta, spec)
        spec["skeleton"] = spec["sex"]
        spec["hip_rest"] = root_scale[spec["sex"]]
        cast["characters"][cid] = spec
    json.dump(cast, open(os.path.join(DATA, "cast.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=0)
    bad = [(s, e["from"], e["to"], e["metric"]) for s in out["edges"] for e in out["edges"][s] if e.get("warn")]
    print("WARN edges needing transition:", bad)
    print("OK", {c: len(cast["characters"][c]["clips"]) for c in CS.ORDER})
