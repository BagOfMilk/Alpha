using System;
using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Characters;
using Game.Core.Combat;
using Game.Core.Saves;
using Game.Core.Threats;

namespace Game.Core.Story
{
    /// <summary>Состав финального штурма: чем выше Готовность, тем жиже волна нападающих.</summary>
    public sealed class FinaleEncounter
    {
        public ReadinessBand Band;
        public readonly List<EnemyDefinition> Enemies = new List<EnemyDefinition>();

        /// <summary>Бонус точности защитникам от Укреплений/подготовки (Fortified прикрывает своих).</summary>
        public int DefenderAccuracyBonus;

        /// <summary>Сколько бойцов орда набрала, пока город тянул с финалом (US-11.4).</summary>
        public int HordeReinforcements;
    }

    public sealed class FinaleReport
    {
        public bool Won;
        public ReadinessBand Band;
        public CampaignOutcome Outcome;
        public string Text;
    }

    /// <summary>
    /// Финальная битва кампании (US-11.4/14.2): запускается СЮЖЕТНОЙ ВЕХОЙ (флаг
    /// спайна, не порогом — доом-клока нет), а скрытая «Готовность» (совет +
    /// Укрепления + сила ростера) формирует состав и сложность. Победа завершает
    /// кампанию; поражение — единственный game over вне айронмена (US-16.2).
    /// Бой ведёт вызывающий код (CombatState) и возвращает исход в Resolve.
    /// </summary>
    public static class FinalBattle
    {
        /// <summary>Сюжетная веха: ставится финальным квестом спайна (US-14.2).</summary>
        public const string ReadyFlag = "finale_ready";
        public const string WonFlag = "campaign_won";

        /// <summary>Ачивка айронмен-победы (US-16.1: ачивки только в айронмене).</summary>
        public const string IronVictoryAchievement = "iron_city_stands";

        /// <summary>Сколько врагов физически помещается на арену (спавны Battle-сцены).</summary>
        public const int ArenaEnemyCapacity = 12;

        public static bool IsUnlocked(ICollection<string> flags)
            => flags != null && flags.Contains(ReadyFlag);

        /// <summary>
        /// Эффективная Готовность (US-11.4): шкала (совет + Укрепления) + сила
        /// ростера — живые боеспособные напарники и их уровни.
        /// </summary>
        public static double EffectiveReadiness(ThreatSystem threats, Roster roster, BalanceConfig cfg)
        {
            double value = threats != null ? threats.Readiness.Value : 0;
            if (roster != null)
            {
                foreach (var c in roster.All)
                {
                    if (!c.IsAlive || c.Status == CompanionStatus.Antagonist) continue;
                    value += cfg.ReadinessPerAliveCompanion + c.Level * cfg.ReadinessPerCompanionLevel;
                }
            }
            return value;
        }

        public static ReadinessBand BandFor(double effectiveReadiness, BalanceConfig cfg)
        {
            if (effectiveReadiness >= cfg.ReadinessFortifiedAt) return ReadinessBand.Fortified;
            if (effectiveReadiness >= cfg.ReadinessBracedAt) return ReadinessBand.Braced;
            return ReadinessBand.Unprepared;
        }

