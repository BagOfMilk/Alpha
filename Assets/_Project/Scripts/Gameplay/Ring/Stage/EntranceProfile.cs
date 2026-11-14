using System;
using System.Collections.Generic;
using Game.Core.Ring;
using Game.Core.Ring.Career;

namespace Game.Gameplay.Ring.Stage
{
    /// <summary>Як борець заходить у ринг (біт 3 персонального виходу).</summary>
    public enum EntranceRingEntry
    {
        /// <summary>Сходи → між канатами (<c>Entrance_Climb_Steps</c> → <c>Entrance_Through_Ropes</c>).</summary>
        StepsThroughRopes,
        /// <summary>Сходи → перемах через верхній канат (<c>Entrance_Vault_Over_Top</c>).</summary>
        StepsVault,
        /// <summary>З підлоги біля фартуха під нижнім канатом, без сходів (<c>Entrance_Roll_In</c>).</summary>
        RollIn,
    }

    /// <summary>Де стоїть поза (біт 4).</summary>
    public enum EntrancePoseSpot
    {
        /// <summary>Центр рингу.</summary>
        Center,
        /// <summary>Там, де борець опинився після входу.</summary>
        Here,
        /// <summary>На другому канаті в куті, обличчям до стійки (до залу).</summary>
        Turnbuckle,
        /// <summary>Обличчям до канатів, площина канатів за 0,62 м попереду.</summary>
        Ropes,
    }

    /// <summary>Вхідні дані профілю — однакові для гравця й суперника-NPC (<c>docs/ring/ENTRANCE_PROFILE.md</c>, «Як влаштовано»).</summary>
    public sealed class EntranceInput
    {
        public Gimmick Gimmick = Gimmick.Showman;
        public RingPath Path = RingPath.Face;
        /// <summary>Сила, Спритність, Техніка, Харизма: −1..3.</summary>
        public int Strength, Agility, Technique, Charisma;
        public bool Injured;
        /// <summary>Популярність 0..10.</summary>
        public int Popularity;
        /// <summary>Жар 0..4.</summary>
        public int Heat;
        public bool Champion, MainEvent, Title, Player, Female;

        /// <summary>Гравець кар'єри: ґімік, домінантний шлях, стати, травма, популярність, жар ворожнечі, пояс, позиція вечора.</summary>
        public static EntranceInput ForPlayer(CareerSession s, bool female)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            var i = new EntranceInput
            {
                Gimmick = s.State.Gimmick, Path = s.CurrentRole, Injured = s.State.IsInjured, Popularity = s.Popularity,
                Heat = s.Feud.State.Heat, Champion = s.IsChampion, MainEvent = s.State.Position == CardPosition.Main,
                Title = s.State.IsTitle, Player = true, Female = female,
            };
            i.SetStats(s.Player);
            return i;
        }

