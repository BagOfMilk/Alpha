using System;
using System.Collections.Generic;
using Game.Core.Ring;
using Game.Core.Ring.Match;

namespace Game.Gameplay.Ring.Models
{
    /// <summary>Звук події «Рингу» — рядок таблиці «подія → звук» (docs/ring/audio/EVENT_SOUND.md).</summary>
    public enum RingCue
    {
        None = 0,
        /// <summary>Удар по тілу.</summary>
        Hit,
        /// <summary>Падіння на мат рингу (кидок, бамп, реверс).</summary>
        Slam,
        /// <summary>Приземлення після стрибка з канатів чи кута.</summary>
        Land,
        /// <summary>Долоня рефері об мат — рахунок «раз-два-три».</summary>
        MatSlap,
        /// <summary>Зчіпка: обмін, реверс 7–9 (обмін захватами), провал больового.</summary>
        Lockup,
        /// <summary>Утримання: тіло падає на суперника.</summary>
        PinCover,
        /// <summary>Больовий і витримка больового: скрип настилу під тиском.</summary>
        HoldStrain,
        /// <summary>Відскок від канатів.</summary>
        RopeRebound,
        /// <summary>Удар у кут.</summary>
        CornerHit,
        /// <summary>Падіння за рингом на підлогу.</summary>
        FloorThud,
        /// <summary>Борець піднімається, переводить подих, закочується в ринг.</summary>
        CanvasShuffle,
        /// <summary>Вибух залу (оплески): великий момент, виклик залу, порятунок союзником.</summary>
        Pop,
        /// <summary>Гул залу — петля; подія без власного удару звучить ним (гучність за ажіотажем).</summary>
        CrowdBed,
        /// <summary>Дзвін початку матчу.</summary>
        BellStart,
        /// <summary>Дзвін кінця матчу (тричі).</summary>
        BellEnd,
        /// <summary>Удар на виході борця (під музику виходу).</summary>
        Entrance,
        /// <summary>З'явилось QTE.</summary>
        QtePrompt,
        /// <summary>Вдале QTE (Flourish — лише видовище, рішення власника 08.11.2026).</summary>
        QteHit,
        /// <summary>QTE пропущено.</summary>
        QteMiss,
        /// <summary>Бій за рингом: удар відкидає суперника на сталевий бар'єр.</summary>
        BarrierHit,
        /// <summary>Бій за рингом: кидок у бар'єр; предмет із провалом ≤6 — нападник після замаху сам летить у бар'єр (другий дотик).</summary>
        BarrierSlam,
        /// <summary>Предмет: удар складаним сталевим стільцем — чистий 10+ (якщо режисер обрав стілець) і по дотичній 7–9.</summary>
        WeaponChair,
        /// <summary>Предмет 10+, якщо режисер обрав стіл: кидок на стіл — стіл ламається під тілом.</summary>
        WeaponTable,
        /// <summary>Відлік рефері за рингом — один звук на кожну цифру.</summary>
        CountOut,
        /// <summary>Склейка місця: пара вилетіла за ринг чи повернулась — зал здіймається.</summary>
        CrowdSurge,
        /// <summary>Промах удару чи замах предметом повз (≤6): суперник ухилився, контакту немає — лише рух повітря.</summary>
        Whiff
    }

    /// <summary>Звук QTE, який кличе режисер матчу: поява й пропуск. Вдале QTE — подія ядра Flourish (таблиця → <see cref="RingCue.QteHit"/>).</summary>
    public enum RingQte { Prompt = 0, Miss = 1 }

    /// <summary>
    /// Предмет чистого удару 10+ за рингом — його обирає режисер матчу (чергує; docs/ring/CLIP_NEEDS.md, TRPG_DESIGN §4.4) і передає звуку
    /// явно: стілець чи стіл.
    /// </summary>
    public enum RingProp { Chair = 0, Table = 1 }

