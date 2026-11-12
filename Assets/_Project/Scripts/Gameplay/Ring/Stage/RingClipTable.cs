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
        /// Склейка (план 1 з двох): на канаті чи куті (<see cref="StageStep.Perch"/>) — <see cref="StageStep.Actor"/>: нападник
        /// (стрибок, таран) або жертва (кидок у канати чи кут, <c>ANIM_BRIEF_COMBAT_CENTER.md</c> §5.1); суперник стоїть у
        /// центрі. Далі — жорстка склейка на наступний крок у центрі. Ходьби між планами немає: режисер ставить на місце
        /// одразу. Такту не несе — хіба що удар спиною в канат чи кут і є ударом події (<see cref="StageStep.Impact"/>).
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
        /// <summary>Хто виконує (A парного кліпу, сольний борець, той, хто на канаті чи куті склейки).</summary>
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
        /// <summary>Склейка: де той, хто на плані 1, — на канатах чи на куті.</summary>
        public MoveStaging Perch;
        /// <summary>Крок-наслідок, а не сама дія події (суперник підвівся, рефері попереджає): такт на ньому не ставиться.</summary>
        public bool Aside;
        /// <summary>Склейка місця: true — пара тепер за рингом, false — знову на рингу.</summary>
        public bool Outside;

        /// <summary>
        /// Пара продовжує попередній крок того самого ланцюга (накриття → утримання → кікаут, больовий, приземлення після
        /// стрибка): без переходу й перестановки — слоти борців лишаються, кадр той самий. Вузол на стику однаковий
        /// (<c>ANIM_CENTER_CLIPS.md</c>); якщо зсув пари інший (стрибок із каната → утримання), A переставляється на різницю
        /// зсувів, B лишається.
        /// </summary>
        public bool Chain;

        /// <summary>
        /// Соло в просторі A пари з цією базою (стрибок «Метеора», «Ластівки»): кліп грається в тому слоті, який матиме A цієї
        /// пари на старті; кінцева поза соло — перша поза пари. Наступний крок — сама пара з <see cref="Chain"/>.
        /// </summary>
        public string AnchorPair;

        /// <summary>Пара: крок закінчується на цій мітці свого кліпа (кидок у канати — до <c>release</c>), а не наприкінці кліпа.</summary>
        public string StopAt;

        /// <summary>Пара: скільки тримати петлю, с часу кліпа (0 — кліп цілком).</summary>
        public float Dur;

        /// <summary>Склейка: кліп стартує біля цієї мітки (спина в канат — від <c>rope_hit</c>, кут — від <c>corner_hit</c>).</summary>
        public string From;

        /// <summary>Склейка: план тримається до цієї мітки (виштовх — <c>rope_release</c>), інакше до кінця кліпа, не довше 1,7 с.</summary>
        public string To;

        /// <summary>Склейка несе такт: удар спиною в канат чи кут — і є ударом події (кидок у кут, 7–9 кидка в канати).</summary>
        public bool Impact;

        /// <summary>
        /// Склейка: приземлення йде на лежачого на спині. Суперник, що стоїть, падає під час плану 1; той, що лежить обличчям
        /// донизу, під склейкою кадру стає на спину на тому самому місці (кліпа перевороту немає).
        /// </summary>
        public bool DropVictim;

        /// <summary>
        /// Пара (чи соло) заходить склейкою кадру: кліп починається з пози, якої в борця зараз немає — вхід больового на лікоть
        /// з будь-якої пози; кліп «на спині» (накриття, захват ноги, підйом), коли борець лежить обличчям донизу.
        /// </summary>
        public bool CutIn;

        /// <summary>Вузол «лежить обличчям донизу» (маніфест): кліпи, що починаються на спині, заходять на нього лише склейкою.</summary>
        public const string LyingFront = "lying_front";

        /// <summary>
        /// Пара ставиться за коренями кліпів (зсув маніфесту — між коренями A і B, <c>ring_anims.py</c>, PAIR_MID_RULE), а не за
        /// тазом першого кадру: у кліпах із розбігом від канатів чи стрибком (<c>Counter_*</c>, <c>Charge_*</c>,
        /// <c>Springboard_Air*</c>, <c>Armbar_Enter</c>) таз на старті далеко від кореня. Лише після склейки — жорстко.
        /// </summary>
        public bool ByRoots;

        /// <summary>
        /// Утримання: з цього кроку рефері йде до плечей захисника, стає на коліно (<c>Ref_Kneel_Down</c>) і рахує
        /// <see cref="RefClip"/>. Рахунок не ріжеться: кліп обрано за ступенем ядра наперед (<c>F2_DIRECTOR.md</c>, «Накриття P1-1»).
        /// </summary>
        public bool Count;

        /// <summary>
        /// Крок триває до мітки кліпа відліку (від старту рахунку) мінус мітка <see cref="UntilLead"/> кліпа наступного кроку
        /// плюс <see cref="UntilPause"/>: кікаут борців — рівно на мітці <c>kickout</c> рефері.
        /// </summary>
        public string UntilCount;
        public string UntilLead;
        public float UntilPause;

        /// <summary>Мітка підстановки в <see cref="Note"/>: «бракує …» — кліпа, якого потребує план, ще немає, грається найближчий наявний.</summary>
        public const string StandInMark = "бракує";

        /// <summary>
        /// Крок грає підстановкою (критерій М1 «підстановок кліпів 0»; до кліпів — лічильник). Окремо від відсутнього файлу кліпа:
        /// прохід 3 «Скилів» показав «підстановок 0», хоча всі фінішери грали важкий ларіат — рахувались лише відсутні файли.
        /// </summary>
        public bool IsStandIn { get { return Note != null && Note.IndexOf(StandInMark, System.StringComparison.Ordinal) >= 0; } }

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
            if (AnchorPair != null) { yield return AnchorPair + "_A"; yield return AnchorPair + "_B"; }
            if (RefClip != null) yield return RefClip;
        }
    }

    /// <summary>
    /// Що режисер знає поза подією (дзеркало показу, черга): таблиця лишається чистою функцією, а тест подає ці дані сам.
    /// </summary>
    public struct PlanContext
    {
        /// <summary>Поза, яку з минулої події тримає <see cref="HeldBy"/>: <see cref="RingClipTable.HeldPin"/>, <c>leglock</c>, <c>armbar</c> або null.</summary>
        public string Held;
        public MatchSide HeldBy;
        /// <summary>Остання показана дія з кидком (крім самого утримання): чи накриття — кульмінація (фінішер, підписний).</summary>
        public MatchEvent LastMove;
        /// <summary>Наступна подія черги (напарник розриває рахунок — <see cref="MatchEventKind.Rescue"/> слідом за «три»).</summary>
        public MatchEvent Next;
        /// <summary>Суперник виконавця лежить (дзеркало до події).</summary>
        public bool VictimDown;
        /// <summary>Кінцевий вузол поточного кліпа виконавця (<c>kneel_up</c> — встає з колін, а не з лежання).</summary>
        public string ActorNode;
        /// <summary>Кінцевий вузол поточного кліпа суперника (<c>lying_front</c> — больовий на ногу заходить склейкою).</summary>
        public string VictimNode;
    }

    /// <summary>
    /// Таблиця «подія матчу → кроки режисури» (TRPG_DESIGN §4.12): прийом кар'єри (<see cref="MatchEvent.MoveKey"/>) і ступінь кидка
    /// перекладає на кліпи рингу; без ключа — запасний вибір за дією. Усе грається в центрі рингу; постановка «канати» чи «кут»
    /// (<see cref="MatchEvent.Staging"/>) додає склейку (<see cref="StageStepKind.Cut"/>). Чиста функція; пози режисер веде окремо з
    /// ефектів подій. Кліпи P1–P2 «Кліпів» — <c>docs/ring/ANIM_CENTER_CLIPS.md</c>; прийоми без власного кліпа взято найближчим
    /// наявним — див. Note і бриф <c>docs/ring/ANIM_BRIEF_COMBAT_CENTER.md</c>.
    /// </summary>
    public static class RingClipTable
    {
        public static MatchSide Opp(MatchSide s) { return s == MatchSide.Player ? MatchSide.Rival : MatchSide.Player; }

        /// <summary>Поза накриття (вузли <c>pinned_A</c> / <c>pinned_B</c>).</summary>
        public const string HeldPin = "pinned";
        public const string HeldLegLock = "leglock";
        public const string HeldArmbar = "armbar";

        /// <summary>З колін у стійку (вузол <c>kneel_up</c> → <c>stance</c>).</summary>
        public const string KneelUp = "Kneel_To_Stance";

        /// <summary>
        /// План «на куті» для стрибків «Метеор» і «Ластівка». <c>Top_Rope_Perch_Loop</c> («ANIM_CENTER_CLIPS.md», P1-3) лежить у
        /// наборі постатей <c>RING_Cast_*.fbx</c>, якого сцена рингу не вантажить і якого немає в маніфесті рингу, тож до нього —
        /// кут <c>Move_Corner_Exit</c> (запит «Кліпам» і «Изучение» — <c>CLIP_NEEDS.md</c>).
        /// </summary>
        public const string CornerTopPerch = "Move_Corner_Exit";
        private const string CornerTopNote = "бракує Top_Rope_Perch_Loop у наборі рингу — план на куті знизу";

        /// <summary>Кліпи прийому кар'єри: частковий успіх (7–9), чистий (10+) і план «на канаті/куті», якщо прийом зі склейкою.</summary>
        private struct MoveClips
        {
            public string Partial, Strong, Perch;
            public StageCam Cam;
            public bool Solo;
            public string Note;
            /// <summary>Підписний прийом (<c>career_moves.json</c>, kind signature): утримання після нього — кульмінація.</summary>
            public bool Climax;
            public MoveClips(string partial, string strong, StageCam cam, string perch = null, bool solo = false, string note = null, bool climax = false)
            { Partial = partial; Strong = strong; Cam = cam; Perch = perch; Solo = solo; Note = note; Climax = climax; }
        }

        /// <summary>
        /// 24 прийоми каталогу <c>career_moves.json</c> → наявні кліпи (центр рингу). Прийоми з власним ланцюгом кліпів P1–P2
        /// (больові, стрибки, канати, кут, таран) план будує окремо (<see cref="PlanMove"/>); тут для них лише «є в таблиці» й
        /// ознака підписного.
        /// </summary>
        private static readonly Dictionary<string, MoveClips> Moves = new Dictionary<string, MoveClips>
        {
            { "forearm",         new MoveClips("Forearm_Strike", "Center_ForearmShiver", StageCam.Close) },
            { "low_kick",        new MoveClips("Center_BigBoot", "Center_BigBoot_Stagger", StageCam.Close, null, false, "бракує кліпа лоу-кіка — біґ-бут") },
            { "whip",            new MoveClips(null, null, StageCam.Big) },
            { "corner_drag",     new MoveClips(null, null, StageCam.Big) },
            { "shoulder_charge", new MoveClips(null, null, StageCam.Big) },
            { "dropkick",        new MoveClips("Center_RunningKnee", "Center_BigBoot_Stagger", StageCam.Big, null, false, "бракує дропкіка — удар коліном") },
            { "spotlight_blow",  new MoveClips("Center_RunningElbow", "Center_Lariat_Left", StageCam.Big, null, false, null, true) },
            { "hip_toss",        new MoveClips("Bodyslam", "Bodyslam", StageCam.Big, null, false, "бракує кидка через стегно — боді-слем") },
            { "lift_slam",       new MoveClips("Bodyslam", "Bodyslam", StageCam.Big) },
            { "snap_suplex",     new MoveClips("Bodyslam", "Bodyslam", StageCam.Big, null, false, "бракує суплекса — боді-слем") },
            { "bulldozer",       new MoveClips("Bodyslam", "Bodyslam", StageCam.Big, null, false, "бракує бульдозера — боді-слем", true) },
            { "springboard",     new MoveClips(null, null, StageCam.Big) },
            { "swallow_dive",    new MoveClips(null, null, StageCam.Big, null, false, null, true) },
            { "armbar",          new MoveClips(null, null, StageCam.Close) },
            { "leg_lock",        new MoveClips(null, null, StageCam.Close) },
            { "spider_lock",     new MoveClips(null, null, StageCam.Close, null, false, null, true) },
            { "strut",           new MoveClips("Idle_Hype_Loop", "Idle_Hype_Loop", StageCam.Close, null, true) },
            { "crowd_pump",      new MoveClips("Idle_Hype_Loop", "Idle_Hype_Loop", StageCam.Close, null, true) },
            { "cheap_shot",      new MoveClips("Center_Headbutt", "Center_Headbutt", StageCam.Close) },
            { "eye_poke",        new MoveClips("Center_Headbutt", "Center_Headbutt", StageCam.Close, null, false, "бракує тицяння в очі — удар головою") },
            { "crusher",         new MoveClips("Center_Lariat_Heavy", "Center_Lariat_Heavy", StageCam.Big, null, false, "бракує підписного фінішера — важкий ларіат") },
            { "meteor",          new MoveClips(null, null, StageCam.Big) },
            { "iron_clamp",      new MoveClips("Chain_Headlock_Crank", "Chain_Headlock_Crank", StageCam.Close, null, false, "бракує больового фінішера — затиск голови") },
            { "grand_finale",    new MoveClips("Center_Clothesline_Flip", "Center_Clothesline_Flip", StageCam.Big) },
        };

        /// <summary>Чи знає таблиця цей прийом (охоронець: усі прийоми каталогу мають кліпи).</summary>
        public static bool KnowsMove(string key) { return key != null && Moves.ContainsKey(key); }

        /// <summary>Підписний прийом за таблицею (охоронець звіряє з kind каталогу).</summary>
        public static bool IsSignature(string key)
        {
            MoveClips mc;
            return key != null && Moves.TryGetValue(key, out mc) && mc.Climax;
        }

        /// <summary>Скільки кроків плану грають підстановкою (<see cref="StageStep.IsStandIn"/>): режисер додає до лічильника матчу.</summary>
        public static int StandIns(List<StageStep> plan)
        {
            int n = 0;
            for (int i = 0; i < plan.Count; i++) if (plan[i].IsStandIn) n++;
            return n;
        }

        /// <summary>План «на канаті/куті» за замовчуванням, якщо прийом зі склейкою, а свого плану в таблиці не має.</summary>
        public static string DefaultPerch(MoveStaging s) { return s == MoveStaging.Corner ? "Move_Corner_Exit" : "Rope_Back_R_Fast"; }

        public static List<StageStep> Plan(MatchEvent ev) { return Plan(ev, default(PlanContext)); }

        public static List<StageStep> Plan(MatchEvent ev, PlanContext ctx)
        {
            var list = new List<StageStep>();
            MatchSide a = ev.Actor, b = Opp(a);
            RollTier t = ev.Tier;
            switch (ev.Kind)
            {
                case MatchEventKind.Action:
                    PlanAction(list, ev, a, b, t, ctx);
                    break;
                case MatchEventKind.Reverse:
                    // Реверс: захисник (Actor) відповідає. 7-9 — обмін захватами (і знову Обмін ініціативи), 10+ — кидає нападника.
                    if (t == RollTier.Strong) list.Add(StageStep.Pair(a, "Bodyslam", StageCam.Big, null, "реверс: кидок"));
                    else if (t == RollTier.Partial) list.Add(StageStep.Pair(a, "Lockup_Exchange", StageCam.Close, null, "реверс: обмін захватами"));
                    break;
                case MatchEventKind.Kickout:
                    PlanKickout(list, ev, ctx);
                    break;
                case MatchEventKind.Endure:
                    PlanEndure(list, ev, ctx);
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

        // ============================ утримання (P1-1) ============================

        /// <summary>
        /// Утримання (подія Kickout, Actor — захисник): рахунок рефері за ступенем ядра обрано наперед і не ріжеться
        /// (<c>F2_DIRECTOR.md</c>, «Накриття P1-1»): 10+ — на «раз», 7–9 — на «два», 7–9 після фінішера чи підписного — «два з
        /// половиною», провал — «три», підкуп — рефері зупиняється на «два», напарник розриває рахунок — завмирає над матом.
        /// Кікаут борців стартує з петлі так, що його мітка kickout — рівно на мітці kickout рефері.
        /// </summary>
        private static void PlanKickout(List<StageStep> list, MatchEvent ev, PlanContext ctx)
        {
            MatchSide d = ev.Actor, a = Opp(d);
            bool bribe = ev.Perk == PerkCatalog.BribedRef;
            bool rescue = ev.Tier == RollTier.Miss && ctx.Next != null && ctx.Next.Kind == MatchEventKind.Rescue;
            bool climax = IsClimax(ctx.LastMove, a);
            string count, kick = null, until, lead = null;
            float pause = 0f;
            bool disbelief = false;
            switch (ev.Tier)
            {
                case RollTier.Strong:
                    count = "Ref_Count_Pin_1_Slow"; kick = "Cover_Kickout_1";
                    break;
                case RollTier.Partial:
                    if (bribe) { count = "Ref_Count_Pin_2_Slow"; disbelief = true; }
                    else if (climax) { count = "Ref_Count_Pin_2_Half"; kick = "Cover_Kickout_2_Half"; disbelief = true; }
                    else { count = "Ref_Count_Pin_2_Slow"; kick = "Cover_Kickout_2"; }
                    break;
                default:
                    count = rescue ? "Ref_Count_Pin_2_Half" : "Ref_Count_Pin_3_Slow";
                    break;
            }
            if (kick != null) { until = "kickout"; lead = "kickout"; }
            else if (count == "Ref_Count_Pin_3_Slow") { until = "count_3"; pause = 0.35f; }   // долоня на маті, далі нападник відпускає
            else until = "kickout";                                                          // рефері зупинився: «два» чи завмер над матом

            bool pinned = ctx.Held == HeldPin && ctx.HeldBy == a;
            // Захисник (Actor події) лежить обличчям донизу (фліп, вихід із больового на лікоть), а накриття — на спину: склейкою.
            if (!pinned)
                list.Add(new StageStep { Kind = StageStepKind.Pair, Actor = a, Clip = "Cover_Enter", Cam = StageCam.Big, RefClip = count, Count = true,
                    CutIn = ctx.ActorNode == StageStep.LyingFront, Note = "утримання: накриття, рефері на коліні рахує" });
            var hold = new StageStep { Kind = StageStepKind.Pair, Actor = a, Clip = "Cover_Hold_Loop", Cam = StageCam.Big, Chain = true,
                UntilCount = until, UntilLead = lead, UntilPause = pause };
            if (pinned) { hold.RefClip = count; hold.Count = true; hold.Note = "утримання з приземлення: рефері на коліні рахує"; }
            list.Add(hold);
            list.Add(new StageStep { Kind = StageStepKind.Pair, Actor = a, Clip = kick ?? "Cover_Release", Cam = StageCam.Big, Chain = true });
            if (disbelief) list.Add(new StageStep { Kind = StageStepKind.Solo, Actor = a, Clip = "Kneel_Disbelief", Cam = StageCam.Close, Once = true, Aside = true });
            list.Add(new StageStep { Kind = StageStepKind.Solo, Actor = a, Clip = KneelUp, Cam = StageCam.Overview, Aside = true });
        }

        /// <summary>Накриття — кульмінація: перед ним фінішер або підписний прийом нападника, що вдався.</summary>
        public static bool IsClimax(MatchEvent last, MatchSide attacker)
        {
            if (last == null || last.Kind != MatchEventKind.Action || last.Actor != attacker || last.Tier == RollTier.Miss) return false;
            return last.Action == MatchActionId.Finisher || IsSignature(last.MoveKey);
        }

        /// <summary>
        /// Мить кліпа відліку (від старту рахунку), на якій кінчається крок <paramref name="hold"/>: мітка
        /// <see cref="StageStep.UntilCount"/> рефері мінус мітка <see cref="StageStep.UntilLead"/> кліпа наступного кроку плюс пауза.
        /// −1 — мітки немає. Режисер і тест рахують однаково.
        /// </summary>
        public static float CountUntil(StageStep hold, StageStep next, string countClip, RingClipEvents ev)
        {
            if (hold == null || hold.UntilCount == null || ev == null) return -1f;
            float t = ev.Time(countClip, hold.UntilCount);
            if (t < 0f) return -1f;
            if (hold.UntilLead != null && next != null)
            {
                float l = ev.Time(RingBeats.MarkBase(next.Clip), hold.UntilLead);
                if (l > 0f) t -= l;
            }
            return t < 0f ? 0f : t + hold.UntilPause;
        }

        // ============================ больовий (P1-2) ============================

        /// <summary>Родина больового за прийомом: на лікоть — armbar, решта (на ногу, «павук», без ключа) — на ногу.</summary>
        public static string HoldFamily(string moveKey) { return moveKey == "armbar" ? "Armbar" : "LegLock"; }

        private static string HeldOf(string family) { return family == "Armbar" ? HeldArmbar : HeldLegLock; }

        /// <summary>
        /// Дія «больовий» (ядро: суперник лежить завжди). Провал — не взяв (<c>LegLock_Enter_Fail</c>: обидва лишаються, як
        /// каже ядро; варіант на лікоть кінчається обома в стійці, а ядро кладе жертву — тож і для ліктя той самий кліп).
        /// Інакше — вхід у захват; утримання й розв'язку показує подія Endure слідом.
        /// </summary>
        private static void AddSubmissionEnter(List<StageStep> list, MatchEvent ev, MatchSide a, PlanContext ctx)
        {
            string k = HoldFamily(ev.MoveKey);
            if (ev.Tier == RollTier.Miss)
            {
                list.Add(new StageStep { Kind = StageStepKind.Pair, Actor = a, Clip = "LegLock_Enter_Fail", Cam = StageCam.Close,
                    CutIn = ctx.VictimNode == StageStep.LyingFront,
                    Note = k == "Armbar" ? "бракує невдачі больового на лікоть над лежачим (Armbar_Enter_Fail кінчається обома в стійці) — кліп невдачі на ногу" : null });
                list.Add(new StageStep { Kind = StageStepKind.Solo, Actor = a, Clip = KneelUp, Cam = StageCam.Overview, Aside = true });
                return;
            }
            // Вхід на лікоть — з будь-якої пози (у маніфесті «з вузла» —): склейкою кадру; на ногу — з лежачого на спині.
            list.Add(new StageStep { Kind = StageStepKind.Pair, Actor = a, Clip = k + "_Enter", Cam = StageCam.Close,
                CutIn = k == "Armbar" || ctx.VictimNode == StageStep.LyingFront, ByRoots = k == "Armbar" });
        }

        /// <summary>
        /// «Терпіти» (Actor — захисник): 10+ — вирвався (тримає неповно → вихід), 7–9 — тримається (дотиск → тягнеться → вихід),
        /// провал — здався (дотиск, після підписного — ефектний, → стукає по мату). Рефері питає про здачу поруч.
        /// </summary>
        private static void PlanEndure(List<StageStep> list, MatchEvent ev, PlanContext ctx)
        {
            MatchSide d = ev.Actor, a = Opp(d);
            string k = ctx.Held == HeldArmbar ? "Armbar" : ctx.Held == HeldLegLock ? "LegLock" : HoldFamily(ev.MoveKey);
            bool chain = ctx.Held == HeldOf(k) && ctx.HeldBy == a;
            const string ask = "Ref_Ask_Submit_Loop";
            switch (ev.Tier)
            {
                case RollTier.Strong:
                    list.Add(new StageStep { Kind = StageStepKind.Pair, Actor = a, Clip = k + "_Hold_Partial_Loop", Cam = StageCam.Close, RefClip = ask, Chain = chain, Dur = 1.6f, Aside = true,
                        Note = "больовий: тримає неповно" });
                    list.Add(new StageStep { Kind = StageStepKind.Pair, Actor = a, Clip = k + "_Escape", Cam = StageCam.Close, Chain = true });
                    break;
                case RollTier.Partial:
                    list.Add(new StageStep { Kind = StageStepKind.Pair, Actor = a, Clip = k + "_Crank", Cam = StageCam.Close, RefClip = ask, Chain = chain, Aside = true });
                    list.Add(new StageStep { Kind = StageStepKind.Pair, Actor = a, Clip = k + "_Reach", Cam = StageCam.Close, Chain = true });
                    list.Add(new StageStep { Kind = StageStepKind.Pair, Actor = a, Clip = k + "_Escape", Cam = StageCam.Close, Chain = true, Aside = true });
                    break;
                default:
                    list.Add(new StageStep { Kind = StageStepKind.Pair, Actor = a, Clip = IsSignature(ev.MoveKey) ? k + "_Crank_Hype" : k + "_Crank", Cam = StageCam.Close,
                        RefClip = ask, Chain = chain, Aside = true });
                    list.Add(new StageStep { Kind = StageStepKind.Pair, Actor = a, Clip = k + "_Tap", Cam = StageCam.Close, Chain = true });
                    break;
            }
            list.Add(new StageStep { Kind = StageStepKind.Solo, Actor = a, Clip = KneelUp, Cam = StageCam.Overview, Aside = true });
        }

        // ============================ поза, що тримається між подіями ============================

        /// <summary>Чи подія продовжує позу, яку тримає <paramref name="by"/>: накриття — утримання (чи сама дія «утримання»), больовий — «терпіти».</summary>
        public static bool Continues(MatchEvent ev, string held, MatchSide by)
        {
            if (held == null || ev == null) return false;
            if (held == HeldPin)
                return (ev.Kind == MatchEventKind.Kickout && ev.Actor == Opp(by))
                    || (ev.Kind == MatchEventKind.Action && ev.Action == MatchActionId.Pin && ev.Actor == by);
            return ev.Kind == MatchEventKind.Endure && ev.Actor == Opp(by);
        }

        /// <summary>Поза лишилась, а подія її не продовжує: нападник відпускає й встає (кроки-наслідки, такту не несуть).</summary>
        public static List<StageStep> Release(string held, MatchSide by)
        {
            var list = new List<StageStep>();
            if (held == null) return list;
            string clip = held == HeldPin ? "Cover_Release" : held == HeldArmbar ? "Armbar_Escape" : "LegLock_Escape";
            list.Add(new StageStep { Kind = StageStepKind.Pair, Actor = by, Clip = clip, Cam = StageCam.Overview, Chain = true, Aside = true, Note = "відпускає" });
            list.Add(new StageStep { Kind = StageStepKind.Solo, Actor = by, Clip = KneelUp, Cam = StageCam.Overview, Aside = true });
            return list;
        }

        /// <summary>Яку позу тримає пара наприкінці кліпа з таким кінцевим вузлом A (з маніфесту): накриття, больовий чи нічого.</summary>
        public static string HeldByNode(string endNodeA)
        {
            if (endNodeA == null) return null;
            if (endNodeA.StartsWith("pinned_", System.StringComparison.Ordinal)) return HeldPin;
            if (endNodeA.StartsWith("leglock_", System.StringComparison.Ordinal)) return HeldLegLock;
            if (endNodeA.StartsWith("armbar_", System.StringComparison.Ordinal)) return HeldArmbar;
            return null;
        }

        // ============================ дії ============================

        private static bool StoodUp(MatchEvent ev, MatchSide b)
        {
            for (int i = 0; i < ev.Effects.Count; i++)
                if (ev.Effects[i].Kind == MatchEffectKind.Stand && ev.Effects[i].Who == b) return true;
            return false;
        }

        /// <summary>
        /// Підйом борця з того вузла, де він лежить (<see cref="PlanContext.ActorNode"/> / <see cref="PlanContext.VictimNode"/>):
        /// з колін (кікаут, стрибок, що не вдався) — у стійку з колін; обличчям донизу — підйом зі спини склейкою кадру (кліпа
        /// підйому з живота немає, <c>CLIP_NEEDS.md</c> §9); інакше — підйом зі спини.
        /// </summary>
        /// <summary>
        /// Кінцевий вузол кліпа, з якого в стійку тільки кліпом підйому: лежить (<c>lying_*</c>), на колінах (<c>kneel_up</c>)
        /// чи під накриттям (<c>pinned_*</c>). null — вузол невідомий (стійка).
        /// </summary>
        public static bool StaysDown(string node)
        {
            return node != null && (node.StartsWith("lying", System.StringComparison.Ordinal) || node == "kneel_up"
                || node.StartsWith("pinned", System.StringComparison.Ordinal));
        }

        /// <summary>Розрив тазу, менший за це, ланцюг не лікує, м: дихання петлі, а не стрибок (ПЛЕЙСХОЛДЕР).</summary>
        public const float ChainGapMin = 0.08f;
        /// <summary>Розрив тазу, більший за це, — не продовження пози, а інша розстановка: лишається як є, м (ПЛЕЙСХОЛДЕР).</summary>
        public const float ChainGapMax = 1.5f;

        /// <summary>
        /// Безшовний ланцюг: наступний кліп стартує з тазом там, де борець є зараз. <paramref name="gap"/> — відстань від
        /// тазу в слоті першого кадру нового кліпа до тазу борця зараз (у площині ринґу). Повертає, чи зсувати слот на цей
        /// розрив: так — між <see cref="ChainGapMin"/> і <see cref="ChainGapMax"/>. Фаз-тест 8f: Cover_Release_A стрибав на
        /// 0,83 м, бо корінь слота лишався, а таз першого кадру відпускання лежить не там, де таз петлі утримання.
        /// </summary>
        public static bool ChainNeedsShift(float gap) { return gap >= ChainGapMin && gap <= ChainGapMax; }

        private static StageStep StandUp(MatchSide who, string node, bool aside)
        {
            var up = StageStep.Solo(who, node == "kneel_up" ? KneelUp : "GetUp_Back", StageCam.Overview);
            up.CutIn = node == StageStep.LyingFront;
            up.Aside = aside;
            return up;
        }

        private static void PlanAction(List<StageStep> list, MatchEvent ev, MatchSide a, MatchSide b, RollTier t, PlanContext ctx)
        {
            // Промінь надії не підводить цілком (ядро, MatchEngine.ApplyRolled): провал рахується як 7–9 — суперник лягає,
            // нападник не падає. Тож і кліп провалу — той самий, що 7–9, а не «Bump_Back» (звірка зі звуком, F2_DIRECTOR п. 1).
            if (ev.Action == MatchActionId.HopeSpot && t == RollTier.Miss) t = RollTier.Partial;
            bool miss = t == RollTier.Miss, strong = t == RollTier.Strong;
            // за рингом склейок «канати/кут» немає: прийом грається там, де пара (план «за рингом» ставить режисер)
            bool cut = ev.Staging != MoveStaging.Center && !ev.Outside;

            if (ev.MoveKey != null && ev.Action != MatchActionId.Pin && ev.Action != MatchActionId.GetUp && PlanMove(list, ev, a, b, t, ctx))
                return;

            MoveClips mc;
            if (ev.MoveKey != null && Moves.TryGetValue(ev.MoveKey, out mc) && mc.Partial != null && ev.Action != MatchActionId.Pin && ev.Action != MatchActionId.GetUp)
            {
                if (cut) list.Add(StageStep.Cut(a, ev.Staging, mc.Perch ?? DefaultPerch(ev.Staging), mc.Note));
                if (mc.Solo)
                {
                    list.Add(StageStep.Solo(a, miss ? "Idle_Hurt_Body_Loop" : mc.Partial, StageCam.Close, true, mc.Note));
                    if (StoodUp(ev, b)) list.Add(StandUp(b, ctx.VictimNode, true));
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
                    // За рингом кута й канатів немає: стрибок із фартуха — найближчий таран, до кліпів набору «за рингом».
                    if (ev.Outside)
                    {
                        if (miss) AddMiss(list, ev.Action, a, false);
                        else list.Add(StageStep.Pair(a, "Center_ShoulderTackle", StageCam.Big, null, "бракує стрибка з фартуха (набір «за рингом») — таран"));
                        break;
                    }
                    // Ризикований трюк без ключа прийому: з каната — стрибок через канат; інакше «Ластівка» з кута (план 1), далі
                    // пірнання й приземлення в центрі.
                    if (cut && ev.Staging == MoveStaging.Ropes) AddSpringboard(list, a, t, ctx);
                    else AddAerial(list, a, t, false, MoveStaging.Corner, ctx);
                    break;
                case MatchActionId.Taunt:
                    list.Add(StageStep.Solo(a, miss ? "Idle_Hurt_Body_Loop" : "Idle_Hype_Loop", StageCam.Close, true));
                    if (StoodUp(ev, b)) list.Add(StandUp(b, ctx.VictimNode, true));
                    break;
                case MatchActionId.Dirty:
                    list.Add(StageStep.Pair(a, miss ? "Center_Sidestep" : "Center_Headbutt", StageCam.Close, null, "брудний прийом: удар головою"));
                    break;
                case MatchActionId.Submission:
                    AddSubmissionEnter(list, ev, a, ctx);
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
                    // З колін (після кікауту, стрибка, що не вдався) — у стійку з колін, а не підйомом зі спини.
                    // Обличчям донизу — підйом зі спини склейкою кадру (кліпа підйому з живота немає, CLIP_NEEDS §9).
                    // Невдалий підйом: хто лежить чи стоїть на колінах — лишається там (кліпа спроби лежачого немає); «Idle_Tired»
                    // ставив його в стійку без підйому — телепорт після відліку (звіт 8f, кадри 05–06). Без кроків: поза не міняється.
                    if (miss) { if (!StaysDown(ctx.ActorNode)) list.Add(StageStep.Solo(a, "Idle_Tired_Loop", StageCam.Overview, true)); }
                    else list.Add(StandUp(a, ctx.ActorNode, false));
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

        /// <summary>
        /// Прийоми з власним ланцюгом кліпів P1–P2 (<c>ANIM_CENTER_CLIPS.md</c>). false — прийом не з цих (або за рингом, де
        /// склейок канатів і кута немає) — план за загальною таблицею.
        /// </summary>
        private static bool PlanMove(List<StageStep> list, MatchEvent ev, MatchSide a, MatchSide b, RollTier t, PlanContext ctx)
        {
            bool miss = t == RollTier.Miss, strong = t == RollTier.Strong;
            switch (ev.MoveKey)
            {
                case "armbar":
                case "leg_lock":
                case "spider_lock":
                    if (ev.Action != MatchActionId.Submission) return false;
                    AddSubmissionEnter(list, ev, a, ctx);
                    return true;
            }
            if (ev.Outside) return false;
            switch (ev.MoveKey)
            {
                case "meteor":
                    if (ev.Action != MatchActionId.Finisher) return false;
                    AddAerial(list, a, t, true, MoveStaging.Corner, ctx);
                    return true;
                case "swallow_dive":
                    AddAerial(list, a, t, false, MoveStaging.Corner, ctx);
                    return true;
                case "springboard":
                    AddSpringboard(list, a, t, ctx);
                    return true;
                case "whip":
                    AddWhip(list, ev, a, b, t);
                    return true;
                case "corner_drag":
                    AddCornerDrag(list, a, b, t);
                    return true;
                case "shoulder_charge":
                    list.Add(new StageStep { Kind = StageStepKind.Cut, Actor = a, Perch = MoveStaging.Ropes, Clip = "Rope_Back_L", Cam = StageCam.Low, Once = true,
                        From = "rope_hit", To = "rope_release" });
                    string charge = miss ? "Charge_Sidestep" : !strong ? "Charge_ShoulderBlock_NoSell"
                        : Pick(ev, "Charge_ShoulderBlock_Heavy", "Charge_ShoulderBlock", "Charge_ShoulderTackle");
                    list.Add(new StageStep { Kind = StageStepKind.Pair, Actor = a, Clip = charge, Cam = StageCam.Big, ByRoots = true });
                    return true;
            }
            return false;
        }

        /// <summary>Детермінований вибір варіанта за номером події: у матчі прийом не повторюється одним кліпом.</summary>
        private static string Pick(MatchEvent ev, params string[] v) { return v[(ev.Seq % v.Length + v.Length) % v.Length]; }

        /// <summary>
        /// Стрибок із кута (P1-3 «Метеор», P2-1 «Ластівка»): план «на куті» → стрибок у просторі A приземлення → приземлення
        /// пари за ступенем. Суперник, що стоїть, падає на плані 1 (приземлення — на лежачого). 10+ кінчається позою накриття;
        /// 7–9 — нападник на колінах і встає (ядро лишає його на ногах); провал — на колінах, лежить за ядром.
        /// </summary>
        private static void AddAerial(List<StageStep> list, MatchSide a, RollTier t, bool meteor, MoveStaging perch, PlanContext ctx)
        {
            var cut = StageStep.Cut(a, perch, perch == MoveStaging.Corner ? CornerTopPerch : DefaultPerch(perch), perch == MoveStaging.Corner ? CornerTopNote : null);
            cut.DropVictim = !ctx.VictimDown || ctx.VictimNode == StageStep.LyingFront;
            list.Add(cut);
            string land = meteor
                ? (t == RollTier.Strong ? "Land_Meteor" : t == RollTier.Partial ? "Land_Meteor_Partial" : "Land_Meteor_Miss")
                : (t == RollTier.Strong ? "Land_Swallow" : t == RollTier.Partial ? "Land_Swallow_Knees" : "Land_Swallow_Miss");
            string air = meteor ? (t == RollTier.Strong ? "Air_Meteor_Hype" : "Air_Meteor") : "Air_Swallow";
            list.Add(new StageStep { Kind = StageStepKind.Solo, Actor = a, Clip = air, Cam = StageCam.Big, AnchorPair = land });
            list.Add(new StageStep { Kind = StageStepKind.Pair, Actor = a, Clip = land, Cam = StageCam.Big, Chain = true });
            if (t == RollTier.Partial) list.Add(new StageStep { Kind = StageStepKind.Solo, Actor = a, Clip = KneelUp, Cam = StageCam.Overview, Aside = true });
        }

        /// <summary>
        /// Стрибок із каната (P2-3): нападник на верхньому канаті (QTE до відриву) → повітря пари над центром → приземлення з
        /// контакту. Жертва стоїть; лежачого — пірнання «Ластівки» з того самого каната.
        /// </summary>
        private static void AddSpringboard(List<StageStep> list, MatchSide a, RollTier t, PlanContext ctx)
        {
            if (ctx.VictimDown)
            {
                var perch = StageStep.Cut(a, MoveStaging.Ropes, "Springboard_Perch");
                perch.DropVictim = ctx.VictimNode == StageStep.LyingFront;   // пірнання — на лежачого на спині
                list.Add(perch);
                string land = t == RollTier.Strong ? "Land_Swallow" : t == RollTier.Partial ? "Land_Swallow_Knees" : "Land_Swallow_Miss";
                list.Add(new StageStep { Kind = StageStepKind.Solo, Actor = a, Clip = "Air_Swallow", Cam = StageCam.Big, AnchorPair = land });
                list.Add(new StageStep { Kind = StageStepKind.Pair, Actor = a, Clip = land, Cam = StageCam.Big, Chain = true });
                if (t == RollTier.Partial) list.Add(new StageStep { Kind = StageStepKind.Solo, Actor = a, Clip = KneelUp, Cam = StageCam.Overview, Aside = true });
                return;
            }
            list.Add(StageStep.Cut(a, MoveStaging.Ropes, "Springboard_Perch"));
            list.Add(new StageStep { Kind = StageStepKind.Pair, Actor = a, Clip = t == RollTier.Strong ? "Springboard_Air" : "Springboard_Air_Catch", Cam = StageCam.Big, ByRoots = true });
            string l = t == RollTier.Strong ? "Springboard_Land_Body" : t == RollTier.Partial ? "Springboard_Land_Catch" : "Springboard_Land_Counter";
            list.Add(new StageStep { Kind = StageStepKind.Pair, Actor = a, Clip = l, Cam = StageCam.Big, Chain = true });
            if (t == RollTier.Partial) list.Add(new StageStep { Kind = StageStepKind.Solo, Actor = a, Clip = KneelUp, Cam = StageCam.Overview, Aside = true });
        }

        /// <summary>
        /// «Затягнути до канатів» (бриф §5.1, P2-2): кидок у центрі лише до <c>release</c> → жертва спиною в канат від
        /// <c>rope_hit</c> → повернення. 10+ — зустрічний прийом нападника, 7–9 — жертва впирається й повертається сама, провал
        /// — реверс: у канати летить нападник.
        /// </summary>
        private static void AddWhip(List<StageStep> list, MatchEvent ev, MatchSide a, MatchSide b, RollTier t)
        {
            bool miss = t == RollTier.Miss;
            MatchSide thrower = miss ? b : a, flier = miss ? a : b;
            string whip = t == RollTier.Strong ? "Whip_Ropes_Fast" : t == RollTier.Partial ? "Whip_Ropes_Resist" : "Whip_Ropes";
            list.Add(new StageStep { Kind = StageStepKind.Pair, Actor = thrower, Clip = whip, Cam = StageCam.Close, StopAt = "release", Aside = true });
            list.Add(new StageStep { Kind = StageStepKind.Cut, Actor = flier, Perch = MoveStaging.Ropes, Clip = t == RollTier.Strong ? "Rope_Back_R_Fast" : "Rope_Back_L",
                Cam = StageCam.Low, Once = true, From = "rope_hit", To = "rope_release", Impact = t != RollTier.Strong });
            if (t == RollTier.Strong)
                list.Add(new StageStep { Kind = StageStepKind.Pair, Actor = a, Clip = Pick(ev, "Counter_Clothesline", "Counter_ShoulderBlock", "Counter_BigBoot", "Counter_Clothesline_Flip"),
                    Cam = StageCam.Big, ByRoots = true });
            else
                list.Add(new StageStep { Kind = StageStepKind.Solo, Actor = flier, Clip = miss ? "Rope_Return_Stumble" : Pick(ev, "Rope_Return_Tired", "Rope_Return_Stumble"),
                    Cam = StageCam.Close, Aside = true });
        }

        /// <summary>
        /// «Затягнути в кут» (бриф §5.1, P2-4): захват у центрі до <c>release</c> → жертва в куті від <c>corner_hit</c> (удар
        /// об кут — такт) → вихід із кута в центрі хитаючись. 7–9 — пом'якшила удар руками, провал — у кут летить нападник.
        /// </summary>
        private static void AddCornerDrag(List<StageStep> list, MatchSide a, MatchSide b, RollTier t)
        {
            bool miss = t == RollTier.Miss;
            MatchSide thrower = miss ? b : a, flier = miss ? a : b;
            string whip = t == RollTier.Strong ? "Whip_Corner_Fast" : t == RollTier.Partial ? "Whip_Corner_Chest" : "Whip_Corner";
            list.Add(new StageStep { Kind = StageStepKind.Pair, Actor = thrower, Clip = whip, Cam = StageCam.Close, StopAt = "release", Aside = true });
            list.Add(new StageStep { Kind = StageStepKind.Cut, Actor = flier, Perch = MoveStaging.Corner, Clip = whip + "_B", Cam = StageCam.Low, Once = true,
                From = "corner_hit", Impact = true });
            list.Add(new StageStep { Kind = StageStepKind.Solo, Actor = flier, Clip = t == RollTier.Partial ? "Corner_Exit_Shake" : "Corner_Exit_Stagger",
                Cam = StageCam.Close, Aside = true });
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