        /// <summary>Суперник-NPC: популярність = ранг × 2 (до 10), жар — рівень ворожнечі з гравцем, пояс — якщо він тримач.</summary>
        public static EntranceInput ForNpc(CareerSession s, NpcDef npc, bool female)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            if (npc == null) throw new ArgumentNullException(nameof(npc));
            var i = new EntranceInput
            {
                Gimmick = npc.Gimmick, Path = npc.Path, Popularity = Math.Min(10, npc.Rank * 2), Heat = s.FeudLevel(npc.Id),
                Champion = s.State.BeltHolder == npc.Id, MainEvent = s.State.Position == CardPosition.Main,
                Title = s.State.IsTitle, Player = false, Female = female,
            };
            i.SetStats(npc.Stats);
            return i;
        }

        public void SetStats(WrestlerStats st)
        {
            if (st == null) return;
            Strength = st.Get(RingAttr.Strength); Agility = st.Get(RingAttr.Agility);
            Technique = st.Get(RingAttr.Technique); Charisma = st.Get(RingAttr.Charisma);
        }
    }

    /// <summary>
    /// Персональний вихід борця: п'ять бітів (завіса, хода, вхід у ринг, поза, шоу), кожен — детермінована чиста функція від
    /// даних борця (<c>docs/ring/ENTRANCE_PROFILE.md</c>, власник 06.11.2026: «выход зависил от твоих статов + от того какой у
    /// тебя гиммик, хил\фейс»). Вихід — лише вигляд, на ажіотаж матчу не впливає. Охоронці — <c>EntranceProfileTests</c>.
    /// Кліпи однакові в <c>RING_Entrance.fbx</c> і <c>RING_Entrance_F.fbx</c>; набір обирає сцена за <see cref="EntranceInput.Female"/>.
    /// </summary>
    public sealed class EntranceProfile
    {
        /// <summary>Перша голова світла — колір костюма (Статут вигляду COL-07: костюм — індивідуальність борця).</summary>
        public const string CostumeHead = "costume";

        // біт 1 — завіса
        public string CurtainGimmick;
        /// <summary>Жест шляху після жесту ґіміка; null — без жесту.</summary>
        public string CurtainPath;
        // біт 2 — хода
        public string WalkLoop;
        /// <summary>Зупинка біля бар'єра (точка шляху (-8, 0, 0)); null — без зупинки.</summary>
        public string Barrier;
        // біт 3 — вхід у ринг
        public EntranceRingEntry Entry;
        public string EntryClip;
        /// <summary>Швидкість сходів і входу (монстр — 0,8: повільно, важко).</summary>
        public float EntrySpeed = 1f;
        public bool UsesSteps { get { return Entry != EntranceRingEntry.RollIn; } }
        // біт 4 — поза
        public string Pose;
        public EntrancePoseSpot PoseSpot;
        /// <summary>Найменша тривалість пози, с: 2,6; Харизма ≥ 2 — 4,0.</summary>
        public float PoseSeconds;
        /// <summary>Чемпіон: після пози <c>Entrance_Pose_Belt_Raise</c> з поясом.</summary>
        public bool Belt;
        // біт 5 — шоу
        /// <summary>Чотири голови світла: <see cref="CostumeHead"/> або ім'я токена (<c>brand_tokens.json</c>).</summary>
        public string[] HeadTokens;
        /// <summary>Яскравість кожної голови (кармін 0,35 — темно-червоний такт лиходія).</summary>
        public float[] HeadGain;
        /// <summary>Множник світла залу (лиходій −30 %).</summary>
        public float HallLight = 1f;
        /// <summary>Темп голів за профілем; сцена бере BPM ролика екрана виходу (B07 п. 2), цей — запасний.</summary>
        public int Bpm;
        /// <summary>Піро на стійках рингу: головний бій, титульний або чемпіон.</summary>
        public bool Pyro;
        /// <summary>Іскри біля завіси: Харизма ≥ 2.</summary>
        public bool Sparks;
        /// <summary>Гомін залу 1..4.</summary>
        public int Crowd = 1;
        /// <summary>Лиходію — гул, не оплески.</summary>
        public bool CrowdBoo;

        public const string WalkFallback = "Entrance_Walk_Loop";
        public const string PoseFallback = "Entrance_Corner_Pose";
        public const string Steps = "Entrance_Climb_Steps";
        public const string BeltRaise = "Entrance_Pose_Belt_Raise";

        /// <summary>
        /// Сімейство виходу ґіміка: чотири ґіміки, для яких зняті кліпи завіси, хода, поза й пули екрана виходу (36 пулів B07).
        /// Ґіміки 5–8 «Кар'єри» (Бувалий, Хардкорник, Самородок, Силач) власних кліпів і пулів не мають і виходять наявним сімейством
        /// за характером ґіміка: Бувалий — техніком (читає суперника, техніка), Хардкорник і Силач — монстром (б'є важко, сила,
        /// повільний важкий вхід), Самородок — шоуменом (рівна основа, без спеціалізації). Раніше вони мовчки падали в «default» —
        /// Шоумена — і кліп завіси збирався з імені, якого немає в наборі (рев'ю ПМ 12–13.11.2026). Власні кліпи чи пули — лише через У0.
        /// Охоронці: <c>EntranceProfileTests.EveryGimmick_HasExplicitFamily</c> і <c>EveryProfileClip_ExistsInBothSets</c> на всіх значеннях.
        /// </summary>
        private static readonly Dictionary<Gimmick, Gimmick> Families = new Dictionary<Gimmick, Gimmick>
        {
            { Gimmick.Monster, Gimmick.Monster },
            { Gimmick.Flyer, Gimmick.Flyer },
            { Gimmick.Technician, Gimmick.Technician },
            { Gimmick.Showman, Gimmick.Showman },
            { Gimmick.Veteran, Gimmick.Technician },
            { Gimmick.Hardcore, Gimmick.Monster },
            { Gimmick.Rookie, Gimmick.Showman },
            { Gimmick.Strongman, Gimmick.Monster },
        };

        /// <summary>Ґіміки, що мають власні кліпи виходу й пули екрана, — значення <see cref="Family"/>.</summary>
        public static readonly Gimmick[] FamilyGimmicks = { Gimmick.Monster, Gimmick.Flyer, Gimmick.Technician, Gimmick.Showman };

        /// <summary>Чи має ґімік рядок у таблиці сімейств (інакше <see cref="Family"/> дає запасного Шоумена).</summary>
        public static bool HasFamily(Gimmick g) { return Families.ContainsKey(g); }

        /// <summary>Сімейство виходу ґіміка (див. таблицю вище); ґімік без рядка — Шоумен (охоронець не пускає такий у збірку).</summary>
        public static Gimmick Family(Gimmick g)
        {
            Gimmick f;
            return Families.TryGetValue(g, out f) ? f : Gimmick.Showman;
        }

        public static EntranceProfile Compose(EntranceInput i)
        {
            if (i == null) throw new ArgumentNullException(nameof(i));
            var p = new EntranceProfile();
            bool face = i.Path == RingPath.Face, heel = i.Path == RingPath.Heel;
            // Далі всі правила — за сімейством, не за самим ґіміком: Силач виходить монстром, а не шоуменом.
            Gimmick fam = Family(i.Gimmick);

            // біт 1
            p.CurtainGimmick = "Entrance_Curtain_" + fam;
            if (i.Charisma >= 1)
                p.CurtainPath = face ? "Entrance_Curtain_Face_Point" : heel ? "Entrance_Curtain_Heel_Dismiss" : null;

            // біт 2 — перше збігання згори вниз
            if (i.Injured) p.WalkLoop = "Entrance_Walk_Limp_Loop";
            else if (i.Strength >= 3 && fam != Gimmick.Flyer) p.WalkLoop = "Entrance_Walk_Heavy_Loop";
            else if (i.Agility >= 3 && fam != Gimmick.Monster) p.WalkLoop = "Entrance_Walk_Bouncy_Loop";
            else
                switch (fam)
                {
                    case Gimmick.Monster: p.WalkLoop = "Entrance_Walk_Heavy_Loop"; break;
                    case Gimmick.Flyer: p.WalkLoop = "Entrance_Walk_Bouncy_Loop"; break;
                    case Gimmick.Technician: p.WalkLoop = "Entrance_Walk_Focused_Loop"; break;
                    case Gimmick.Showman:
                    default: p.WalkLoop = "Entrance_Walk_Strut_Loop"; break;
                }
            if (face && i.Popularity >= 4) p.Barrier = "Entrance_Barrier_High_Five";
            else if (heel && i.Heat >= 2) p.Barrier = "Entrance_Barrier_Heel_Taunt";

            // біт 3
            if (fam == Gimmick.Monster && i.Strength >= 2) { p.Entry = EntranceRingEntry.StepsThroughRopes; p.EntryClip = "Entrance_Through_Ropes"; p.EntrySpeed = 0.8f; }
            else if (fam == Gimmick.Flyer && i.Agility >= 2) { p.Entry = EntranceRingEntry.StepsVault; p.EntryClip = "Entrance_Vault_Over_Top"; }
            else if (heel || i.Agility >= 2) { p.Entry = EntranceRingEntry.RollIn; p.EntryClip = "Entrance_Roll_In"; }
            else { p.Entry = EntranceRingEntry.StepsThroughRopes; p.EntryClip = "Entrance_Through_Ropes"; }

            // біт 4
            switch (fam)
            {
                case Gimmick.Monster: p.Pose = "Entrance_Pose_Monster_Roar"; p.PoseSpot = EntrancePoseSpot.Center; break;
                case Gimmick.Flyer:
                    if (heel) { p.Pose = "Entrance_Pose_Heel_Shush"; p.PoseSpot = EntrancePoseSpot.Here; }
                    else { p.Pose = "Entrance_Pose_Turnbuckle_Point"; p.PoseSpot = EntrancePoseSpot.Turnbuckle; }
                    break;
                case Gimmick.Technician:
                    if (heel) { p.Pose = "Entrance_Pose_Heel_Mock"; p.PoseSpot = EntrancePoseSpot.Here; }
                    else { p.Pose = "Entrance_Pose_Technician_Stretch"; p.PoseSpot = EntrancePoseSpot.Ropes; }
                    break;
                case Gimmick.Showman:
                default:
                    if (heel) { p.Pose = "Entrance_Pose_Heel_Shush"; p.PoseSpot = EntrancePoseSpot.Here; }
                    else { p.Pose = "Entrance_Pose_Turnbuckle_Arms"; p.PoseSpot = EntrancePoseSpot.Turnbuckle; }
                    break;
            }
            p.PoseSeconds = i.Charisma >= 2 ? 4.0f : 2.6f;
            p.Belt = i.Champion;

            // біт 5. Кольори голів — лише токени (Статут вигляду COL-01); золото — лише гравцеві (COL-02, FX-09), тому
            // «Gold» профілю в NPC стає кольором ролі; «#3A0A0A» лиходія — кармін на 0,35 яскравості; «Red» бунтаря — рожевий.
            switch (i.Path)
            {
                case RingPath.Heel:
                    p.HeadTokens = new[] { CostumeHead, "crimson", "crimson", "crimson" };
                    p.HeadGain = new[] { 1f, 1f, 0.35f, 1f };
                    p.HallLight = 0.7f;
                    p.CrowdBoo = true;
                    p.Crowd = 1 + i.Heat / 2;
                    break;
                case RingPath.Rebel:
                    p.HeadTokens = new[] { CostumeHead, "paper", "rose", i.Player ? "gold" : "rose" };
                    p.HeadGain = new[] { 1f, 1f, 1f, 1f };
                    p.Crowd = 2;
                    break;
                default:
                    p.HeadTokens = new[] { CostumeHead, i.Player ? "gold" : "cobaltText", "paper", "paper" };
                    p.HeadGain = new[] { 1f, 1f, 1f, 1f };
                    p.Crowd = 1 + i.Popularity / 3;
                    break;
            }
            if (p.Crowd < 1) p.Crowd = 1;
            if (p.Crowd > 4) p.Crowd = 4;
            switch (fam)
            {
                case Gimmick.Monster: p.Bpm = 96; break;
                case Gimmick.Technician: p.Bpm = 110; break;
                case Gimmick.Flyer: p.Bpm = 132; break;
                case Gimmick.Showman:
                default: p.Bpm = 120; break;
            }
            p.Pyro = i.MainEvent || i.Title || i.Champion;
            p.Sparks = i.Charisma >= 2;
            return p;
        }

        /// <summary>Усі кліпи профілю в порядку бітів (для перевірки, що набір їх має).</summary>
        public string[] Clips()
        {
            var l = new System.Collections.Generic.List<string> { CurtainGimmick };
            if (CurtainPath != null) l.Add(CurtainPath);
            l.Add(WalkLoop);
            if (Barrier != null) l.Add(Barrier);
            if (UsesSteps) l.Add(Steps);
            l.Add(EntryClip);
            l.Add(Pose);
            if (Belt) l.Add(BeltRaise);
            return l.ToArray();
        }
    }
}