    /// <summary>
    /// Таблиця «подія → звук» (критерій М1: «без порожніх подій» — удар, гонг, рахунок, натовп, QTE, вихід; бій за рингом — запит ПМ
    /// 08.11.2026). Чистий C#: що звучить, вирішує тут, а грає <c>Stage/Unity/RingSound</c> лише схвалене власником
    /// (<see cref="RingSoundApproval"/>).
    /// <para>Ключі — НАЗВИ значень переліків ядра, а не самі значення. Таблиця пережила обидва ядра: основну гілку (там є
    /// <c>IntoRopes</c>/<c>IntoCorner</c>) і переробку бою (подія <c>Flourish</c>, дія <c>Weapon</c>, наслідок <c>Place</c>). Гілка звуку
    /// стоїть на гілці бою (1c03451) і йде в основну разом із пакетом бою, тож поля переробки (<c>MatchEvent.Outside</c>,
    /// <c>MatchEvent.MoveKey</c>) читаються напряму. Охоронець — <c>RingSoundMapTests</c>: кожне значення <see cref="MatchEventKind"/>,
    /// <see cref="MatchActionId"/> і <see cref="MatchEffectKind"/> має рядок.</para>
    /// <para>Звук іде за тим, що видно на екрані (<c>Stage/RingClipTable</c>): промах удару — ухил без контакту, реверс 7–9 — обмін
    /// захватами. Де таблиця кліпів ставить заглушку («бракує …» — кидок у бар'єр, кидок через канати, удар стільцем), звук іде за
    /// задуманим рухом із docs/ring/CLIP_NEEDS.md, а не за заглушкою. Предмет — за CLIP_NEEDS і TRPG_DESIGN §4.4 (ПМ 08.11.2026): 10+ —
    /// чистий удар стільцем або кидок на стіл (реквізит обирає режисер, <see cref="RingProp"/>), 7–9 — стілець по дотичній і нападник
    /// зачепив себе, ≤6 — замах повз і нападник сам у бар'єр. Другий дотик (самоудар, бар'єр) — <see cref="FollowUpOf(MatchEvent)"/>, на
    /// мітці кліпа.</para>
    /// </summary>
    public static class RingSoundMap
    {
        /// <summary>Тека нових звуків (не з CC0-набору): <c>Assets/Art/Ring/Sound/Resources/RingSound/&lt;слот&gt;.wav</c>, у грі — Resources.</summary>
        public const string ResourcesFolder = "RingSound";

        // Подія без дії: окремий звук. "Action"/"Sell" — за дією (нижче), "Kickout"/"MatchEnd" грає RingSound повтором долоні й дзвоном.
        private static readonly Dictionary<string, RingCue> Kinds = new Dictionary<string, RingCue>
        {
            { "Exchange", RingCue.Lockup },      // обмін на старті; одразу після реверсу 7–9 — без другого Lockup (ForEvent з prev)
            { "Action", RingCue.None },          // за дією
            { "Sell", RingCue.CrowdBed },        // продаж — до удару; удар звучить на самій дії (раніше звучав двічі, і до контакту)
            { "Reverse", RingCue.Slam },         // 10+ — кидок; 7–9 — обмін захватами, провал — нічого (ступінь — у ForEvent)
            { "Kickout", RingCue.MatSlap },      // «раз-два» долонею, далі вибух залу (CrowdPop режисера)
            { "Endure", RingCue.HoldStrain },
            { "Rescue", RingCue.Pop },
            { "TurnEnd", RingCue.CrowdBed },     // ініціатива переходить: гул залу, без удару
            { "MatchEnd", RingCue.BellEnd },     // + три долоні, якщо кінець утриманням; відлік за рингом — останні цифри на дії
            { "Flourish", RingCue.QteHit },      // переробка бою: вдале QTE
        };

        private static readonly Dictionary<string, RingCue> Actions = new Dictionary<string, RingCue>
        {
            { "Strike", RingCue.Hit },
            { "Dirty", RingCue.Hit },
            { "Desperate", RingCue.Hit },
            { "Throw", RingCue.Slam },
            { "Finisher", RingCue.Slam },
            { "HopeSpot", RingCue.Slam },
            { "TopRope", RingCue.Land },
            { "IntoRopes", RingCue.RopeRebound },   // лише основна гілка: у переробці бою дії немає
            { "IntoCorner", RingCue.CornerHit },    // лише основна гілка
            { "Taunt", RingCue.Pop },
            { "Submission", RingCue.HoldStrain },
            { "Pin", RingCue.PinCover },
            { "GetUp", RingCue.CanvasShuffle },
            { "Recover", RingCue.CanvasShuffle },
            { "CatchBreath", RingCue.CanvasShuffle },
            { "LeaveRing", RingCue.FloorThud },     // викинув за канати — пара на підлозі (склейка — наслідок Place)
            { "ReturnToRing", RingCue.CanvasShuffle }, // закотив суперника в ринг і заліз сам
            { "Ally", RingCue.Pop },
            { "Weapon", RingCue.WeaponChair },      // переробка бою: предмет за рингом; ступінь — у Refine
        };

