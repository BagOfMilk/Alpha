using System.Collections.Generic;
using Game.Core.Ring;
using Game.Core.Ring.Match;

namespace Game.Gameplay.Ring.Stage
{
    /// <summary>
    /// Яким планом знімати крок: загальним, ближче (удар) чи кіно (кидок, фінішер, утримання). <see cref="Cine"/> — кіноплан,
    /// який режисер рахує сам з поз борців (фінішер, перемога): склейкою, а не наїздом. <see cref="Low"/> — знизу вгору (план
    /// «на канаті» склейки).
    /// </summary>
    public enum StageCam { Overview = 0, Close = 1, Big = 2, Low = 3, Cine = 4 }

    /// <summary>
    /// Вид кроку режисури. Рішення власника 08.11.2026 (TRPG_DESIGN §4.4, §4.12): усе — у центрі рингу, бігу по рингу немає;
    /// прийом із канатів чи кута — склейкою кадру (<see cref="Cut"/>). Значення 2 (хода в зону) і 5 (кидок у канати з бігом)
    /// прибрано разом із зонами.
    /// </summary>
    public enum StageStepKind
    {
        /// <summary>Парний кліп: A і B за зсувом із маніфесту (<see cref="StageStep.Clip"/> + _A/_B), центр пари — центр рингу.</summary>
        Pair = 0,
        /// <summary>Один борець грає кліп на місці.</summary>
        Solo = 1,
        /// <summary>Лише рефері (попередження, відлік).</summary>
        Ref = 3,
        /// <summary>Кінець матчу: рефері піднімає руку переможцю.</summary>
        End = 4,
        /// <summary>
        /// Склейка (план 1 з двох): нападник уже на канаті чи куті (<see cref="StageStep.Perch"/>) грає <see cref="StageStep.Clip"/>,
        /// суперник стоїть у центрі й не рухається; далі — жорстка склейка на наступний крок (приземлення пари в центрі). Ходьби між
        /// планами немає: режисер ставить нападника на місце одразу. Такту не несе — такт на контакті приземлення.
        /// </summary>
        Cut = 6,
        /// <summary>
        /// Склейка місця (бій за рингом, власник 08.11.2026: «Лишити в ітерації 1 склейкою»): після жорсткої склейки пара вже за
        /// рингом (<see cref="StageStep.Outside"/>) або знову на рингу. Бігу й перелазу через канати на екрані немає. Кліпа не має,
        /// тримає кадр <see cref="StageStep.Hold"/> с; такту не несе.
        /// </summary>
        Place = 7
    }

    /// <summary>Один крок режисури: що зіграти й ким. Чисті дані — без рушія, тож таблицю «дія → кліп» видно окремо.</summary>
    public sealed class StageStep
    {
        public StageStepKind Kind;
        /// <summary>Хто виконує (A парного кліпу, сольний борець, нападник склейки).</summary>
        public MatchSide Actor;
        /// <summary>База імені парного кліпу (без _A/_B) або ім'я сольного (і кліпа «на канаті» склейки).</summary>
        public string Clip;
        /// <summary>Кліп рефері, що йде разом із кроком (або єдиний у кроку Ref).</summary>
        public string RefClip;
        public StageCam Cam;
        /// <summary>Скільки тримати кадр, якщо кліпів нема, с.</summary>
        public float Hold;
        /// <summary>Кліп грається один раз, без петлі (для петльових Idle_*).</summary>
        public bool Once;
        /// <summary>Рефері дивиться на цього борця (попередження).</summary>
        public bool RefFacesActor;
        public string Note;
        /// <summary>Склейка: де стоїть нападник на плані 1 — на канатах чи на куті.</summary>
        public MoveStaging Perch;
        /// <summary>Крок-наслідок, а не сама дія події (суперник підвівся, рефері попереджає): такт на ньому не ставиться.</summary>
        public bool Aside;
        /// <summary>Склейка місця: true — пара тепер за рингом, false — знову на рингу.</summary>
        public bool Outside;

