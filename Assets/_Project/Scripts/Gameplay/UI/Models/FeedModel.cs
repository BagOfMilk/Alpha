using System;
using System.Collections.Generic;
using Game.Core.Characters.Creation;
using Game.Core.Session;
using Game.Core.Session.Views;
using Game.Gameplay.Text;

namespace Game.Gameplay.UI
{
    /// <summary>Зона стрічки згори донизу (docs/HUD_DESIGN.md §4.3).</summary>
    public enum FeedZone { Pending = 0, Important = 1, News = 2 }

    /// <summary>
    /// Серйозність рядка — колір і форма рамки (HUD_DESIGN §6.1). Колір НЕ
    /// кодує рівень Напруги (P-HUD-02): перехід полоси завжди <see cref="Mandatory"/>,
    /// незалежно від того, яка це полоса.
    /// </summary>
    public enum FeedSeverity { Neutral = 0, Mandatory = 1, Warning = 2, Danger = 3 }

    public sealed class FeedItem
    {
        public FeedZone Zone;
        public FeedSeverity Severity;
        public string Key;
        public string Text;
        /// <summary>Скільки однакових рядків підряд згорнуто в цей (×N); 1 — без повторів.</summary>
        public int Count = 1;
        /// <summary>Імена решти учасників групової реакції (ScreenText.FeedLine.AlsoNames).</summary>
        public IReadOnlyList<string> AlsoNames;
        public int Day;
    }

    public sealed class FeedPanel
    {
        public IReadOnlyList<FeedItem> Pending;
        public IReadOnlyList<FeedItem> Important;
        public IReadOnlyList<FeedItem> News;
        /// <summary>Скільки рядків новин не влізло в ліміт (вони лишаються в DayLog і хроніці).</summary>
        public int HiddenNews;
    }

    /// <summary>
    /// Модель стрічки без рушія (HUD_DESIGN §4.3, §8): три зони —
    /// «Потребує рішення», «Важливе за добу», «Новини». Обов'язковий сигнал
    /// (зміна полоси, ступінь передвісника) стоїть у закріпленій зоні і НЕ
    /// витісняється переповненими новинами (інв. 4, Статут UI-07).
    /// Перевіряється headless (<c>FeedModelTests</c>).
    /// </summary>
    public static class FeedModel
    {
        /// <summary>Скільки рядків новин показувати; решта — лише в хроніці.</summary>
        public const int DefaultMaxNews = 40;

        /// <summary>
        /// Обов'язковий сигнал за КЛЮЧЕМ події DayLog.
        /// ТИМЧАСОВО до поля [M] (HUD_DESIGN §4.3): публічний <see cref="GameEvent"/>
        /// не несе ознаки <c>SignalRequest.Mandatory</c>, тож класифікуємо за
        /// тими самими ключами, якими ядро кладе мандатні кандидати
        /// (<c>SignalComposer</c>): «tension.band.&lt;Band&gt;» і
        /// «forewarn.levelN». Коли ядро віддасть ознаку публічно, цей метод
        /// замінюється читанням поля — решта моделі не міняється.
        /// </summary>
        public static bool IsMandatory(GameEvent evt)
        {
            return TryBandChange(evt, out _) || ForewarnLevel(evt) > 0;
        }