        // Наслідки події, що мають власний звук. Решта звучить дією, яка їх спричинила (None — свідомо, не забуто).
        private static readonly Dictionary<string, RingCue> Effects = new Dictionary<string, RingCue>
        {
            { "Place", RingCue.CrowdSurge },      // переробка бою: склейка — пара за рингом (1) чи знову на рингу (0)
            { "Count", RingCue.CountOut },        // відлік за рингом зріс на Amount: стільки ж цифр
            { "Damage", RingCue.None }, { "Momentum", RingCue.None }, { "Hype", RingCue.None }, { "Down", RingCue.None },
            { "Stand", RingCue.None }, { "Stunned", RingCue.None }, { "Warning", RingCue.None }, { "RefereeDistracted", RingCue.None },
            { "Token", RingCue.None }, { "Stamina", RingCue.None },
            { "ZoneChange", RingCue.None },       // лише основна гілка: місце пари там звучить дією LeaveRing/ReturnToRing
            { "ApChange", RingCue.None },         // лише основна гілка
        };

        private static readonly Dictionary<RingCue, string[]> Slots = new Dictionary<RingCue, string[]>
        {
            { RingCue.Hit, new[] { "hit_01", "hit_02", "hit_03", "hit_04" } },
            { RingCue.Slam, new[] { "slam_01", "slam_02", "slam_03", "slam_04", "slam_05" } },
            { RingCue.Land, new[] { "land" } },
            { RingCue.MatSlap, new[] { "mat_slap" } },
            { RingCue.Lockup, new[] { "lockup" } },
            { RingCue.PinCover, new[] { "pin_cover" } },
            { RingCue.HoldStrain, new[] { "hold_strain" } },
            { RingCue.RopeRebound, new[] { "rope_rebound" } },
            { RingCue.CornerHit, new[] { "corner_hit" } },
            { RingCue.FloorThud, new[] { "floor_thud" } },
            { RingCue.CanvasShuffle, new[] { "canvas_shuffle" } },
            { RingCue.Pop, new[] { "applause_hall" } },
            { RingCue.CrowdBed, new[] { "crowd_shouting" } },
            { RingCue.BellStart, new[] { "bell_01" } },
            { RingCue.BellEnd, new[] { "bell_02" } },
            { RingCue.Entrance, new[] { "gong_01" } },
            { RingCue.QtePrompt, new[] { "qte_prompt" } },
            { RingCue.QteHit, new[] { "qte_hit" } },
            { RingCue.QteMiss, new[] { "qte_miss" } },
            { RingCue.BarrierHit, new[] { "barrier_hit" } },
            { RingCue.BarrierSlam, new[] { "barrier_slam" } },
            { RingCue.WeaponChair, new[] { "weapon_chair" } },
            { RingCue.WeaponTable, new[] { "weapon_table" } },
            { RingCue.CountOut, new[] { "count_out" } },
            { RingCue.CrowdSurge, new[] { "crowd_surge" } },
            { RingCue.Whiff, new[] { "whiff" } },
        };

        /// <summary>Слоти, яких немає в CC0-наборі сцени (RingStageAssets): нові звуки подій, з теки <see cref="ResourcesFolder"/>.</summary>
        public static readonly string[] NewSlots =
        {
            "lockup", "pin_cover", "hold_strain", "rope_rebound", "corner_hit", "floor_thud", "canvas_shuffle",
            "qte_prompt", "qte_hit", "qte_miss",
            "barrier_hit", "barrier_slam", "weapon_chair", "weapon_table", "count_out", "crowd_surge", "whiff"
        };

        /// <summary>Скільки цифр відліку за рингом грати на один наслідок Count (ядро додає +3 за дію).</summary>
        public const int MaxCountTicks = 3;

        // Фінішери-больові (категорія submission у career_moves.json): на екрані захват (RingClipTable: iron_clamp — Chain_Headlock_Crank),
        // тож звучать скрипом захвату, а не падінням. Охоронець — RingSoundMapTests звіряє набір із каталогом прийомів.
        private static readonly HashSet<string> HoldFinisherKeys = new HashSet<string> { "iron_clamp" };