        /// <summary>Склейка місця: пара за рингом (<paramref name="outside"/>) чи знову на рингу; кадр тримається <paramref name="hold"/> с.</summary>
        public static StageStep Place(MatchSide who, bool outside, float hold, string note = null)
        {
            return new StageStep { Kind = StageStepKind.Place, Actor = who, Outside = outside, Hold = hold, Cam = StageCam.Overview, Note = note };
        }

        public static StageStep Pair(MatchSide a, string baseName, StageCam cam, string refClip = null, string note = null)
        {
            return new StageStep { Kind = StageStepKind.Pair, Actor = a, Clip = baseName, Cam = cam, RefClip = refClip, Note = note };
        }

        public static StageStep Solo(MatchSide who, string clip, StageCam cam, bool once = false, string note = null)
        {
            return new StageStep { Kind = StageStepKind.Solo, Actor = who, Clip = clip, Cam = cam, Once = once, Note = note };
        }

        /// <summary>План 1 склейки: <paramref name="who"/> на канаті чи куті грає <paramref name="clip"/> (один раз).</summary>
        public static StageStep Cut(MatchSide who, MoveStaging perch, string clip, string note = null)
        {
            return new StageStep { Kind = StageStepKind.Cut, Actor = who, Perch = perch, Clip = clip, Cam = StageCam.Low, Once = true, Note = note };
        }

        /// <summary>Усі кліпи кроку (для перевірки, що вони є в маніфесті): пара — _A і _B.</summary>
        public IEnumerable<string> AllClips()
        {
            switch (Kind)
            {
                case StageStepKind.Pair:
                    yield return Clip + "_A"; yield return Clip + "_B";
                    break;
                case StageStepKind.Solo:
                case StageStepKind.Cut:
                    yield return Clip;
                    break;
            }
            if (RefClip != null) yield return RefClip;
        }
    }

    /// <summary>
    /// Таблиця «подія матчу → кроки режисури» (TRPG_DESIGN §4.12): прийом кар'єри (<see cref="MatchEvent.MoveKey"/>) і ступінь кидка
    /// перекладає на кліпи рингу; без ключа — запасний вибір за дією. Усе грається в центрі рингу; постановка «канати» чи «кут»
    /// (<see cref="MatchEvent.Staging"/>) додає склейку (<see cref="StageStepKind.Cut"/>) перед приземленням у центрі. Чиста функція;
    /// пози режисер веде окремо з ефектів подій. Прийоми без власного кліпа взято найближчим наявним — див. Note і бриф
    /// <c>docs/ring/ANIM_BRIEF_COMBAT_CENTER.md</c>.
    /// </summary>
    public static class RingClipTable
    {
        public static MatchSide Opp(MatchSide s) { return s == MatchSide.Player ? MatchSide.Rival : MatchSide.Player; }

        /// <summary>Кліпи прийому кар'єри: частковий успіх (7–9), чистий (10+) і план «на канаті/куті», якщо прийом зі склейкою.</summary>
        private struct MoveClips
        {
            public string Partial, Strong, Perch;
            public StageCam Cam;
            public bool Solo;
            public string Note;
            public MoveClips(string partial, string strong, StageCam cam, string perch = null, bool solo = false, string note = null)
            { Partial = partial; Strong = strong; Cam = cam; Perch = perch; Solo = solo; Note = note; }
        }