        /// <summary>«tension.band.Murmur» → Murmur. Текстові ключі «tension.band.label.*»/«…risen» — не події зміни полоси.</summary>
        public static bool TryBandChange(GameEvent evt, out string band)
        {
            band = null;
            const string prefix = "tension.band.";
            if (evt?.Key == null || !evt.Key.StartsWith(prefix, StringComparison.Ordinal)) return false;
            string tail = evt.Key.Substring(prefix.Length);
            foreach (var name in HudHeaderModel.BandOrder)
            {
                if (string.Equals(name, tail, StringComparison.Ordinal))
                {
                    band = name;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Ступінь передвісника з ключа «forewarn.levelN»; 0 — не передвісник.</summary>
        public static int ForewarnLevel(GameEvent evt)
        {
            const string prefix = "forewarn.level";
            if (evt?.Key == null || !evt.Key.StartsWith(prefix, StringComparison.Ordinal)) return 0;
            int level;
            return int.TryParse(evt.Key.Substring(prefix.Length), out level) && level > 0 ? level : 0;
        }

        /// <summary>
        /// Закріплення «до кінця доби»: обов'язкові сигнали сьогоднішньої і щойно
        /// закритої доби. Перехід полоси народжується в конвеєрі дня, а гравець
        /// читає його вранці наступної доби — інакше зона була б порожня саме
        /// тоді, коли на неї дивляться.
        /// </summary>
        public static bool IsPinned(GameEvent evt, int currentDay) =>
            evt != null && IsMandatory(evt) && evt.Day >= currentDay - 1;

        public static FeedPanel Build(IReadOnlyList<GameEvent> log, PendingOfferView pending, int currentDay,
            Gender gender, RosterView roster, int maxNews = DefaultMaxNews)
        {
            var panel = new FeedPanel
            {
                Pending = BuildPending(pending, currentDay, gender),
                Important = new List<FeedItem>(),
                News = new List<FeedItem>()
            };
            if (log == null) return panel;

            var important = new List<FeedItem>();
            var rest = new List<GameEvent>(log.Count);
            for (int i = 0; i < log.Count; i++)
            {
                var evt = log[i];
                if (IsPinned(evt, currentDay)) continue;
                rest.Add(evt);
            }

            // Важливе — найновіше зверху; той самий текст підряд згортається.
            for (int i = log.Count - 1; i >= 0; i--)
            {
                var evt = log[i];
                if (!IsPinned(evt, currentDay)) continue;
                string text = ImportantText(evt, gender, roster);
                if (string.IsNullOrEmpty(text)) continue;
                if (important.Count > 0 && important[important.Count - 1].Text == text)
                {
                    important[important.Count - 1].Count++;
                    continue;
                }
                important.Add(new FeedItem
                {
                    Zone = FeedZone.Important,
                    Severity = SeverityOf(evt),
                    Key = evt.Key,
                    Text = text,
                    Day = evt.Day
                });
            }
            panel.Important = important;

            // Новини — той самий згортач, що й IMGUI-стрічка (×N, групові реакції).
            var lines = ScreenText.BuildFeedLines(rest, gender, roster);
            int limit = Math.Max(0, maxNews);
            var news = new List<FeedItem>(Math.Min(limit, lines.Count));
            for (int i = 0; i < lines.Count && news.Count < limit; i++)
            {
                news.Add(new FeedItem
                {
                    Zone = FeedZone.News,
                    Severity = FeedSeverity.Neutral,
                    Text = lines[i].Text,
                    Count = lines[i].Count,
                    AlsoNames = lines[i].AlsoNames
                });
            }
            panel.News = news;
            panel.HiddenNews = Math.Max(0, lines.Count - news.Count);
            return panel;
        }

        /// <summary>Повний рядок для показу: текст, «×N» і «також: …» — однаково для обох видів.</summary>
        public static string DisplayText(FeedItem item, Gender gender)
        {
            if (item == null) return string.Empty;
            string text = item.Text ?? string.Empty;
            if (item.Count > 1)
                text += " " + UkrainianText.Format("ui.feed.repeat", gender, "count", item.Count.ToString());
            if (item.AlsoNames != null && item.AlsoNames.Count > 0)
                text += " (" + UkrainianText.Format("ui.feed.also", gender, "names", string.Join(", ", item.AlsoNames)) + ")";
            return text;
        }

        private static List<FeedItem> BuildPending(PendingOfferView pending, int currentDay, Gender gender)
        {
            var list = new List<FeedItem>(1);
            if (pending == null) return list;
            string titleKey = (pending.TopicId ?? string.Empty) + ".title";
            string title = !string.IsNullOrEmpty(pending.TopicId) && UkrainianText.Has(titleKey, gender)
                ? UkrainianText.Get(titleKey, gender)
                : UkrainianText.Get("ui.hud.pending.generic", gender);
            list.Add(new FeedItem
            {
                Zone = FeedZone.Pending,
                Severity = pending.IsCrisis ? FeedSeverity.Danger : FeedSeverity.Warning,
                Key = pending.TopicId,
                Text = title,
                Day = currentDay
            });
            return list;
        }

        /// <summary>Рядок події + слово полоси в дужках для переходу («… (Бродіння)», HUD_DESIGN §5.1). Лише слово — жодного числа.</summary>
        private static string ImportantText(GameEvent evt, Gender gender, RosterView roster)
        {
            string text = ScreenText.EventLine(evt, gender, roster);
            string band;
            if (TryBandChange(evt, out band))
            {
                string word = ScreenText.MoodChip(band, gender);
                if (!string.IsNullOrEmpty(word)) text = string.IsNullOrEmpty(text) ? word : text + " (" + word + ")";
            }
            return text;
        }

        private static FeedSeverity SeverityOf(GameEvent evt)
        {
            int level = ForewarnLevel(evt);
            if (level >= 3) return FeedSeverity.Danger;
            if (level > 0) return FeedSeverity.Warning;
            return FeedSeverity.Mandatory;
        }
    }
}