        /// <summary>Фінішери-больові: прийом звучить захватом (<see cref="RingCue.HoldStrain"/>), а не падінням.</summary>
        public static IReadOnlyCollection<string> HoldFinishers { get { return HoldFinisherKeys; } }

        // Повітряні фінішери (категорія aerial у career_moves.json): meteor — стрибок із кута (RingClipTable.AddAerial, Ф2 крок 7:
        // Air_Meteor → Land_Meteor / _Partial, такт — на контакті приземлення). Звук — приземлення, як у стрибка з канатів (TopRope);
        // провал — Land_Meteor_Miss, нападник сам падає на мат (Slam). Охоронець — RingSoundMapTests звіряє набір із каталогом прийомів.
        private static readonly HashSet<string> AerialFinisherKeys = new HashSet<string> { "meteor" };

        /// <summary>Повітряні фінішери: прийом звучить приземленням (<see cref="RingCue.Land"/>), як стрибок з канатів.</summary>
        public static IReadOnlyCollection<string> AerialFinishers { get { return AerialFinisherKeys; } }

        // Прийоми, чий такт — удар тілом об місце (RingClipTable: склейка Cut з Impact). Звук місця — склейка режисера: канат —
        // RopeRebound на мітці rope_hit, кут — CornerHit на corner_hit (RingMatchDirector.RunCut). Подія додає лише те, чого місце не дає.
        // «Затягнути до канатів» (whip, AddWhip): 7–9 і провал (реверс: у канати летить нападник) — лише спина в канат, удару немає —
        // гул залу (CrowdBed); 10+ — зустрічний удар у центрі (Hit). «Затягнути в кут» (corner_drag, AddCornerDrag): тіло б'ється об
        // кут на кожному ступені — удар тілом (Hit) під CornerHit; провал — той самий удар, у кут летить нападник (раніше Whiff: ухилу немає).
        private const string RopeSendKey = "whip", CornerSendKey = "corner_drag";

        /// <summary>Звук події матчу без ступеня кидка й місця пари (як у журналі без кидка, на рингу).</summary>
        public static RingCue ForEvent(MatchEventKind kind, MatchActionId action)
        {
            return ForEvent(kind.ToString(), action.ToString(), false, RollTier.Strong, false);
        }

        /// <summary>Звук події матчу: ступінь кидка й місце пари (бій за рингом) уточнюють звук дії; предмет 10+ — стілець.</summary>
        public static RingCue ForEvent(MatchEvent ev) { return ForEvent(ev, RingProp.Chair); }

        /// <summary>Звук події матчу з реквізитом чистого удару предметом 10+, який обрав режисер.</summary>
        public static RingCue ForEvent(MatchEvent ev, RingProp prop)
        {
            if (ev == null) return RingCue.None;
            return ForEvent(ev.Kind.ToString(), ev.Action.ToString(), ev.HasRoll, ev.Tier, ev.Outside, prop, ev.MoveKey);
        }

        /// <summary>
        /// Звук події з урахуванням попередньої в журналі (<paramref name="prev"/> — подія, що звучала перед нею, null — немає).
        /// Обмін (Exchange) одразу після реверсу 7–9 лише вирішує ініціативу: обмін захватами вже прозвучав на самому реверсі
        /// (кліп <c>Lockup_Exchange</c>), тож другого Lockup немає — лише гул залу. Рішення ПМ 08.11.2026 (docs/ring/F2_DIRECTOR.md
        /// п. 7 — у гілці <c>claude/ring-combat-overhaul</c>, 7c19e2e). Подає події по одній — <see cref="RingSoundTrail"/>.
        /// </summary>
        public static RingCue ForEvent(MatchEvent ev, RingProp prop, MatchEvent prev)
        {
            if (IsExchangeAfterPartialReverse(ev, prev)) return RingCue.CrowdBed;
            return ForEvent(ev, prop);
        }

        /// <summary>
        /// Обмін, що йде в журналі одразу за реверсом 7–9 (номер події — на один більший): ядро видає його в тому ж виклику
        /// (<c>MatchEngine.React</c> → <c>GiveInitiative(RollExchange())</c>). Обмін, що настав пізніше, — не цей випадок.
        /// </summary>
        public static bool IsExchangeAfterPartialReverse(MatchEvent ev, MatchEvent prev)
        {
            return ev != null && prev != null && ev.Kind == MatchEventKind.Exchange
                && prev.Kind == MatchEventKind.Reverse && prev.HasRoll && prev.Tier == RollTier.Partial && prev.Seq + 1 == ev.Seq;
        }

