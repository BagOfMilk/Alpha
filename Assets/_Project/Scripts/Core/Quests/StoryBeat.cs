using System.Collections.Generic;
using Game.Core.Characters;

namespace Game.Core.Quests
{
    /// <summary>
    /// Сюжетный бит с цепочкой дублёров (US-14.1): спайн переживает смерть носителя.
    /// CriticalSafe — ограничение на этапе письма: сюжетно-критические биты несёт
    /// протагонист или лояльно-надёжные лица (не способные предать), поэтому уход
    /// в боссы ничего не рвёт.
    /// </summary>
    public sealed class StoryBeat
    {
        public string Id;
        public string PreferredDelivererId;
        public readonly List<string> Understudies = new List<string>();
        public bool CriticalSafe;
        public string NarratorFallback = "(голос за кадром)";

        public StoryBeat(string id, string preferredDelivererId)
        {
            Id = id;
            PreferredDelivererId = preferredDelivererId;
        }

        public StoryBeat Understudy(string companionId) { Understudies.Add(companionId); return this; }
        public StoryBeat Critical() { CriticalSafe = true; return this; }

        /// <summary>Все потенциальные носители по порядку приоритета.</summary>
        public IEnumerable<string> CarriersInOrder()
        {
            if (!string.IsNullOrEmpty(PreferredDelivererId)) yield return PreferredDelivererId;
            for (int i = 0; i < Understudies.Count; i++) yield return Understudies[i];
        }
    }

    /// <summary>Кто понёс бит: дублёр или нарратор-фолбэк (флавор-ack).</summary>
    public readonly struct BeatDelivery
    {
        public readonly string DelivererId;     // null → нарратор
        public readonly bool IsNarratorFallback;
        public readonly string Text;

        public BeatDelivery(string delivererId, bool narrator, string text)
        {
            DelivererId = delivererId;
            IsNarratorFallback = narrator;
            Text = text;
        }
    }

    /// <summary>
    /// Разрешение носителя бита по живому ростеру (US-14.1): первый ЖИВОЙ и
    /// не-антагонист в цепочке; если все выбыли — нарратор-фолбэк.
    /// </summary>
    public static class DelivererChain
    {
        public static BeatDelivery Resolve(StoryBeat beat, Roster roster)
        {
            if (beat != null && roster != null)
            {
                foreach (var id in beat.CarriersInOrder())
                {
                    var c = roster.Get(id);
                    if (c != null && c.IsAlive && c.Status != CompanionStatus.Antagonist)
                        return new BeatDelivery(c.Id, false, c.DisplayName);
                }
            }
            return new BeatDelivery(null, true, beat?.NarratorFallback ?? "(голос за кадром)");
        }

        /// <summary>
        /// Проверка ограничения письма (US-14.1): критический бит несут только
        /// «надёжные» — протагонист или преданные (LoyaltyBand.Devoted). Если хоть
        /// один носитель ненадёжен — бит назначен неверно.
        /// </summary>
        public static bool IsCriticalSafe(StoryBeat beat, Roster roster)
        {
            if (beat == null || !beat.CriticalSafe || roster == null) return true;
            foreach (var id in beat.CarriersInOrder())
            {
                var c = roster.Get(id);
                if (c == null) continue;
                if (c.IsProtagonist) continue;
                if (c.LoyaltyBand == LoyaltyBand.Devoted) continue;
                return false; // способный предать носит критбит — нарушение
            }
            return true;
        }
    }
}
