using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Combat;
using Game.Core.Economy;
using Game.Core.Factions;
using Game.Core.Threats;

namespace Game.Core.Saves
{
    /// <summary>
    /// Сессия кампании — то, что сохраняется/загружается (US-16.1). Агрегирует
    /// состояние города: база (день/тир/население/ресурсы/инвентарь/ростер/угрозы/
    /// позиции), фракции и сюжетные флаги. Гибридные сейвы: свободно в базе, под
    /// айронменом в вылазке быстрый сейв заблокирован.
    /// </summary>
    public sealed class Campaign
    {
        public BalanceConfig Cfg { get; }
        public BaseState Base { get; }
        public FactionRegistry Factions { get; }
        public readonly HashSet<string> Flags = new HashSet<string>();

        public bool Ironman { get; set; }

        /// <summary>Отряд вне базы (в пути/вылазке) — влияет на доступность быстрого сейва.</summary>
        public bool InExpedition { get; set; }

        public Roster Roster => Base.Roster;

        public Campaign(BalanceConfig cfg, BaseState baseState, FactionRegistry factions)
        {
            Cfg = cfg ?? new BalanceConfig();
            Base = baseState;
            Factions = factions;
        }

        /// <summary>Свободный сейв в базе; в вылазке под айронменом — нельзя (US-16.1).</summary>
        public bool CanQuickSave => !(Ironman && InExpedition);

        /// <summary>Новая игра: дефолтный стартовый ростер/база/фракции/угрозы.</summary>
        public static Campaign NewGame(BalanceConfig cfg)
        {
            cfg = cfg ?? new BalanceConfig();
            var roster = new Roster();
            foreach (var bg in DefaultContent.AllBackgrounds())
                roster.Add(bg.CreateInstance(bg.Id, cfg));
            var leader = roster.Get("leader");
            if (leader != null) leader.IsProtagonist = true;

            var baseState = new BaseState(roster, new ResourceLedger(), cfg);
            foreach (var slot in DefaultContent.AllSlots()) baseState.AddSlot(slot);
            baseState.AttachThreats(new ThreatSystem(cfg, new SeededRng(0),
                DefaultContent.IncidentPool(), DefaultContent.TensionSpikes()));

            return new Campaign(cfg, baseState, DefaultFactions.NewRegistry()) { Ironman = cfg.Ironman };
        }
    }
}
