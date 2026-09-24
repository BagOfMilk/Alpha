using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;

namespace Game.Core.Companions
{
    /// <summary>
    /// Снимок ушедшего в антагонисты (US-9.4, порт B4): id + уровень на момент
    /// ухода. Гир/оружие сюда сознательно НЕ входят — <c>Companion.Equipment</c>
    /// заводит пакет B3 в параллельном воркчасті и в этом дереве недоступен;
    /// расширение записи (снимок гира, `ReturnGearOnKill`) — шов для D1/B3
    /// после фазы C (см. seamsForD1 отчёта пакета).
    /// </summary>
    public sealed class AntagonistRecord
    {
        public readonly string CompanionId;
        public readonly int Level;

        public AntagonistRecord(string companionId, int level)
        {
            CompanionId = companionId;
            Level = level;
        }
    }

    /// <summary>
    /// Уход напарника в антагонисты (US-9.4, R2/§2 №25): срабатывает при низкой
    /// лояльности (полоса ≤ Resentful), выдержанной N дней подряд, либо при
    /// сюжетном флаге «defector_seeded» (Поправка №7 R2/§4.6) плюс та же низкая
    /// полоса. Переход необратим (<see cref="Companion.MarkAntagonist"/>).
    /// Протагонист предать не может — решает вызывающий (протагонист не имеет
    /// собственного флага на Companion в этой модели, см. §4.5).
    /// </summary>
    public static class Defection
    {
        /// <summary>Флаг StoryFlags (A1, §4.6): низкая лояльность плюс сюжетный посев.</summary>
        public const string DefectorSeededFlag = "defector_seeded";

        private static readonly CompanionSocialBalance DefaultSocial = new CompanionSocialBalance();

        /// <summary>
        /// Готов ли напарник к уходу. <paramref name="consecutiveDaysAtOrBelowResentful"/>
        /// считает <see cref="DefectionWatch"/> (тикается раз в сутки — кто
        /// зовёт Tick(), решает D1, шов в seamsForD1: в DayStepOrder сегодня нет
        /// для этого отдельного шага).
        /// </summary>
        public static bool ShouldDefect(Companion c, bool isProtagonist,
            int consecutiveDaysAtOrBelowResentful, bool defectorSeeded, BalanceConfig cfg = null)
        {
            if (c == null || c.IsDead || isProtagonist) return false;
            if (c.Status == CompanionStatus.Antagonist) return false; // уже ушёл — не дефектит дважды
            if (c.LoyaltyBand > LoyaltyBand.Resentful) return false;

            var social = cfg?.CompanionSocial ?? DefaultSocial;
            if (consecutiveDaysAtOrBelowResentful >= social.DefectionDaysAtLowLoyalty) return true;
            return defectorSeeded;
        }

        /// <summary>Переводит в антагонисты: снимок уровня, снятие с позиции, статус.</summary>
        public static AntagonistRecord Defect(Companion c, BaseState baseState = null)
        {
            if (c == null) return null;
            var record = new AntagonistRecord(c.Id, c.Level);

            if (c.IsAssigned) baseState?.Unassign(c.AssignedSlotId);
            c.MarkAntagonist(); // необратимо (US-9.1)
            return record;
        }
    }

    /// <summary>
    /// Считает подряд идущие сутки на дне лояльности (Broken/Resentful) на
    /// напарника — вход для порога дефекции (R2/§2 №25 "N діб"). Персистится
    /// как IStateBlob (шов для D1: подключить фрагментом слепка рядом с
    /// eco=/sites=/flags=, §4.8 R13 — сейчас не подключено нигде).
    /// </summary>
    public sealed class DefectionWatch : Game.Core.Loop.IStateBlob
    {
        private readonly Dictionary<string, int> _daysLow = new Dictionary<string, int>();

        public int DaysAtOrBelowResentful(string companionId)
            => companionId != null && _daysLow.TryGetValue(companionId, out var d) ? d : 0;

        /// <summary>Вызывается раз в сутки конвейера для каждого напарника состава.</summary>
        public void Tick(Roster roster)
        {
            if (roster == null) return;
            foreach (var c in roster.All)
            {
                if (c.IsDead || c.Status == CompanionStatus.Antagonist)
                {
                    _daysLow.Remove(c.Id);
                    continue;
                }
                if (c.LoyaltyBand <= LoyaltyBand.Resentful)
                    _daysLow[c.Id] = DaysAtOrBelowResentful(c.Id) + 1;
                else
                    _daysLow.Remove(c.Id);
            }
        }

        public string CaptureState()
        {
            var keys = new List<string>(_daysLow.Keys);
            keys.Sort(System.StringComparer.Ordinal);
            var sb = new System.Text.StringBuilder();
            foreach (var k in keys)
            {
                if (sb.Length > 0) sb.Append(',');
                sb.Append(k).Append(':').Append(_daysLow[k]);
            }
            return sb.ToString();
        }

        public void RestoreState(string blob)
        {
            _daysLow.Clear();
            if (string.IsNullOrEmpty(blob)) return;
            foreach (var entry in blob.Split(','))
            {
                var f = entry.Split(':');
                if (f.Length < 2) continue;
                if (int.TryParse(f[1], out var v)) _daysLow[f[0]] = v;
            }
        }
    }
}