        /// <summary>24 прийоми каталогу <c>career_moves.json</c> → наявні кліпи (центр рингу).</summary>
        private static readonly Dictionary<string, MoveClips> Moves = new Dictionary<string, MoveClips>
        {
            { "forearm",         new MoveClips("Forearm_Strike", "Center_ForearmShiver", StageCam.Close) },
            { "low_kick",        new MoveClips("Center_BigBoot", "Center_BigBoot_Stagger", StageCam.Close, null, false, "бракує кліпа лоу-кіка — біґ-бут") },
            { "whip",            new MoveClips("Center_Clothesline", "Center_Clothesline_Flip", StageCam.Big, "Rope_Back_R_Fast", false, "відскок від канатів склейкою → ларіат у центрі") },
            { "corner_drag",     new MoveClips("Chain_Headlock_Punches", "Chain_FrontFacelock_Clubs", StageCam.Close, null, false, "серія ударів у центрі") },
            { "shoulder_charge", new MoveClips("Center_ShoulderBlock", "Center_ShoulderBlock_Heavy", StageCam.Big) },
            { "dropkick",        new MoveClips("Center_RunningKnee", "Center_BigBoot_Stagger", StageCam.Big, null, false, "бракує дропкіка — удар коліном") },
            { "spotlight_blow",  new MoveClips("Center_RunningElbow", "Center_Lariat_Left", StageCam.Big) },
            { "hip_toss",        new MoveClips("Bodyslam", "Bodyslam", StageCam.Big, null, false, "бракує кидка через стегно — боді-слем") },
            { "lift_slam",       new MoveClips("Bodyslam", "Bodyslam", StageCam.Big) },
            { "snap_suplex",     new MoveClips("Bodyslam", "Bodyslam", StageCam.Big, null, false, "бракує суплекса — боді-слем") },
            { "bulldozer",       new MoveClips("Bodyslam", "Bodyslam", StageCam.Big, null, false, "бракує бульдозера — боді-слем") },
            { "springboard",     new MoveClips("Center_ShoulderTackle", "Center_ShoulderTackle", StageCam.Big, "Rope_Back_L", false, "з канатів склейкою → таран у центрі; бракує стрибка з каната") },
            { "swallow_dive",    new MoveClips("Center_ShoulderTackle", "Center_ShoulderTackle", StageCam.Big, "Move_Corner_Exit", false, "з кута склейкою → таран у центрі; бракує стрибка з третього каната") },
            { "armbar",          new MoveClips("Chain_ArmWringer", "Chain_ArmWringer_Kneel", StageCam.Close) },
            { "leg_lock",        new MoveClips("Chain_Hammerlock", "Chain_Hammerlock_Lift", StageCam.Close, null, false, "бракує замка ноги — гамерлок") },
            { "spider_lock",     new MoveClips("Chain_Wristlock", "Chain_Wristlock_Spin", StageCam.Close) },
            { "strut",           new MoveClips("Idle_Hype_Loop", "Idle_Hype_Loop", StageCam.Close, null, true) },
            { "crowd_pump",      new MoveClips("Idle_Hype_Loop", "Idle_Hype_Loop", StageCam.Close, null, true) },
            { "cheap_shot",      new MoveClips("Center_Headbutt", "Center_Headbutt", StageCam.Close) },
            { "eye_poke",        new MoveClips("Center_Headbutt", "Center_Headbutt", StageCam.Close, null, false, "бракує тицяння в очі — удар головою") },
            { "crusher",         new MoveClips("Center_Lariat_Heavy", "Center_Lariat_Heavy", StageCam.Big, null, false, "бракує підписного фінішера — важкий ларіат") },
            { "meteor",          new MoveClips("Center_ShoulderTackle", "Center_ShoulderTackle", StageCam.Big, "Move_Corner_Exit", false, "з кута склейкою → таран у центрі; бракує стрибка з кута") },
            { "iron_clamp",      new MoveClips("Chain_Headlock_Crank", "Chain_Headlock_Crank", StageCam.Close, null, false, "бракує больового фінішера — затиск голови") },
            { "grand_finale",    new MoveClips("Center_Clothesline_Flip", "Center_Clothesline_Flip", StageCam.Big) },
        };

        /// <summary>Чи знає таблиця цей прийом (охоронець: усі прийоми каталогу мають кліпи).</summary>
        public static bool KnowsMove(string key) { return key != null && Moves.ContainsKey(key); }

