using System;
using System.Collections.Generic;
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
        /// <summary>Борець піднімається, переводить подих.</summary>
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
        QteMiss
    }

    /// <summary>Звук QTE, який кличе режисер матчу: поява й пропуск. Вдале QTE — подія ядра Flourish (таблиця → <see cref="RingCue.QteHit"/>).</summary>
    public enum RingQte { Prompt = 0, Miss = 1 }

    /// <summary>
    /// Таблиця «подія → звук» (критерій М1: «без порожніх подій» — удар, гонг, рахунок, натовп, QTE, вихід). Чистий C#: що
    /// звучить, вирішує тут, а грає <c>Stage/Unity/RingSound</c> лише схвалене власником (<see cref="RingSoundApproval"/>).
    /// <para>Ключі — НАЗВИ значень переліків ядра, а не самі значення: та сама таблиця працює і з основною гілкою, і з переробкою бою
    /// (там з'явилась подія <c>Flourish</c>, а <c>IntoRopes</c>/<c>IntoCorner</c>/<c>LeaveRing</c>/<c>ReturnToRing</c> зникли). Охоронець —
    /// <c>RingSoundMapTests</c>: кожне значення <see cref="MatchEventKind"/> і <see cref="MatchActionId"/> має звук.</para>
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
            { "MatchEnd", RingCue.BellEnd },     // + три долоні, якщо кінець утриманням
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
            { "IntoRopes", RingCue.RopeRebound },
            { "IntoCorner", RingCue.CornerHit },
            { "Taunt", RingCue.Pop },
            { "Submission", RingCue.HoldStrain },
            { "Pin", RingCue.PinCover },
            { "GetUp", RingCue.CanvasShuffle },
            { "Recover", RingCue.CanvasShuffle },
            { "CatchBreath", RingCue.CanvasShuffle },
            { "LeaveRing", RingCue.FloorThud },
            { "ReturnToRing", RingCue.FloorThud },
            { "Ally", RingCue.Pop },
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
        };

        /// <summary>Слоти, яких немає в CC0-наборі сцени (RingStageAssets): нові звуки подій, з теки <see cref="ResourcesFolder"/>.</summary>
        public static readonly string[] NewSlots =
        {
            "lockup", "pin_cover", "hold_strain", "rope_rebound", "corner_hit", "floor_thud", "canvas_shuffle",
            "qte_prompt", "qte_hit", "qte_miss"
        };

        /// <summary>Звук події матчу: для дії й продажу — за дією, інакше — за видом події. None — лише для невідомих назв.</summary>
        public static RingCue ForEvent(MatchEventKind kind, MatchActionId action)
        {
            RingCue c;
            if (!Kinds.TryGetValue(kind.ToString(), out c)) return RingCue.None;
            if (c != RingCue.None) return c;
            return ForAction(action);
        }

        /// <summary>Звук дії (подія Action).</summary>
        public static RingCue ForAction(MatchActionId action)
        {
            RingCue c;
            return Actions.TryGetValue(action.ToString(), out c) ? c : RingCue.None;
        }

        /// <summary>Чи має вид події власний рядок у таблиці (Action — теж: його звук дає дія).</summary>
        public static bool HasKind(string kindName) { return Kinds.ContainsKey(kindName); }

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
}