        /// <summary>
        /// Звук за назвами: для дії й продажу — за дією, інакше — за видом події. None — лише для невідомих назв.
        /// <paramref name="moveKey"/> — прийом з каталогу (<c>MatchEvent.MoveKey</c>), null — дія без прийому.
        /// </summary>
        public static RingCue ForEvent(string kind, string action, bool hasRoll, RollTier tier, bool outside, RingProp prop = RingProp.Chair,
            string moveKey = null)
        {
            RingCue c;
            if (kind == null || !Kinds.TryGetValue(kind, out c)) return RingCue.None;
            if (kind == "Reverse" && hasRoll)
            {
                // RingClipTable.Plan: 10+ — захисник кидає нападника (Bodyslam), 7–9 — обмін захватами (Lockup_Exchange), провал —
                // кроків немає (далі кидок самого нападника), тож і звуку немає.
                if (tier == RollTier.Strong) return RingCue.Slam;
                return tier == RollTier.Partial ? RingCue.Lockup : RingCue.CrowdBed;
            }
            if (c != RingCue.None) return c;
            if (action == null || !Actions.TryGetValue(action, out c)) return RingCue.None;
            return Refine(action, c, hasRoll, tier, outside, prop, moveKey);
        }

        /// <summary>
        /// Другий дотик тієї самої дії — режисер кличе його на мітці кліпа (як <see cref="RingCue.RopeRebound"/> на канаті): предмет 7–9 —
        /// нападник зачепив себе (<see cref="RingCue.Hit"/>), ≤6 — після замаху повз сам летить у бар'єр (<see cref="RingCue.BarrierSlam"/>).
        /// None — другого дотику немає.
        /// </summary>
        public static RingCue FollowUpOf(MatchEvent ev)
        {
            return ev == null ? RingCue.None : FollowUpOf(ev.Kind.ToString(), ev.Action.ToString(), ev.HasRoll, ev.Tier);
        }

        /// <summary>Другий дотик за назвами (обидва ядра).</summary>
        public static RingCue FollowUpOf(string kind, string action, bool hasRoll, RollTier tier)
        {
            if (kind != "Action" || action != "Weapon" || !hasRoll) return RingCue.None;
            if (tier == RollTier.Partial) return RingCue.Hit;
            if (tier == RollTier.Miss) return RingCue.BarrierSlam;
            return RingCue.None;
        }

        /// <summary>Звук дії (подія Action) без ступеня кидка, на рингу.</summary>
        public static RingCue ForAction(MatchActionId action)
        {
            RingCue c;
            return Actions.TryGetValue(action.ToString(), out c) ? c : RingCue.None;
        }

