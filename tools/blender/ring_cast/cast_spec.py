# Склад «Рингу» для задачі «оживити борців»: хто є хто, зріст, тіло, реквізит, імена кісток.
# Спільний для Blender-скриптів (exec) і звичайного Python (import). Без залежностей.

PREFIX = "mixamorig:"

# Скелет набору (kit_m / kit_f, 53 кістки) -> імена й ієрархія mixamorig. Кістка Root викидається: у mixamorig коренем є Hips.
_FINGERS = {"thumb": "Thumb", "index": "Index", "middle": "Middle", "ring": "Ring", "pinky": "Pinky"}
BONE_MAP = {
    "pelvis": "Hips", "spine_01": "Spine", "spine_02": "Spine1", "spine_03": "Spine2", "neck_01": "Neck", "head": "Head",
}
for _s, _S in (("l", "Left"), ("r", "Right")):
    BONE_MAP.update({
        "clavicle_" + _s: _S + "Shoulder", "upperarm_" + _s: _S + "Arm", "lowerarm_" + _s: _S + "ForeArm", "hand_" + _s: _S + "Hand",
        "thigh_" + _s: _S + "UpLeg", "calf_" + _s: _S + "Leg", "foot_" + _s: _S + "Foot", "ball_" + _s: _S + "ToeBase",
    })
    for _f, _F in _FINGERS.items():
        for _i in (1, 2, 3):
            BONE_MAP["%s_0%d_%s" % (_f, _i, _s)] = "%sHand%s%d" % (_S, _F, _i)

# Батько кожної кістки в mixamorig (те саме дерево, що в Y Bot, без кінцевих *_End і *4).
MIXAMO_PARENT = {"Hips": None, "Spine": "Hips", "Spine1": "Spine", "Spine2": "Spine1", "Neck": "Spine2", "Head": "Neck"}
for _S in ("Left", "Right"):
    MIXAMO_PARENT.update({
        _S + "Shoulder": "Spine2", _S + "Arm": _S + "Shoulder", _S + "ForeArm": _S + "Arm", _S + "Hand": _S + "ForeArm",
        _S + "UpLeg": "Hips", _S + "Leg": _S + "UpLeg", _S + "Foot": _S + "Leg", _S + "ToeBase": _S + "Foot",
    })
    for _F in _FINGERS.values():
        MIXAMO_PARENT["%sHand%s1" % (_S, _F)] = _S + "Hand"
        MIXAMO_PARENT["%sHand%s2" % (_S, _F)] = "%sHand%s1" % (_S, _F)
        MIXAMO_PARENT["%sHand%s3" % (_S, _F)] = "%sHand%s2" % (_S, _F)

# Скелет UAL (Quaternius, CC0, DEF-*) -> скелет набору.
UAL_MAP = {
    "DEF-hips": "pelvis", "DEF-spine.001": "spine_01", "DEF-spine.002": "spine_02", "DEF-spine.003": "spine_03",
    "DEF-neck": "neck_01", "DEF-head": "head",
}
for _u, _s in ((".L", "l"), (".R", "r")):
    UAL_MAP.update({
        "DEF-shoulder" + _u: "clavicle_" + _s, "DEF-upper_arm" + _u: "upperarm_" + _s, "DEF-forearm" + _u: "lowerarm_" + _s,
        "DEF-hand" + _u: "hand_" + _s, "DEF-thigh" + _u: "thigh_" + _s, "DEF-shin" + _u: "calf_" + _s,
        "DEF-foot" + _u: "foot_" + _s, "DEF-toe" + _u: "ball_" + _s,
    })
    for _f in ("index", "middle", "ring", "pinky"):
        for _i in (1, 2, 3):
            UAL_MAP["DEF-f_%s.0%d%s" % (_f, _i, _u)] = "%s_0%d_%s" % (_f, _i, _s)
    for _i in (1, 2, 3):
        UAL_MAP["DEF-thumb.0%d%s" % (_i, _u)] = "thumb_0%d_%s" % (_i, _s)

# id -> специфікація. letter — літера з брифу; height — зріст у метрах (верх черепа, без зачіски; ПЛЕЙСХОЛДЕР: у gdd чисел немає).
CAST = {
    "stalevyi": dict(letter="А", name="Богдан «Сталевий» Дуб", sex="m", culture="ukrainian", height=1.92, archetype="Силач-чемпіон, герой",
                     role="wrestler", props=["belt"], hair="hair_short02", beard=None, shape="power"),
    "dzhura": dict(letter="Б", name="Матео «Колібрі» Рамос", sex="m", culture="latin", height=1.85, archetype="Летун-лучадор, герой",
                   role="wrestler", props=["chair"], hair=None, beard=None, shape="lean"),
    "baron": dict(letter="Г", name="Олівер «Барон» Вінслоу", sex="m", culture="mediterranean", height=1.88, archetype="Шоумен, лиходій",
                  role="wrestler", props=["chair", "kendo", "case"], hair="hair_short01", beard=None, shape="showman"),
    "hammer": dict(letter="Бос", name="Тарас «Молот» Гребінь", sex="m", culture="nordic", height=2.40, archetype="Монстр, головний лиходій",
                   role="wrestler", props=[], hair=None, beard=None, shape="monster"),
    "komet": dict(letter="В", name="Амара «Комета» Окафор", sex="f", culture="west_african", height=1.70, archetype="Чемпіонка-летунка, героїня",
                  role="wrestler", props=["mic"], hair="hair_afro01", beard=None, shape="athletic_f"),
    "vdova": dict(letter="", name="Мей «Вдова» Лін", sex="f", culture="east_asian", height=1.68, archetype="Технік, лиходійка",
                  role="wrestler", props=[], hair="hair_ponytail01", beard=None, shape="lean_f"),
    "novachka": dict(letter="", name="Новачка", sex="f", culture="ukrainian", height=1.66, archetype="Новачка-бунтарка, героїня гравця",
                     role="wrestler", props=[], hair="hair_cortu_short_messy_hair", beard=None, shape="scrappy_f"),
    "lina": dict(letter="Д", name="Ліна Ворон", sex="f", culture="mediterranean", height=1.72, archetype="Менеджерка й букерка, не борець",
                 role="manager", props=["spray"], hair="hair_ponytail01", beard=None, shape="plain_f"),
}
ORDER = ["stalevyi", "dzhura", "baron", "hammer", "komet", "vdova", "novachka", "lina"]
