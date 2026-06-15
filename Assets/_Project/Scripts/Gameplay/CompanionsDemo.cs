using System.Text;
using Game.Core;
using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Combat;
using Game.Core.Companions;
using Game.Core.Economy;
using Game.Core.Items;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Драма ростера (Эпик 9) в консоли: ценностные связи и баентер → рябь лояльности
    /// от смерти (по связям, с ограждением от каскада) → уход в антагонисты с гиром и
    /// уровнями → бой с боссом-перебежчиком → возврат гира убийством. Сид фиксирован.
    /// </summary>
    public sealed class CompanionsDemo : MonoBehaviour
    {
        [Tooltip("Необязательно. Если пусто — дефолтный баланс из кода.")]
        public BalanceConfigAsset balanceAsset;
        public int seed = 42;
        public bool runOnStart = true;

        private void Start()
        {
            if (runOnStart) RunDrama();
        }

        [ContextMenu("Run Drama")]
        public void RunDrama()
        {
            var cfg = balanceAsset != null ? balanceAsset.ToConfig() : new BalanceConfig();
            var roster = new Roster();
            foreach (var bg in DefaultContent.AllBackgrounds())
                roster.Add(bg.CreateInstance(bg.Id, cfg));
            roster.Get("leader").IsProtagonist = true;
            var baseState = new BaseState(roster, new ResourceLedger(), cfg);

            var bonds = new RosterBonds(DefaultValues.System());
            var drama = new RosterDrama(bonds, cfg);

            // --- Связи и баентер ---
            var medic = roster.Get("medic");
            var negotiator = roster.Get("negotiator");
            var brawler = roster.Get("brawler");
            var sb = new StringBuilder("=== ЦЕННОСТНЫЕ СВЯЗИ ===\n");
            sb.AppendLine($"  {medic.DisplayName} ↔ {negotiator.DisplayName}: {bonds.Between(medic, negotiator)}");
            sb.AppendLine($"  {medic.DisplayName} ↔ {brawler.DisplayName}: {bonds.Between(medic, brawler)}");
            sb.AppendLine("Баентер:");
            sb.AppendLine("  " + BanterPicker.Pick(medic, negotiator, bonds.Between(medic, negotiator)));
            sb.AppendLine("  " + BanterPicker.Pick(medic, brawler, bonds.Between(medic, brawler)));
            Debug.Log(sb.ToString());

            // --- Смерть и рябь по связям (с ограждением от каскада) ---
            Debug.Log($"Лояльности до: медик {medic.Loyalty} ({medic.LoyaltyBand}), боец {brawler.Loyalty}");
            negotiator.Kill();
            var ripple = drama.OnDeath(roster, "negotiator");
            var rs = new StringBuilder($"=== ПОГИБ: {negotiator.DisplayName} — рябь по ростеру ===\n");
            foreach (var e in ripple.Effects)
                rs.AppendLine($"  {roster.Get(e.CompanionId).DisplayName}: {(e.LoyaltyDelta >= 0 ? "+" : "")}{e.LoyaltyDelta} лояльности — {e.Note} ({e.Bond})");
            rs.Append("(тяжёлый отклик ограничен MaxRippleTargets — одна смерть не рушит весь ростер)");
            Debug.Log(rs.ToString());

            // --- Уход в антагонисты с гиром → босс → возврат гира ---
            // Боец на дне лояльности + надетая именная пушка.
            brawler.Equipment.Equip(ItemInstance.NamedFrom(DefaultItems.Widowmaker()));
            while (brawler.LoyaltyBand != LoyaltyBand.Resentful) brawler.AdjustLoyalty(-10);
            var ds = new StringBuilder("=== ПРЕДАТЕЛЬСТВО ===\n");
            ds.AppendLine($"  {brawler.DisplayName}: лояльность на дне ({brawler.LoyaltyBand}), готов уйти: {DefectionSystem.ShouldDefect(brawler)}");

            var record = DefectionSystem.Defect(brawler, baseState);
            ds.AppendLine($"  Ушёл в антагонисты со своим гиром (ур.{record.Level}, оружие «{record.Weapon?.DisplayName}», предметов: {record.CapturedGear.Count}). Статус: {brawler.Status}");

            // Бой с боссом-перебежчиком.
            var won = FightBoss(roster, brawler, cfg, seed);
            ds.AppendLine($"  Бой с боссом: {(won ? "босс повержен" : "отряд не справился")}");
            if (won)
            {
                DefectionSystem.ReturnGearOnKill(record, baseState.Inventory);
                ds.AppendLine($"  Гир возвращён в сташ: {baseState.Inventory.Count} предмет(ов) " +
                              $"(включая «{record.Weapon?.DisplayName}»)");
            }
            Debug.Log(ds.ToString());
        }

        /// <summary>Стычка отряда (3 лоялиста) против босса-перебежчика.</summary>
        private static bool FightBoss(Roster roster, Companion defector, BalanceConfig cfg, int seed)
        {
            var cs = new CombatState(new GridMap(8, 3), cfg, new SeededRng(seed));
            var abilities = DefaultContent.AbilityCatalog();
            cs.AddUnit(CombatUnit.FromCompanion(roster.Get("marksman"), DefaultContent.Rifle(), cfg, abilities), new GridPos(0, 0));
            cs.AddUnit(CombatUnit.FromCompanion(roster.Get("medic"), DefaultContent.Rifle(), cfg, abilities), new GridPos(0, 1));
            cs.AddUnit(CombatUnit.FromCompanion(roster.Get("leader"), DefaultContent.Rifle(), cfg, abilities), new GridPos(0, 2));
            cs.AddUnit(DefectionSystem.BuildBossUnit(defector, cfg, abilities), new GridPos(7, 1));
            cs.Begin();
            CombatDemo.AutoBattle(cs, cfg, 400);
            return cs.Outcome == CombatOutcome.Victory;
        }
    }
}