        /// <summary>
        /// Ступінь кидка й місце пари — так, як показує <c>RingClipTable</c>. Провал: удар чи брудний прийом — ухил без контакту
        /// (<c>Center_Sidestep</c>); «Вийти з рингу» і больовий — пара в захваті (<c>Lockup_Stalemate</c>); кидок, відчайдушний, фінішер
        /// і стрибок з канатів — падіння (<c>Bump_Back</c>). Промінь надії не підводить цілком (ядро, <c>MatchEngine.ApplyRolled</c>):
        /// провал — план 7–9, суперник падає, звук той самий, що 7–9 (як у <c>RingClipTable.PlanAction</c>, 9c18c40);
        /// провокація — борцю боляче, оплесків немає (<c>Idle_Hurt_Body_Loop</c>); підйом — той самий кліп, що в «відновитися»
        /// (<c>Idle_Tired_Loop</c>), тож і звук той самий. Провал важить більше за місце: за рингом промах удару — теж ухил.
        /// Фінішер — за ключем прийому (з 2937113 подія фінішера несе ключ ґіміка): больовий (<see cref="HoldFinishers"/>) — захват,
        /// повітряний (<see cref="AerialFinishers"/>) — приземлення, решта — падіння суперника. «Затягнути до канатів» 7–9 і провал — лише
        /// канат склейки (гул залу), «затягнути в кут» — удар тілом під звук кута на кожному ступені. Предмет 10+ — реквізит режисера (стілець чи стіл), 7–9 — стілець, ≤6 —
        /// замах повз (бар'єр — другим дотиком); за рингом удар і кидок ідуть у бар'єр.
        /// </summary>
        private static RingCue Refine(string action, RingCue c, bool hasRoll, RollTier tier, bool outside, RingProp prop, string moveKey)
        {
            if (action == "Weapon")
            {
                if (!hasRoll || tier == RollTier.Partial) return RingCue.WeaponChair;
                if (tier == RollTier.Strong) return prop == RingProp.Table ? RingCue.WeaponTable : RingCue.WeaponChair;
                return RingCue.Whiff;
            }
            // Промінь надії: провал рахується як 7–9 — так само ступінь підміняє таблиця кліпів, тож звук іде за тим самим планом.
            if (action == "HopeSpot" && hasRoll && tier == RollTier.Miss) tier = RollTier.Partial;
            // Удар об канат чи кут (склейка з тактом): за рингом канатів і кута немає — план загальний (RingClipTable.PlanMove).
            if (hasRoll && !outside && moveKey == RopeSendKey && tier != RollTier.Strong) return RingCue.CrowdBed;
            if (hasRoll && !outside && moveKey == CornerSendKey) return RingCue.Hit;
            if (hasRoll && tier == RollTier.Miss)
            {
                switch (action)
                {
                    case "Strike":
                    case "Dirty": return RingCue.Whiff;
                    case "LeaveRing":
                    case "Submission": return RingCue.Lockup;
                    case "Throw":
                    case "Desperate":
                    case "Finisher":
                    case "TopRope": return RingCue.Slam;
                    case "Taunt": return RingCue.CrowdBed;
                }
                return c;
            }
            if (action == "Finisher" && moveKey != null && HoldFinisherKeys.Contains(moveKey)) return RingCue.HoldStrain;
            if (action == "Finisher" && moveKey != null && AerialFinisherKeys.Contains(moveKey)) return RingCue.Land;
            if (outside)
            {
                if (action == "Strike") return RingCue.BarrierHit;
                if (action == "Throw") return RingCue.BarrierSlam;
            }
            return c;
        }

        /// <summary>Звук наслідку події (склейка місця, відлік за рингом); None — наслідок звучить дією, що його спричинила.</summary>
        public static RingCue ForEffect(MatchEffectKind kind) { return ForEffect(kind.ToString()); }

        public static RingCue ForEffect(string kindName)
        {
            RingCue c;
            return kindName != null && Effects.TryGetValue(kindName, out c) ? c : RingCue.None;
        }

        /// <summary>Чи має вид події власний рядок у таблиці (Action — теж: його звук дає дія).</summary>
        public static bool HasKind(string kindName) { return Kinds.ContainsKey(kindName); }

        /// <summary>Чи має дія рядок у таблиці.</summary>
        public static bool HasAction(string actionName) { return Actions.ContainsKey(actionName); }

        /// <summary>Чи розглянуто наслідок у таблиці (зі звуком чи свідомо без).</summary>
        public static bool HasEffect(string effectName) { return Effects.ContainsKey(effectName); }

        /// <summary>Слоти бібліотеки звуку, з яких грає цей звук (порожньо — лише для None).</summary>
        public static IReadOnlyList<string> SlotsOf(RingCue cue)
        {
            string[] s;
            return Slots.TryGetValue(cue, out s) ? s : (IReadOnlyList<string>)Array.Empty<string>();
        }

        /// <summary>Усі звуки таблиці, крім None.</summary>
        public static IEnumerable<RingCue> AllCues()
        {
            foreach (RingCue c in Enum.GetValues(typeof(RingCue)))
                if (c != RingCue.None) yield return c;
        }

    }

    /// <summary>
    /// Слід подій для звуку: пам'ятає подію, що звучала перед поточною, і питає таблицю з нею (<see cref="RingSoundMap.ForEvent(MatchEvent, RingProp, MatchEvent)"/>).
    /// Без Unity, тож тести подають журнал матчу так само, як режисер подає його в <c>RingSound.OnEvent</c>. Між матчами скидати
    /// не обов'язково: таблиця звіряє номер події, а обмін слідом за реверсом 7–9 ядро видає в тому ж виклику.
    /// </summary>
    public sealed class RingSoundTrail
    {
        private MatchEvent _prev;

        /// <summary>Звук наступної події; подія стає попередньою для того, що прийде далі.</summary>
        public RingCue Next(MatchEvent ev, RingProp prop = RingProp.Chair)
        {
            var prev = _prev;
            _prev = ev;
            return RingSoundMap.ForEvent(ev, prop, prev);
        }
    }
}
