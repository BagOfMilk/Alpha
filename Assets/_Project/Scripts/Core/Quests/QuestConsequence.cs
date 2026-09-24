using System;
using System.Collections.Generic;

namespace Game.Core.Quests
{
    /// <summary>
    /// Наслідок рішення квесту — ДАНІ, а не дія (R6). Квестовий рушій сам
    /// нічого не застосовує: ні до Напруги, ні до фракцій, ні до лояльності,
    /// ні до інвентаря. Він повертає ЦЕЙ список, а застосовує D1 — точно так
    /// само, як <c>CheckOutcome</c> повертає полосу, а хто ранить чи хвалить
    /// вирішує вже той, хто просив перевірку.
    ///
    /// Навіщо саме так: пакет B6 паралельний B1 (бій)/B4 (лояльність/дефекція)/
    /// B5 (фракції) — жодного з їхніх типів ще нема в цьому робочому дереві.
    /// Дані замість викликів — це і є розв'язка §1.1 «декуплінг через
    /// id/рядки/малі інтерфейси».
    ///
    /// <see cref="TensionDelta"/> — ЄДИНЕ число, яке ця структура несе для
    /// Напруги, і воно завжди йде через
    /// <c>DayProcessor.QueueExternal(TensionDriver.QuestChoice, amount)</c>
    /// (R6, інваріант 5: список драйверів закритий). Тип навмисно НЕ містить
    /// поля <c>TensionDriver</c> — інакше квест міг би підмінити драйвер, а
    /// закритий список став би питанням дисципліни, а не структури
    /// (перевірено <c>ArchitectureGuardTests.Quests_And_Story_...</c>).
    /// </summary>
    public sealed class QuestConsequence
    {
        /// <summary>
        /// Новий порожній наслідок — безпечний дефолт замість null. НЕ статичний
        /// синглтон навмисно: fluent-будівельники мутують інстанс на місці, і
        /// спільний об'єкт-заглушка одним випадковим <c>.Tension(...)</c> зіпсував
        /// би собою всі місця, де використаний дефолт.
        /// </summary>
        public static QuestConsequence Empty() => new QuestConsequence();

        /// <summary>Дельта для <c>TensionDriver.QuestChoice</c> (додатне — росте, від'ємне — падає).</summary>
        public int TensionDelta;

        /// <summary>Id фракції → дельта. Фракцій (B5) тут ще нема — це лише дані на потім.</summary>
        public readonly Dictionary<string, int> FactionDeltas = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>Id напарника → дельта лояльності. <c>Companion.LoyaltyBand</c> (B4) тут ще нема.</summary>
        public readonly Dictionary<string, int> LoyaltyDeltas = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>Сюжетні флаги, які виставити (через <c>StoryFlags.Set</c>).</summary>
        public readonly List<string> Flags = new List<string>();

        /// <summary>Id іменних предметів (B3), які видати.</summary>
        public readonly List<string> ItemIds = new List<string>();

        /// <summary>Досвід протагоністу/учасникам — хто саме отримує, вирішує D1.</summary>
        public int Xp;

        public bool IsEmpty =>
            TensionDelta == 0 && Xp == 0 &&
            FactionDeltas.Count == 0 && LoyaltyDeltas.Count == 0 &&
            Flags.Count == 0 && ItemIds.Count == 0;

        // ---- fluent-будівельники для контенту (DefaultQuests) ----

        public QuestConsequence Tension(int amount) { TensionDelta = amount; return this; }
        public QuestConsequence Faction(string factionId, int delta)
        {
            if (!string.IsNullOrEmpty(factionId)) FactionDeltas[factionId] = delta;
            return this;
        }
        public QuestConsequence Loyalty(string companionId, int delta)
        {
            if (!string.IsNullOrEmpty(companionId)) LoyaltyDeltas[companionId] = delta;
            return this;
        }
        public QuestConsequence Flag(string flagId)
        {
            if (!string.IsNullOrEmpty(flagId)) Flags.Add(flagId);
            return this;
        }
        public QuestConsequence Item(string itemId)
        {
            if (!string.IsNullOrEmpty(itemId)) ItemIds.Add(itemId);
            return this;
        }
        public QuestConsequence WithXp(int amount) { Xp = amount; return this; }

        /// <summary>Об'єднати два наслідки (перехід + фінальна нагорода термінала).</summary>
        public static QuestConsequence Merge(QuestConsequence a, QuestConsequence b)
        {
            if (a == null || a.IsEmpty) return b ?? Empty();
            if (b == null || b.IsEmpty) return a;

            var result = new QuestConsequence
            {
                TensionDelta = a.TensionDelta + b.TensionDelta,
                Xp = a.Xp + b.Xp
            };
            foreach (var kv in a.FactionDeltas) result.FactionDeltas[kv.Key] = kv.Value;
            foreach (var kv in b.FactionDeltas)
                result.FactionDeltas[kv.Key] = result.FactionDeltas.TryGetValue(kv.Key, out var v) ? v + kv.Value : kv.Value;
            foreach (var kv in a.LoyaltyDeltas) result.LoyaltyDeltas[kv.Key] = kv.Value;
            foreach (var kv in b.LoyaltyDeltas)
                result.LoyaltyDeltas[kv.Key] = result.LoyaltyDeltas.TryGetValue(kv.Key, out var v) ? v + kv.Value : kv.Value;
            result.Flags.AddRange(a.Flags);
            result.Flags.AddRange(b.Flags);
            result.ItemIds.AddRange(a.ItemIds);
            result.ItemIds.AddRange(b.ItemIds);
            return result;
        }
    }
}
