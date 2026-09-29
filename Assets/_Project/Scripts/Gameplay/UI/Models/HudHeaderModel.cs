using System;
using System.Collections.Generic;
using Game.Core.Characters;
using Game.Core.Characters.Creation;
using Game.Core.Pressure;
using Game.Core.Session;
using Game.Core.Session.Views;
using Game.Gameplay.Text;

namespace Game.Gameplay.UI
{
    /// <summary>Один відкритий ресурс у шапці: ключ (для тексту «resource.{key}»), підпис і значення.</summary>
    public sealed class HudResource
    {
        public string Key;
        public string Label;
        public int Value;
    }

    /// <summary>Щабель драбини полос Напруги — лише слово, жодного числа (інв. 3, Статут UI-06).</summary>
    public sealed class HudLadderRung
    {
        public string BandKey;
        public string Word;
        public bool IsCurrent;
    }

    /// <summary>Позначка стану в шапці («На варті», «Вільна гра»).</summary>
    public sealed class HudBadge
    {
        public string Key;
        public string Label;
    }

    /// <summary>
    /// Готова до показу шапка (docs/HUD_DESIGN.md §4.2, §5.1). Числа тут —
    /// лише відкриті: доба, ресурси з allow-list R17, скільки людей поза
    /// містом. Прихована шкала приходить лише словом полоси і драбиною слів.
    /// </summary>
    public sealed class HudHeader
    {
        /// <summary>«Доба N · фаза · назва тіра» — одна назва тіра замість колишніх «Люди: …» і «Тір N» (HUD_DESIGN §2.2).</summary>
        public string DayLine;
        /// <summary>Ключ полоси («Calm».."Fracture"), як його віддає SessionView.TensionBand.</summary>
        public string BandKey;
        /// <summary>Слово полоси («Ропіт»).</summary>
        public string BandWord;
        /// <summary>Підпис для шапки: «Настрій: Ропіт».</summary>
        public string BandLine;
        /// <summary>Уся драбина знизу вгору (Спокій → Злам), поточний щабель позначений.</summary>
        public IReadOnlyList<HudLadderRung> Ladder;
        /// <summary>Слово сусідньої нижчої полоси; null на нижньому краї (Спокій).</summary>
        public string BandBelowWord;
        /// <summary>Слово сусідньої вищої полоси; null на верхньому краї (Злам).</summary>
        public string BandAboveWord;
        /// <summary>Рядки підказки при наведенні (рішення власника 29.09.2026, «1. B»): вище · зараз · нижче, без чисел і стрілок.</summary>
        public IReadOnlyList<string> LadderLines;
        public IReadOnlyList<HudResource> Resources;
        /// <summary>Скільки напарників поза містом (на вилазці) — публічний стан ростеру, не прихована шкала.</summary>
        public int AwayCount;
        /// <summary>«Поза містом: N» або null, коли всі вдома (позначка живе, поки живе стан — HUD_DESIGN §4.1).</summary>
        public string AwayLine;
        public IReadOnlyList<HudBadge> Badges;
    }

    /// <summary>
    /// Модель шапки без рушія (HUD_DESIGN §8 «модель окремо від малювання»,
    /// HP-4): рахує, ЩО показати, з публічних виглядів <c>GameSession</c>.
    /// Малюють її два види однаково — UI Toolkit (<c>Toolkit/HudToolkitView</c>)
    /// і IMGUI-фолбек (<c>GameShell.DrawTopBar</c>, прапорець <c>-imgui-hud</c>).
    /// Перевіряється headless (<c>HudHeaderModelTests</c>).
    /// </summary>
    public static class HudHeaderModel
    {
        /// <summary>
        /// Порядок полос знизу вгору — з самого enum ядра, а не зі списку тут:
        /// нова полоса (якщо колись з'явиться) стане на своє місце без правки
        /// шапки. Значення enum назовні не йде — лише ім'я як ключ тексту.
        /// </summary>
        public static readonly IReadOnlyList<string> BandOrder = BuildBandOrder();

        private static string[] BuildBandOrder()
        {
            var values = (TensionBand[])Enum.GetValues(typeof(TensionBand));
            Array.Sort(values);
            var names = new string[values.Length];
            for (int i = 0; i < values.Length; i++) names[i] = values[i].ToString();
            return names;
        }

        // ---------------- ресурси: одна таблиця рядків ----------------

        private sealed class ResourceRow
        {
            public readonly string Key;
            public readonly Func<EconomyView, int> Read;
            public readonly string FieldName;

            public ResourceRow(string key, string fieldName, Func<EconomyView, int> read)
            {
                Key = key; FieldName = fieldName; Read = read;
            }
        }

        /// <summary>
        /// Відкриті ресурси шапки — ОДНА таблиця рядків у порядку показу.
        /// Паралельна гілка ділить «матеріали» на два компоненти: після злиття
        /// сюди додається по рядку на нове поле <see cref="EconomyView"/> (і
        /// ключ «resource.{key}» у UkrainianText) — більше нічого в шапці не
        /// міняється. Охоронець <c>HudHeaderModelTests.ResourceTable_CoversEveryEconomyField</c>
        /// падає, якщо поле з'явилось, а рядка немає.
        /// </summary>
        private static readonly ResourceRow[] ResourceRows =
        {
            new ResourceRow("gold", "Gold", e => e.Gold),
            new ResourceRow("materials", "Materials", e => e.Materials),
            new ResourceRow("food", "Food", e => e.Food),
        };

