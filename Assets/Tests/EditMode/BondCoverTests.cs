using System.Linq;
using System.Reflection;
using Game.Core.Balance;
using Game.Core.Characters;
using Game.Core.Combat;
using Game.Core.Companions;
using Game.Core.Session;
using Game.Core.Stats;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Трек C8 — зв'язки в бою (Поправка №14.8; власник — «Лишити, низький
    /// пріоритет»). Побратими (наявний зв'язок Kinship ростеру) поруч раз за раунд
    /// прикривають: ворог влучив в одного — другий б'є нападника у відповідь.
    /// </summary>
    public class BondCoverTests
    {
        private static readonly BalanceConfig Cfg = new BalanceConfig();

        private static WeaponDefinition Club(int dmg) => new WeaponDefinition("club", "Club", SkillType.Melee)
        { DamageMin = dmg, DamageMax = dmg, ApCost = 2, OptimalRange = 1 };

        private static CombatUnit Player(string id) =>
            new CombatUnit(id, Side.Player, new UnitProfile
            {
                DisplayName = id, MaxHp = 40, MaxAp = 8, Accuracy = 200, Initiative = 1, MoveApPerTile = 1
            }, Club(3));

        private static CombatUnit Raider() =>
            new CombatUnit("raider", Side.Enemy, new UnitProfile
            {
                DisplayName = "raider", MaxHp = 40, MaxAp = 8, Accuracy = 200, Initiative = 9, MoveApPerTile = 1
            }, Club(2));

        /// <summary>Нападник ходить першим і стоїть впритул до партнера; друг — біля обох.</summary>
        private static CombatState Scene(out CombatUnit partner, out CombatUnit friend, out CombatUnit raider,
                                         GridPos friendPos, bool bonded = true)
        {
            var cs = new CombatState(new GridMap(6, 3), Cfg, new ThresholdRule(Cfg), null);
            partner = Player("partner");
            friend = Player("friend");
            raider = Raider();
            cs.AddUnit(partner, new GridPos(2, 1));
            cs.AddUnit(friend, friendPos);
            cs.AddUnit(raider, new GridPos(3, 1));
            if (bonded) cs.AddBond(partner.Id, friend.Id);
            cs.Begin();
            Assert.AreSame(raider, cs.Current);
            return cs;
        }

        [Test]
        public void FriendNextToTheHitPartner_StrikesBack_OncePerRound()
        {
            var cs = Scene(out var partner, out var friend, out var raider, new GridPos(2, 0));

            Assert.AreEqual(CombatActionResult.Success, cs.Attack(partner.Id));
            int afterCover = raider.Hp;
            Assert.Less(afterCover, 40, "друг прикрив — удар у відповідь");
            Assert.AreEqual(1, cs.Journal.Count(e => e.Key == CombatLogKeys.BondCover));

            Assert.AreEqual(CombatActionResult.Success, cs.Attack(partner.Id));
            Assert.AreEqual(afterCover, raider.Hp, "раз за раунд");

            cs.EndTurn(); cs.EndTurn(); cs.EndTurn(); // раунд 2 — знову нападник
            Assert.AreSame(raider, cs.Current);
            cs.Attack(partner.Id);
            Assert.Less(raider.Hp, afterCover, "новий раунд — знову прикриває");
        }

        [Test]
        public void FarAway_OrNotBonded_NoCover()
        {
            var far = Scene(out var p1, out _, out var r1, new GridPos(0, 2));
            far.Attack(p1.Id);
            Assert.AreEqual(40, r1.Hp, "побратим далеко — не прикрив");

            var stranger = Scene(out var p2, out _, out var r2, new GridPos(2, 0), bonded: false);
            stranger.Attack(p2.Id);
            Assert.AreEqual(40, r2.Hp, "не побратим — не прикриває");
        }

        [Test]
        public void Session_BondsComeFromTheRoster_Kinship()
        {
            var s = new GameSession();
            s.NewGame(new NewGameOptions { SkipCreation = true, HitRule = HitRuleKind.Threshold });
            var step = s.AdvanceScene();
            while (!step.IsFinished) step = step.IsChoice ? s.ChooseSceneOption(0) : s.AdvanceScene();

            var who = s.GetRosterView().Companions.First(c => c.Id != GameSession.ProtagonistId && c.Status != CompanionStatus.Dead);
            s.TakeCaptive(who.Id, "enemy.horde_scout", 0, null, new[] { "enemy.horde_scout" });
            s.RaidCaptors(who.Id);

            // Єдина рефлексія у файлі: ростер — щоб порахувати очікувані пари тим самим RosterBonds.
            var roster = (Roster)typeof(GameSession).GetField("_worldRoster", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(s);
            var bonds = new RosterBonds(null);
            var squad = s.GetBattleView().Units.Where(u => u.Side == "Player").ToList();
            foreach (var a in squad)
                foreach (var b in squad)
                {
                    if (a.Id == b.Id) continue;
                    bool kin = bonds.Between(roster.Get(a.Id.Substring(2)), roster.Get(b.Id.Substring(2))) == BondType.Kinship;
                    Assert.AreEqual(kin, a.BondUnitIds.Contains(b.Id), a.Id + " / " + b.Id);
                }
        }
    }
}