        /// <summary>
        /// Состав штурма по полосе Готовности (детерминирован): неготовый город
        /// встречает полную волну с чумоносцем; укреплённый — поредевшую, и стены
        /// дают защитникам бонус точности. Сложность составом, не HP (US-3.15).
        /// </summary>
        public static FinaleEncounter BuildEncounter(ReadinessBand band)
        {
            var enc = new FinaleEncounter { Band = band };
            switch (band)
            {
                case ReadinessBand.Fortified:
                    enc.DefenderAccuracyBonus = 10;
                    enc.Enemies.Add(DefaultContent.RaiderBruiser());
                    enc.Enemies.Add(DefaultContent.ScavGunner());
                    enc.Enemies.Add(DefaultContent.RustDrone());
                    enc.Enemies.Add(DefaultContent.FeralGhoul());
                    enc.Enemies.Add(DefaultContent.FeralGhoul());
                    break;

                case ReadinessBand.Braced:
                    enc.DefenderAccuracyBonus = 5;
                    enc.Enemies.Add(DefaultContent.RaiderBruiser());
                    enc.Enemies.Add(DefaultContent.ScavGunner());
                    enc.Enemies.Add(DefaultContent.ScavGunner());
                    enc.Enemies.Add(DefaultContent.RustDrone());
                    enc.Enemies.Add(DefaultContent.FeralGhoul());
                    enc.Enemies.Add(DefaultContent.FeralGhoul());
                    break;

                default: // Unprepared: полная волна + Чумоносец (токсин-контролёр)
                    enc.Enemies.Add(DefaultContent.RaiderBruiser());
                    enc.Enemies.Add(DefaultContent.RaiderBruiser());
                    enc.Enemies.Add(DefaultContent.ScavGunner());
                    enc.Enemies.Add(DefaultContent.ScavGunner());
                    enc.Enemies.Add(DefaultContent.RustDrone());
                    enc.Enemies.Add(DefaultContent.PlagueBearer());
                    enc.Enemies.Add(DefaultContent.FeralGhoul());
                    enc.Enemies.Add(DefaultContent.FeralGhoul());
                    break;
            }
            return enc;
        }

        /// <summary>Удобная сборка: веха проверяется, полоса считается из состояния кампании.</summary>
        public static FinaleEncounter BuildEncounter(Campaign campaign)
        {
            if (campaign == null) throw new ArgumentNullException(nameof(campaign));
            if (campaign.Outcome != CampaignOutcome.Ongoing)
                throw new InvalidOperationException("Кампания уже завершена — финал не переигрывается (US-16.2)");
            if (!IsUnlocked(campaign.Flags))
                throw new InvalidOperationException("Финал ещё не открыт сюжетной вехой (US-14.2)");
            var band = BandFor(
                EffectiveReadiness(campaign.Base.ThreatsSystem, campaign.Roster, campaign.Cfg), campaign.Cfg);
            var encounter = BuildEncounter(band);

            // Орда СОБИРАЕТСЯ, пока город тянет (US-11.4): каждые
            // HordeGrowthDays дней после вехи финала добавляют бойца в штурм.
            // Без этого время после вехи ничего не стоило: можно было бесконечно
            // качаться и фармить, а финал ждал в одном и том же составе.
            int delay = campaign.DaysSinceFinaleReady();
            int extra = campaign.Cfg.HordeGrowthDays > 0 ? delay / campaign.Cfg.HordeGrowthDays : 0;
            int added = 0;
            for (int i = 0; i < extra && encounter.Enemies.Count < ArenaEnemyCapacity; i++, added++)
                encounter.Enemies.Add(i % 2 == 0
                    ? DefaultContent.FeralGhoul() : DefaultContent.ScavGunner());
            encounter.HordeReinforcements = added; // ровно то, что реально доехало до арены
            return encounter;
        }

        /// <summary>Исход финала: победа завершает кампанию, поражение — game over (US-16.2).</summary>
        public static FinaleReport Resolve(Campaign campaign, bool won)
        {
            if (campaign == null) throw new ArgumentNullException(nameof(campaign));
            if (campaign.Outcome != CampaignOutcome.Ongoing)
                throw new InvalidOperationException("Кампания уже завершена — финал не переигрывается (US-16.2)");
            if (!IsUnlocked(campaign.Flags))
                throw new InvalidOperationException("Финал ещё не открыт сюжетной вехой (US-14.2)");

            var band = BandFor(
                EffectiveReadiness(campaign.Base.ThreatsSystem, campaign.Roster, campaign.Cfg), campaign.Cfg);
            campaign.Outcome = won ? CampaignOutcome.Won : CampaignOutcome.Lost;
            if (won)
            {
                campaign.Flags.Add(WonFlag);
                campaign.TryUnlockAchievement(IronVictoryAchievement); // только в айронмене
            }

            return new FinaleReport
            {
                Won = won,
                Band = band,
                Outcome = campaign.Outcome,
                Text = won
                    ? "Штурм отбит. Город выстоял — кампания завершена победой."
                    : "Оборона пала. Город потерян."
            };
        }
    }
}