        /// <summary>Імена полів <see cref="EconomyView"/>, які покриває таблиця (для охоронця в тестах).</summary>
        public static IReadOnlyList<string> CoveredEconomyFields
        {
            get
            {
                var list = new List<string>(ResourceRows.Length);
                foreach (var row in ResourceRows) list.Add(row.FieldName);
                return list;
            }
        }

        /// <summary>Узагальнений список відкритих ресурсів із публічного гаманця (R17).</summary>
        public static List<HudResource> ResourcesFrom(EconomyView economy, Gender gender)
        {
            var result = new List<HudResource>(ResourceRows.Length);
            if (economy == null) return result;
            foreach (var row in ResourceRows)
                result.Add(new HudResource
                {
                    Key = row.Key,
                    Label = UkrainianText.Get("resource." + row.Key, gender),
                    Value = row.Read(economy)
                });
            return result;
        }

        /// <summary>Скільки напарників зараз на вилазці (<see cref="CompanionStatus.OnMission"/>) — публічне поле ростеру.</summary>
        public static int CountAway(RosterView roster)
        {
            if (roster?.Companions == null) return 0;
            int n = 0;
            foreach (var c in roster.Companions)
                if (c != null && c.Status == CompanionStatus.OnMission) n++;
            return n;
        }

        // ---------------- шапка ----------------

        public static HudHeader Build(SessionView view, SessionState state, IReadOnlyList<HudResource> resources,
            RosterView roster, Gender gender)
        {
            var header = new HudHeader
            {
                Resources = resources ?? new List<HudResource>(),
                Badges = new List<HudBadge>(),
                Ladder = new List<HudLadderRung>(),
                LadderLines = new List<string>()
            };
            if (view == null) return header;

            header.DayLine = DayLine(view, state, gender);

            header.BandKey = view.TensionBand;
            header.BandWord = ScreenText.MoodChip(view.TensionBand, gender);
            header.BandLine = string.IsNullOrEmpty(header.BandWord)
                ? string.Empty
                : UkrainianText.Format("ui.topbar.mood", gender, "band", header.BandWord);

            var ladder = LadderFor(view.TensionBand, gender);
            header.Ladder = ladder;
            int current = -1;
            for (int i = 0; i < ladder.Count; i++)
                if (ladder[i].IsCurrent) current = i;
            if (current >= 0)
            {
                header.BandBelowWord = current > 0 ? ladder[current - 1].Word : null;
                header.BandAboveWord = current < ladder.Count - 1 ? ladder[current + 1].Word : null;
                header.LadderLines = LadderLines(header.BandAboveWord, header.BandWord, header.BandBelowWord, gender);
            }

            header.AwayCount = CountAway(roster);
            header.AwayLine = header.AwayCount > 0
                ? UkrainianText.Format("ui.hud.away", gender, "count", header.AwayCount.ToString())
                : null;

            var badges = new List<HudBadge>();
            if (view.IsPatrolling)
                badges.Add(new HudBadge { Key = "patrolling", Label = UkrainianText.Get("ui.topbar.patrolling", gender) });
            if (view.IsFreePlay)
                badges.Add(new HudBadge { Key = "freeplay", Label = UkrainianText.Get("ui.topbar.freeplay", gender) });
            header.Badges = badges;
            return header;
        }

        /// <summary>
        /// «Доба 7 · Ранок · Село». Фаза — як і раніше в шапці IMGUI: уранці й у
        /// вільній грі підпис веде <see cref="SessionState"/> (конвеєр лишає
        /// Phase=Night, доки гравець не натисне «Почати день»).
        /// </summary>
        public static string DayLine(SessionView view, SessionState state, Gender gender)
        {
            if (view == null) return string.Empty;
            string phaseKey;
            if (state == SessionState.Morning || state == SessionState.FreePlay)
                phaseKey = "ui.topbar.phase.morning";
            else
                phaseKey = view.Phase == Game.Core.Loop.DayPhase.Night ? "ui.topbar.phase.night" : "ui.topbar.phase.day";

            var parts = new List<string>(3)
            {
                UkrainianText.Format("ui.topbar.day", gender, "day", view.Day.ToString()),
                UkrainianText.Get(phaseKey, gender)
            };
            string tier = ScreenText.CrowdChip(view.CrowdBand, gender);
            if (!string.IsNullOrEmpty(tier)) parts.Add(tier);
            return string.Join(" · ", parts);
        }

        /// <summary>Уся драбина знизу вгору; невідома полоса — драбина без позначки поточного.</summary>
        public static List<HudLadderRung> LadderFor(string currentBand, Gender gender)
        {
            var result = new List<HudLadderRung>(BandOrder.Count);
            foreach (var band in BandOrder)
                result.Add(new HudLadderRung
                {
                    BandKey = band,
                    Word = ScreenText.MoodChip(band, gender),
                    IsCurrent = string.Equals(band, currentBand, StringComparison.OrdinalIgnoreCase)
                });
            return result;
        }

        private static List<string> LadderLines(string above, string current, string below, Gender gender)
        {
            var lines = new List<string>(3);
            if (above != null) lines.Add(UkrainianText.Format("ui.hud.ladder.above", gender, "band", above));
            lines.Add(UkrainianText.Format("ui.hud.ladder.current", gender, "band", current));
            if (below != null) lines.Add(UkrainianText.Format("ui.hud.ladder.below", gender, "band", below));
            return lines;
        }
    }
}
