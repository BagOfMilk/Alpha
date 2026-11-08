using System;
using System.Collections.Generic;
using System.Reflection;
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
        /// <summary>Обмін на старті: борці зчіпляються.</summary>
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
    /// <para>Ключі — НАЗВИ значень переліків ядра, а не самі значення: та сама таблиця працює і з основною гілкою, і з переробкою бою
    /// (там з'явились подія <c>Flourish</c>, дія <c>Weapon</c>, наслідок <c>Place</c> і поле <c>MatchEvent.Outside</c>, а
    /// <c>IntoRopes</c>/<c>IntoCorner</c> зникли). Охоронець — <c>RingSoundMapTests</c>: кожне значення <see cref="MatchEventKind"/>,
    /// <see cref="MatchActionId"/> і <see cref="MatchEffectKind"/> має рядок.</para>
    /// <para>Звук іде за тим, що видно на екрані (<c>Stage/RingClipTable</c>): промах удару — ухил без контакту. Предмет — за
    /// docs/ring/CLIP_NEEDS.md і TRPG_DESIGN §4.4 (ПМ 08.11.2026): 10+ — чистий удар стільцем або кидок на стіл (реквізит обирає режисер,
    /// <see cref="RingProp"/>), 7–9 — стілець по дотичній і нападник зачепив себе, ≤6 — замах повз і нападник сам у бар'єр. Другий дотик
    /// (самоудар, бар'єр) — <see cref="FollowUpOf(MatchEvent)"/>, на мітці кліпа.</para>
    /// </summary>
    public static class RingSoundMap
    {
        /// <summary>Тека нових звуків (не з CC0-набору): <c>Assets/Art/Ring/Sound/Resources/RingSound/&lt;слот&gt;.wav</c>, у грі — Resources.</summary>
        public const string ResourcesFolder = "RingSound";

        // Подія без дії: окремий звук. "Action"/"Sell" — за дією (нижче), "Kickout"/"MatchEnd" грає RingSound повтором долоні й дзвоном.
        private static readonly Dictionary<string, RingCue> Kinds = new Dictionary<string, RingCue>
        {
            { "Exchange", RingCue.Lockup },
            { "Action", RingCue.None },          // за дією
            { "Sell", RingCue.CrowdBed },        // продаж — до удару; удар звучить на самій дії (раніше звучав двічі, і до контакту)
            { "Reverse", RingCue.Slam },
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
            return ForEvent(ev.Kind.ToString(), ev.Action.ToString(), ev.HasRoll, ev.Tier, OutsideOf(ev), prop);
        }

        /// <summary>
        /// Звук за назвами (обидва ядра): для дії й продажу — за дією, інакше — за видом події. None — лише для невідомих назв.
        /// </summary>
        public static RingCue ForEvent(string kind, string action, bool hasRoll, RollTier tier, bool outside, RingProp prop = RingProp.Chair)
        {
            RingCue c;
            if (kind == null || !Kinds.TryGetValue(kind, out c)) return RingCue.None;
            if (c != RingCue.None) return c;
            if (action == null || !Actions.TryGetValue(action, out c)) return RingCue.None;
            return Refine(action, c, hasRoll, tier, outside, prop);
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
        /// Ступінь кидка й місце пари — так, як показує <c>RingClipTable</c>: удар чи брудний прийом з провалом — ухил без контакту;
        /// «Вийти з рингу» з провалом — суперник утримався в захваті; відчайдушний з провалом — сам падає; предмет 10+ — реквізит
        /// режисера (стілець чи стіл), 7–9 — стілець, ≤6 — замах повз (бар'єр — другим дотиком); за рингом удар і кидок ідуть у бар'єр.
        /// </summary>
        private static RingCue Refine(string action, RingCue c, bool hasRoll, RollTier tier, bool outside, RingProp prop)
        {
            if (action == "Weapon")
            {
                if (!hasRoll || tier == RollTier.Partial) return RingCue.WeaponChair;
                if (tier == RollTier.Strong) return prop == RingProp.Table ? RingCue.WeaponTable : RingCue.WeaponChair;
                return RingCue.Whiff;
            }
            if (hasRoll && tier == RollTier.Miss)
            {
                if (action == "Strike" || action == "Dirty") return RingCue.Whiff;
                if (action == "LeaveRing") return RingCue.Lockup;
                if (action == "Desperate") return RingCue.Slam;
                return c;
            }
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

        // ---------- місце пари ----------

        private static readonly Dictionary<Type, FieldInfo> OutsideFields = new Dictionary<Type, FieldInfo>();

        /// <summary>
        /// Чи була пара за рингом, коли дія почалась (<c>MatchEvent.Outside</c> переробки бою). Поле читається за назвою, бо в основному
        /// ядрі його ще немає: до злиття переробки — завжди false. Після злиття можна читати <c>ev.Outside</c> напряму.
        /// </summary>
        public static bool OutsideOf(object ev)
        {
            if (ev == null) return false;
            var t = ev.GetType();
            FieldInfo f;
            lock (OutsideFields)
            {
                if (!OutsideFields.TryGetValue(t, out f))
                {
                    f = t.GetField("Outside", BindingFlags.Public | BindingFlags.Instance);
                    if (f != null && f.FieldType != typeof(bool)) f = null;
                    OutsideFields[t] = f;
                }
            }
            return f != null && (bool)f.GetValue(ev);
        }
    }
}
