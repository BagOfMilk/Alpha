using System.Collections.Generic;
using System.Linq;
using Game.Core.Balance;
using Game.Core.Combat;
using Game.Core.Dungeons;
using Game.Core.Loop;
using Game.Core.Session;
using Game.Core.Stats;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Трек C3 — поле бою (Поправка №14.4; власник, 29.09.2026: «ти не додав
    /// взагалі укриття та взриваючі бочки чи щось таке»). Об'єкти дають укриття
    /// сусідам, бочки вибухають ланцюгом, укриття руйнується поетапно, сіно
    /// горить, підкріплення приходить за відліком, ШІ бачить те саме, що й
    /// гравець, а шаблони арен валідні.
    /// </summary>
    public class BattleFieldTests
    {
        private static readonly BalanceConfig Cfg = new BalanceConfig();

        private static CombatUnit U(string id, Side side, int init = 5, int hp = 20, WeaponDefinition w = null)
        {
            var p = new UnitProfile
            {
                DisplayName = id, MaxHp = hp, MaxAp = 8, Accuracy = 70,
                Initiative = init, MoveApPerTile = 1
            };
            return new CombatUnit(id, side, p, w);
        }

        private static WeaponDefinition Bow() => new WeaponDefinition("bow", "Лук", SkillType.Ranged)
        { DamageMin = 3, DamageMax = 3, ApCost = 3, OptimalRange = 8 };

        private static CombatState Field(int w = 9, int h = 5) => new CombatState(new GridMap(w, h), Cfg, new ThresholdRule(Cfg), null);

        // ---------------- укриття від об'єктів ----------------

        [Test]
        public void LowObject_GivesHalfCover_ToOrthogonalNeighbours_FromItsSide()
        {
            var cs = Field();
            var obj = new GridPos(4, 2);
            cs.AddObject(MapObjectKind.LowCover, obj);

            Assert.IsFalse(cs.Map.IsWalkable(obj), "об'єкт займає клітинку");
            Assert.IsFalse(cs.Map.BlocksSight(obj), "низька перепона огляду не закриває");
            Assert.AreEqual(CoverType.Half, cs.Map.GetCover(new GridPos(3, 2), Direction.East), "захід від об'єкта — укриття зі сходу");
            Assert.AreEqual(CoverType.Half, cs.Map.GetCover(new GridPos(5, 2), Direction.West));
            Assert.AreEqual(CoverType.Half, cs.Map.GetCover(new GridPos(4, 3), Direction.South));
            Assert.AreEqual(CoverType.Half, cs.Map.GetCover(new GridPos(4, 1), Direction.North));
            Assert.AreEqual(CoverType.None, cs.Map.GetCover(new GridPos(3, 2), Direction.West), "з протилежного боку — фланг");
            Assert.AreEqual(CoverType.None, cs.Map.GetCover(new GridPos(3, 3), Direction.East), "діагональ укриття не дає");
        }

        [Test]
        public void HighObject_GivesFullCover_AndBlocksSight()
        {
            var cs = Field();
            cs.AddObject(MapObjectKind.HighCover, new GridPos(4, 2));
            Assert.AreEqual(CoverType.Full, cs.Map.GetCover(new GridPos(3, 2), Direction.East));
            Assert.IsTrue(cs.Map.BlocksSight(new GridPos(4, 2)));
        }

        // ---------------- вибух, ланцюг, руйнування ----------------

        [Test]
        public void Keg_Explodes_HurtsEveryoneInRadius_ChainsToNextKeg_AndDegradesCoverOneStep()
        {
            var cs = Field(12, 5);
            var shooter = U("shooter", Side.Player, init: 9, w: Bow());
            var e1 = U("e1", Side.Enemy, init: 1);
            var e2 = U("e2", Side.Enemy, init: 1);
            var far = U("far", Side.Enemy, init: 1);
            cs.AddObject(MapObjectKind.PowderKeg, new GridPos(6, 2));
            cs.AddObject(MapObjectKind.PowderKeg, new GridPos(7, 2));   // ланцюг
            cs.AddObject(MapObjectKind.HighCover, new GridPos(8, 3));   // у радіусі другої бочки
            cs.AddObject(MapObjectKind.LowCover, new GridPos(5, 1));    // у радіусі першої
            cs.AddUnit(shooter, new GridPos(0, 2));
            cs.AddUnit(e1, new GridPos(6, 3));   // поруч із першою
            cs.AddUnit(e2, new GridPos(8, 2));   // поруч лише з другою
            cs.AddUnit(far, new GridPos(11, 4));
            cs.Begin();

            Assert.AreEqual(CombatActionResult.Success, cs.AttackObject(new GridPos(6, 2)));

            int boom = Cfg.Combat.ExplosionDamage;
            Assert.AreEqual(20 - boom * 2, e1.Hp, "e1 у радіусі обох бочок — двічі");
            Assert.AreEqual(20 - boom, e2.Hp, "e2 дістала лише друга бочка — ланцюг спрацював");
            Assert.AreEqual(20, far.Hp);
            Assert.IsFalse(cs.Objects.Any(o => o.Kind == MapObjectKind.PowderKeg), "обидві бочки вибухли");

            var high = cs.ObjectAt(new GridPos(8, 3));
            Assert.IsNotNull(high, "висока перепона не зникає за раз");
            Assert.AreEqual(MapObjectKind.LowCover, high.Kind, "висока → низька (один щабель)");
            Assert.IsNull(cs.ObjectAt(new GridPos(5, 1)), "низька → нічого");
            Assert.IsTrue(cs.Map.IsWalkable(new GridPos(6, 2)), "на місці бочки — прохід");
            Assert.AreEqual(CoverType.None, cs.Map.GetCover(new GridPos(4, 1), Direction.East), "зруйнована перепона укриття більше не дає");
        }

        [Test]
        public void AttackObject_RejectsCoverObjects_AndRespectsApAndSight()
        {
            var cs = Field();
            cs.AddObject(MapObjectKind.LowCover, new GridPos(4, 2));
            cs.AddObject(MapObjectKind.HighCover, new GridPos(6, 2));
            cs.AddObject(MapObjectKind.PowderKeg, new GridPos(8, 2)); // за високою перепоною
            cs.AddUnit(U("p", Side.Player, init: 9, w: Bow()), new GridPos(0, 2));
            cs.AddUnit(U("e", Side.Enemy, init: 1), new GridPos(8, 4));
            cs.Begin();

            Assert.AreEqual(CombatActionResult.InvalidTarget, cs.AttackObject(new GridPos(4, 2)), "по перепоні не б'ють");
            Assert.AreEqual(CombatActionResult.NoLineOfSight, cs.AttackObject(new GridPos(8, 2)), "бочку за брилою не видно");
        }

        [Test]
        public void Haystack_Ignites_FireBurnsThoseInside_AndDiesOut()
        {
            var cs = Field(12, 5);
            var shooter = U("shooter", Side.Player, init: 9, w: Bow());
            var victim = U("victim", Side.Enemy, init: 5);
            cs.AddObject(MapObjectKind.Haystack, new GridPos(6, 2));
            cs.AddUnit(shooter, new GridPos(0, 2));
            cs.AddUnit(victim, new GridPos(7, 2));
            cs.Begin();

            Assert.AreEqual(CombatActionResult.Success, cs.AttackObject(new GridPos(6, 2)));
            Assert.AreEqual(1, cs.Fires.Count);
            Assert.IsTrue(victim.HasStatus(StatusType.Burning), "хто в зоні на момент займання — горить");
            Assert.IsTrue(cs.Journal.Any(e => e.Key == CombatLogKeys.HaystackIgnited));

            // Раунди минають — вогонь згасає.
            for (int guard = 0; guard < 30 && cs.Fires.Count > 0 && cs.Outcome == CombatOutcome.Ongoing; guard++) cs.EndTurn();
            Assert.AreEqual(0, cs.Fires.Count);
            Assert.IsTrue(cs.Journal.Any(e => e.Key == CombatLogKeys.FireOut));
        }

        // ---------------- підкріплення ----------------

        [Test]
        public void Reinforcement_ArrivesOnItsRound_AndJoinsTheTurnOrder()
        {
            var cs = Field();
            cs.AddUnit(U("p", Side.Player, init: 9), new GridPos(0, 0));
            cs.AddUnit(U("e", Side.Enemy, init: 1), new GridPos(8, 4));
            var late = U("late", Side.Enemy, init: 5);
            cs.AddReinforcement(2, late, new GridPos(8, 4)); // місце зайняте — стане поруч
            cs.Begin();

            Assert.AreEqual(2, cs.NextReinforcementRound);
            Assert.AreEqual(1, cs.NextReinforcementCount);
            Assert.IsNull(cs.GetUnit("late"), "до свого раунду на полі його немає");

            while (cs.Round < 2 && cs.Outcome == CombatOutcome.Ongoing) cs.EndTurn();

            Assert.IsNotNull(cs.GetUnit("late"), "прийшов на початку раунду 2");
            Assert.AreEqual(1, GridPos.Chebyshev(late.Pos, new GridPos(8, 4)), "найближча вільна клітинка");
            Assert.IsTrue(cs.NextRoundTurnOrder.Contains(late), "став у чергу ходів");
            Assert.AreEqual(0, cs.NextReinforcementRound);
            Assert.IsTrue(cs.Journal.Any(e => e.Key == CombatLogKeys.ReinforcementsArrived));
        }

        // ---------------- ШІ ----------------

        [Test]
        public void Ai_ShootsTheKeg_WhenItHitsEnemiesAndNoAlly()
        {
            var cs = Field(12, 5);
            var archer = U("archer", Side.Enemy, init: 9, w: Bow());
            var p1 = U("p1", Side.Player, init: 1, hp: 8);
            var p2 = U("p2", Side.Player, init: 1, hp: 8);
            cs.AddObject(MapObjectKind.PowderKeg, new GridPos(3, 2));
            cs.AddObject(MapObjectKind.HighCover, new GridPos(2, 4)); // щоб p1/p2 мали хоч щось, а бочка була вигідніша
            cs.AddUnit(archer, new GridPos(10, 2));
            cs.AddUnit(p1, new GridPos(3, 3));
            cs.AddUnit(p2, new GridPos(2, 2));
            cs.Begin();

            Assert.IsTrue(CombatAi.TryAct(cs, archer));
            Assert.IsTrue(cs.Journal.Any(e => e.Key == CombatLogKeys.KegExploded), "двоє в радіусі — бочка вигідніша за один постріл");
        }

        [Test]
        public void Ai_NeverShootsAKeg_NextToItsOwnSide()
        {
            var cs = Field(12, 5);
            var archer = U("archer", Side.Enemy, init: 9, w: Bow());
            var buddy = U("buddy", Side.Enemy, init: 1);
            var p1 = U("p1", Side.Player, init: 1, hp: 8);
            var p2 = U("p2", Side.Player, init: 1, hp: 8);
            cs.AddObject(MapObjectKind.PowderKeg, new GridPos(3, 2));
            cs.AddUnit(archer, new GridPos(10, 2));
            cs.AddUnit(buddy, new GridPos(4, 2)); // свій у радіусі
            cs.AddUnit(p1, new GridPos(3, 3));
            cs.AddUnit(p2, new GridPos(2, 2));
            cs.Begin();

            CombatAi.TryAct(cs, archer);
            Assert.IsFalse(cs.Journal.Any(e => e.Key == CombatLogKeys.KegExploded), "своїх не підриває");
        }

        // ---------------- шаблони арен ----------------

        [Test]
        public void EveryArenaTemplate_BuildsAValidField_ForEveryRoomThatUsesIt()
        {
            foreach (var siteId in DefaultDungeon.KnownSiteIds)
                foreach (var room in DefaultDungeon.Rooms(siteId))
                {
                    if (room.Kind != DungeonRoomKind.Combat || !ArenaTemplates.TryGet(room.ArenaKey, out var rows)) continue;

                    ArenaTemplates.CountSlots(rows, out int players, out int enemies, out _);
                    Assert.GreaterOrEqual(players, FirstHourWorld.PartyIds.Length, $"{room.Id}: місць загону замало");
                    Assert.GreaterOrEqual(enemies, room.EnemyIds.Count, $"{room.Id}: місць ворогів замало");

                    var setup = ArenaTemplates.Build(rows, FirstHourWorld.PartyIds, room.EnemyIds, HitRuleKind.Threshold, BattleOpening.Encounter);
                    var map = new GridMap(setup.Width, setup.Height);
                    foreach (var wall in setup.Walls) map.SetWall(wall.Pos);
                    foreach (var o in setup.Objects) { map.SetWalkable(o.Pos, false); }

                    foreach (var p in setup.PlayerUnits)
                    {
                        Assert.IsTrue(map.IsWalkable(p.Pos), $"{room.Id}: місце загону на перешкоді");
                        bool reachesSomeEnemy = setup.EnemyUnits.Any(e => Pathfinder.Path(map, p.Pos, NeighbourOf(map, e.Pos)).Count > 0);
                        Assert.IsTrue(reachesSomeEnemy, $"{room.Id}: з {p.Pos} не дійти до жодного ворога");
                    }
                }
        }

        private static GridPos NeighbourOf(GridMap map, GridPos p)
        {
            foreach (var d in new[] { new GridPos(p.X - 1, p.Y), new GridPos(p.X + 1, p.Y), new GridPos(p.X, p.Y - 1), new GridPos(p.X, p.Y + 1) })
                if (map.IsWalkable(d)) return d;
            return p;
        }

        [Test]
        public void DungeonBattle_UsesTheRoomTemplate_AndTheViewShowsObjectsAndCoverSides()
        {
            var s = new GameSession();
            s.NewGame(new NewGameOptions { SkipCreation = true, HitRule = HitRuleKind.Threshold });
            var step = s.AdvanceScene();
            while (!step.IsFinished) step = step.IsChoice ? s.ChooseSceneOption(0) : s.AdvanceScene();
            s.DepartExpedition(DefaultDungeon.AbandonedCamp, Game.Core.Expeditions.ExpeditionApproach.Delve, FirstHourWorld.PartyIds, 2);
            s.ResolveDungeonRoom(IncidentPath.Bloody);

            var view = s.GetBattleView();
            Assert.IsNotNull(view.Objects);
            Assert.IsTrue(view.Objects.Any(o => o.Kind == "PowderKeg" && o.IsTargetable && o.EffectDamage > 0), "бочка видна з наслідком до кліку");
            Assert.IsTrue(view.Objects.Any(o => o.Kind == "Haystack"));
            Assert.IsNotNull(view.Grid.TileCoverSides);
            Assert.AreEqual(view.Grid.Width * view.Grid.Height, view.Grid.TileCoverSides.Count);
            Assert.IsTrue(view.Grid.TileCoverSides.Any(sides => sides.Contains("Half")), "укриття по боках видно");
        }
    }
}