        /// <summary>План «на канаті/куті» за замовчуванням, якщо прийом зі склейкою, а свого плану в таблиці не має.</summary>
        public static string DefaultPerch(MoveStaging s) { return s == MoveStaging.Corner ? "Move_Corner_Exit" : "Rope_Back_R_Fast"; }

        public static List<StageStep> Plan(MatchEvent ev)
        {
            var list = new List<StageStep>();
            MatchSide a = ev.Actor, b = Opp(a);
            RollTier t = ev.Tier;
            switch (ev.Kind)
            {
                case MatchEventKind.Action:
                    PlanAction(list, ev, a, b, t);
                    break;
                case MatchEventKind.Reverse:
                    // Реверс: захисник (Actor) відповідає. 7-9 — обмін захватами (і знову Обмін ініціативи), 10+ — кидає нападника.
                    if (t == RollTier.Strong) list.Add(StageStep.Pair(a, "Bodyslam", StageCam.Big, null, "реверс: кидок"));
                    else if (t == RollTier.Partial) list.Add(StageStep.Pair(a, "Lockup_Exchange", StageCam.Close, null, "реверс: обмін захватами"));
                    break;
                case MatchEventKind.Kickout:
                    // Утримання: нападник накриває захисника (Actor події), рефері рахує. Провал — повні три.
                    list.Add(StageStep.Pair(b, "Cover", StageCam.Big, t == RollTier.Miss ? "Ref_Count_Pin_3" : "Ref_Count_Pin_2",
                        "утримання (Cover + відлік рефері)"));
                    break;
                case MatchEventKind.Endure:
                    list.Add(new StageStep { Kind = StageStepKind.Ref, RefClip = "Ref_Ask_Submit_Loop", Hold = 1.6f, Cam = StageCam.Close, Once = true,
                        Note = "рефері питає про здачу" });
                    break;
                case MatchEventKind.Rescue:
                    list.Add(new StageStep { Kind = StageStepKind.Ref, RefClip = "Ref_Flinch", Hold = 1.3f, Cam = StageCam.Overview, Note = "напарник відволікає рефері" });
                    break;
                case MatchEventKind.MatchEnd:
                    list.Add(new StageStep { Kind = StageStepKind.End, Cam = StageCam.Big, Note = "кінець: рефері піднімає руку" });
                    break;
                // Exchange (захват у центрі грає режисер між діями), TurnEnd (ініціатива переходить), Flourish (QTE) — кроків не мають.
            }
            // Попередження борцеві — рефері реагує окремим кроком.
            if (ev.Kind == MatchEventKind.Action)
            {
                for (int i = 0; i < ev.Effects.Count; i++)
                {
                    if (ev.Effects[i].Kind != MatchEffectKind.Warning) continue;
                    list.Add(new StageStep { Kind = StageStepKind.Ref, Actor = ev.Effects[i].Who, RefFacesActor = true, RefClip = "Ref_Warn", Cam = StageCam.Close, Note = "попередження", Aside = true });
                    break;
                }
            }
            return list;
        }

        private static bool StoodUp(MatchEvent ev, MatchSide b)
        {
            for (int i = 0; i < ev.Effects.Count; i++)
                if (ev.Effects[i].Kind == MatchEffectKind.Stand && ev.Effects[i].Who == b) return true;
            return false;
        }

