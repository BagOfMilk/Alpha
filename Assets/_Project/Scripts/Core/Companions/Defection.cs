using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;

namespace Game.Core.Companions
{
    /// <summary>
    /// Знімок того, хто пішов в антагоністи (US-9.4, порт B4): id + рівень на момент
    /// відходу. Гір/зброя сюди свідомо НЕ входять — <c>Companion.Equipment</c>
    /// заводить пакет B3 у паралельній робочій частині і в цьому дереві недоступний;
    /// розширення запису (знімок гіра, `ReturnGearOnKill`) — шов для D1/B3
    /// після фази C (див. seamsForD1 звіту пакета).
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
    /// Перехід напарника в антагоністи (US-9.4, R2/§2 №25): спрацьовує при низькій
    /// лояльності (смуга ≤ Resentful), витриманій N днів поспіль, або при
    /// сюжетному флагу «defector_seeded» (Поправка №7 R2/§4.6) плюс та сама низька
    /// смуга. Перехід незворотний (<see cref="Companion.MarkAntagonist"/>).
    /// Протагоніст зрадити не може — вирішує викликач (протагоніст не має
    /// власного флага на Companion у цій моделі, див. §4.5).
    /// </summary>
    public static class Defection
    {
        /// <summary>Флаг StoryFlags (A1, §4.6): низька лояльність плюс сюжетний посів.</summary>
        public const string DefectorSeededFlag = "defector_seeded";

        private static readonly CompanionSocialBalance DefaultSocial = new CompanionSocialBalance();

        /// <summary>
        /// Чи готовий напарник до відходу. <paramref name="consecutiveDaysAtOrBelowResentful"/>
        /// рахує <see cref="DefectionWatch"/> (тікається раз на добу — хто
        /// кличе Tick(), вирішує D1, шов у seamsForD1: у DayStepOrder сьогодні немає
        /// для цього окремого кроку).
        /// </summary>
        public static bool ShouldDefect(Companion c, bool isProtagonist,
            int consecutiveDaysAtOrBelowResentful, bool defectorSeeded, BalanceConfig cfg = null)
        {
            if (c == null || c.IsDead || isProtagonist) return false;
            if (c.Status == CompanionStatus.Antagonist) return false; // вже пішов — не дефектить двічі
            if (c.LoyaltyBand > LoyaltyBand.Resentful) return false;

            var social = cfg?.CompanionSocial ?? DefaultSocial;
            if (consecutiveDaysAtOrBelowResentful >= social.DefectionDaysAtLowLoyalty) return true;
            return defectorSeeded;
        }

        /// <summary>Переводить в антагоністи: знімок рівня, зняття з позиції, статус.</summary>
        public static AntagonistRecord Defect(Companion c, BaseState baseState = null)
        {
            if (c == null) return null;
            var record = new AntagonistRecord(c.Id, c.Level);

            if (c.IsAssigned) baseState?.Unassign(c.AssignedSlotId);
            c.MarkAntagonist(); // незворотно (US-9.1)
            return record;
        }
    }

    /// <summary>
    /// Рахує послідовні доби на дні лояльності (Broken/Resentful) на
    /// напарника — вхід для порогу дефекції (R2/§2 №25 "N діб"). Персистується
    /// як IStateBlob (шов для D1: підключити фрагментом зліпка поряд з
    /// eco=/sites=/flags=, §4.8 R13 — зараз не підключено ніде).
    /// </summary>
    public sealed class DefectionWatch : Game.Core.Loop.IStateBlob
    {
        private readonly Dictionary<string, int> _daysLow = new Dictionary<string, int>();

        public int DaysAtOrBelowResentful(string companionId)
            => companionId != null && _daysLow.TryGetValue(companionId, out var d) ? d : 0;

        /// <summary>Викликається раз на добу конвеєра для кожного напарника складу.</summary>
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