        private static void PlanAction(List<StageStep> list, MatchEvent ev, MatchSide a, MatchSide b, RollTier t)
        {
            // Промінь надії не підводить цілком (ядро, MatchEngine.ApplyRolled): провал рахується як 7–9 — суперник лягає,
            // нападник не падає. Тож і кліп провалу — той самий, що 7–9, а не «Bump_Back» (звірка зі звуком, F2_DIRECTOR п. 1).
            if (ev.Action == MatchActionId.HopeSpot && t == RollTier.Miss) t = RollTier.Partial;
            bool miss = t == RollTier.Miss, strong = t == RollTier.Strong;
            // за рингом склейок «канати/кут» немає: прийом грається там, де пара (план «за рингом» ставить режисер)
            bool cut = ev.Staging != MoveStaging.Center && !ev.Outside;

            MoveClips mc;
            if (ev.MoveKey != null && Moves.TryGetValue(ev.MoveKey, out mc) && ev.Action != MatchActionId.Pin && ev.Action != MatchActionId.GetUp)
            {
                if (cut) list.Add(StageStep.Cut(a, ev.Staging, mc.Perch ?? DefaultPerch(ev.Staging), mc.Note));
                if (mc.Solo)
                {
                    list.Add(StageStep.Solo(a, miss ? "Idle_Hurt_Body_Loop" : mc.Partial, StageCam.Close, true, mc.Note));
                    if (StoodUp(ev, b)) { var up = StageStep.Solo(b, "GetUp_Back", StageCam.Overview); up.Aside = true; list.Add(up); }
                }
                else if (miss) AddMiss(list, ev.Action, a, cut);
                else list.Add(StageStep.Pair(a, strong ? mc.Strong : mc.Partial, strong ? StageCam.Big : mc.Cam, null, cut ? null : mc.Note));
                return;
            }

            switch (ev.Action)
            {
                case MatchActionId.Strike:
                    if (cut) list.Add(StageStep.Cut(a, ev.Staging, DefaultPerch(ev.Staging)));
                    list.Add(StageStep.Pair(a, miss ? "Center_Sidestep" : strong ? "Center_BigBoot_Stagger" : "Forearm_Strike",
                        strong ? StageCam.Big : StageCam.Close, null, miss ? "промах: суперник ухиляється" : null));
                    break;
                case MatchActionId.Throw:
                    if (miss) list.Add(StageStep.Solo(a, "Bump_Back", StageCam.Close, false, "кидок не вдався: падає сам"));
                    else list.Add(StageStep.Pair(a, "Bodyslam", StageCam.Big, null, ev.Outside ? "бракує кидка в бар'єр (набір «за рингом») — боді-слем" : null));
                    break;
                case MatchActionId.LeaveRing:
                    // Вихід за ринг — без бігу й перелазу: поштовх у центрі, склейка — пара вже на підлозі біля бар'єра.
                    if (miss) { list.Add(StageStep.Pair(a, "Lockup_Stalemate", StageCam.Close, null, "не викинув: суперник утримався")); break; }
                    list.Add(StageStep.Pair(a, "Center_ShoulderBlock", StageCam.Close, null, "бракує кидка через канати — поштовх"));
                    list.Add(StageStep.Place(a, true, 0.6f, "склейка: пара за рингом"));
                    break;
                case MatchActionId.ReturnToRing:
                    list.Add(StageStep.Place(a, false, 0.8f, "склейка: пара знову на рингу; бракує закочування (набір «за рингом»)"));
                    break;
                case MatchActionId.Weapon:
                    if (miss) list.Add(StageStep.Solo(a, "Bump_Back", StageCam.Close, false, "предмет не влучив"));
                    else list.Add(StageStep.Pair(a, strong ? "Center_Lariat_Heavy" : "Center_Headbutt", StageCam.Big, null,
                        strong ? "бракує удару стільцем або кидка на стіл — режисер чергує (набір «за рингом»)"
                               : "бракує удару стільцем по дотичній, нападник зачепив себе (набір «за рингом»)"));
                    break;
                case MatchActionId.TopRope:
                    // Ризикований трюк: склейка — нападник на куті (план 1), далі приземлення в центрі (план 2). Без бігу до кута.
                    list.Add(StageStep.Cut(a, cut ? ev.Staging : MoveStaging.Corner, DefaultPerch(cut ? ev.Staging : MoveStaging.Corner),
                        "бракує кліпа «з третього каната» — бриф Ф4"));
                    if (miss) AddMiss(list, ev.Action, a, true);
                    else list.Add(StageStep.Pair(a, "Center_ShoulderTackle", StageCam.Big, null, "летючий — найближчий таран"));
                    break;
                case MatchActionId.Taunt:
                    list.Add(StageStep.Solo(a, miss ? "Idle_Hurt_Body_Loop" : "Idle_Hype_Loop", StageCam.Close, true));
                    if (StoodUp(ev, b)) { var up = StageStep.Solo(b, "GetUp_Back", StageCam.Overview); up.Aside = true; list.Add(up); }
                    break;
                case MatchActionId.Dirty:
                    list.Add(StageStep.Pair(a, miss ? "Center_Sidestep" : "Center_Headbutt", StageCam.Close, null, "брудний прийом: удар головою"));
                    break;
                case MatchActionId.Submission:
                    list.Add(StageStep.Pair(a, miss ? "Lockup_Stalemate" : strong ? "Chain_Headlock_Crank" : "Chain_Headlock", StageCam.Close,
                        null, "бракує больового з реальною підсадкою"));
                    break;
                case MatchActionId.Finisher:
                    if (cut) list.Add(StageStep.Cut(a, ev.Staging, DefaultPerch(ev.Staging)));
                    if (miss) list.Add(StageStep.Solo(a, "Bump_Back", StageCam.Big, false, "фінішер не вдався"));
                    else list.Add(StageStep.Pair(a, "Center_Lariat_Heavy", StageCam.Big, null, "бракує підписного фінішера — важкий ларіат"));
                    break;
                case MatchActionId.Desperate:
                case MatchActionId.HopeSpot:
                    if (miss) list.Add(StageStep.Solo(a, "Bump_Back", StageCam.Close));
                    else list.Add(StageStep.Pair(a, strong ? "Center_ShoulderBlock_Heavy" : "Center_ShoulderBlock", StageCam.Big, null, "відчайдушний — важкий таран"));
                    break;
                case MatchActionId.GetUp:
                    list.Add(miss ? StageStep.Solo(a, "Idle_Tired_Loop", StageCam.Overview, true) : StageStep.Solo(a, "GetUp_Back", StageCam.Overview));
                    break;
                case MatchActionId.Pin:
                    break; // сам відлік — у події Kickout, що йде слідом
                case MatchActionId.Recover:
                case MatchActionId.CatchBreath:
                    list.Add(StageStep.Solo(a, "Idle_Tired_Loop", StageCam.Overview, true, ev.Action == MatchActionId.CatchBreath ? "переводить подих" : null));
                    break;
                case MatchActionId.Ally:
                    list.Add(new StageStep { Kind = StageStepKind.Ref, RefClip = "Ref_Point_Ropes", Hold = 1.4f, Cam = StageCam.Overview, Note = "бракує виходу напарника" });
                    break;
            }
        }

        /// <summary>Промах прийому з таблиці: удар — суперник ухиляється, решта (кидок, стрибок, фінішер) — нападник падає сам.</summary>
        private static void AddMiss(List<StageStep> list, MatchActionId action, MatchSide a, bool fromCut)
        {
            switch (action)
            {
                case MatchActionId.Strike:
                case MatchActionId.Dirty:
                    list.Add(StageStep.Pair(a, "Center_Sidestep", StageCam.Close, null, "промах: суперник ухиляється"));
                    break;
                case MatchActionId.Submission:
                    list.Add(StageStep.Pair(a, "Lockup_Stalemate", StageCam.Close, null, "больовий не вдався: нічия в захваті"));
                    break;
                default:
                    list.Add(StageStep.Solo(a, "Bump_Back", fromCut ? StageCam.Big : StageCam.Close, false, "прийом не вдався: падає сам"));
                    break;
            }
        }
    }
}
