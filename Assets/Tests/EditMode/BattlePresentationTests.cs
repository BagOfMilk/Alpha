using System.Linq;
using Game.Core.Balance;
using Game.Core.Combat;
using Game.Core.Session;
using Game.Core.Stats;
using Game.Gameplay;
using Game.Gameplay.UI;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Подача бою П1–П4 (docs/research/RT_COMBAT_PRESENTATION.md; власник 08.10.2026: «роби сама П1–П4»):
    /// ритм тактів і множник швидкості, удар у кадр влучання, шанс із клітинки голограми руху, щити укриття.
    /// Механіка бою не змінюється: прев'ю з точки — те саме число, що й звичайне прев'ю, коли юніт там стоїть.
    /// </summary>
    public class BattlePresentationTests
    {
        private static readonly BalanceConfig Cfg = new BalanceConfig();

        private static WeaponDefinition Bow() => new WeaponDefinition("bow", "Bow", SkillType.Ranged)
        { DamageMin = 3, DamageMax = 3, ApCost = 2, OptimalRange = 6 };

        private static CombatUnit Unit(string id, Side side, int accuracy = 60) =>
            new CombatUnit(id, side, new UnitProfile
            {
                DisplayName = id, MaxHp = 20, MaxAp = 8, Accuracy = accuracy, Initiative = side == Side.Player ? 9 : 1,
                MoveApPerTile = 1
            }, Bow());

        // ---------------- П3: шанс із гіпотетичної клітинки (ядро) ----------------

        [Test]
        public void HitChanceFrom_OwnTile_EqualsTheRegularPreview()
        {
            var cs = new CombatState(new GridMap(10, 6), Cfg, new ThresholdRule(Cfg), null);
            var hero = Unit("hero", Side.Player);
            var foe = Unit("foe", Side.Enemy);
            cs.AddUnit(hero, new GridPos(1, 1));
            cs.AddUnit(foe, new GridPos(6, 1));
            cs.Begin();

            Assert.AreEqual(cs.HitChancePreview(hero, foe), cs.HitChancePreviewFrom(hero, hero.Pos, foe),
                "з власної клітинки — рівно те число, що покаже прев'ю атаки (інваріант 8)");
        }

        [Test]
        public void HitChanceFrom_Flank_IgnoresCover_AndDoesNotMoveTheUnit()
        {
            var map = new GridMap(10, 6);
            var cs = new CombatState(map, Cfg, new ThresholdRule(Cfg), null);
            var hero = Unit("hero", Side.Player);
            var foe = Unit("foe", Side.Enemy);
            cs.AddUnit(hero, new GridPos(1, 2));
            cs.AddUnit(foe, new GridPos(5, 2));
            map.SetCover(new GridPos(5, 2), Direction.West, CoverType.Full); // щит до героя
            cs.Begin();

            int frontal = cs.HitChancePreviewFrom(hero, new GridPos(1, 2), foe);
            int flank = cs.HitChancePreviewFrom(hero, new GridPos(5, 5), foe);   // з півночі — сторона без укриття
            Assert.Greater(flank, frontal, "з флангу укриття не діє — шанс вищий");
            Assert.AreEqual(new GridPos(1, 2), hero.Pos, "прев'ю нічого не рухає");
        }

        [Test]
        public void Session_ShotsFromOwnTile_MatchHitChancePreview_AndTileCoverIsReadOnly()
        {
            var s = new GameSession();
            s.NewTrainingBattle(new TrainingBattleOptions { HitRule = HitRuleKind.Threshold });
            for (int guard = 0; guard < 20 && s.GetBattleView() != null && s.GetBattleView().IsAiTurn; guard++)
                s.CombatAiStepOneAction();

            var view = s.GetBattleView();
            Assert.IsNotNull(view);
            var me = view.Units.First(u => u.Id == view.CurrentUnitId);
            var here = new GridPos(me.Pos.X, me.Pos.Y);

            var shots = s.PreviewShotsFrom(here);
            if (!me.WeaponIsMelee) Assert.IsNotEmpty(shots, "у тренуванні є вороги, дальня зброя бачить усіх");
            foreach (var shot in shots)
                Assert.AreEqual(s.PreviewHitChance(me.Id, shot.TargetId), shot.Chance, shot.TargetId);

            var cover = s.PreviewTileCover(here);
            Assert.IsNotNull(cover);
            CollectionAssert.Contains(new[] { "None", "Half", "Full" }, cover.Best);
            Assert.IsNull(s.PreviewTileCover(new GridPos(-1, -1)), "поза картою — null");

            var after = s.GetBattleView().Units.First(u => u.Id == me.Id);
            Assert.AreEqual(me.Pos.X, after.Pos.X);
            Assert.AreEqual(me.Pos.Y, after.Pos.Y);
        }

        // ---------------- П1–П2: ритм тактів ----------------

        [Test]
        public void AttackBeat_ImpactComesBeforeTheEnd_AndLeavesTimeForTheReaction()
        {
            foreach (bool melee in new[] { true, false })
                foreach (float clip in new[] { 0f, 0.6f, 1.2f, 3f })
                    foreach (float speed in BattleTactTiming.SpeedOptions)
                    {
                        var b = BattleTactTiming.Attack(melee, false, clip, speed);
                        Assert.Greater(b.ImpactAt, 0f);
                        Assert.Less(b.ImpactAt, b.Duration, $"melee={melee} clip={clip} speed={speed}");
                        Assert.LessOrEqual(b.Duration, BattleTactTiming.MaxAttackSeconds / speed + 1e-4f, "довгий кліп не тягне бій");
                    }
        }

        [Test]
        public void AttackBeat_ImpactFollowsTheClip_MeleeLaterThanShot()
        {
            var melee = BattleTactTiming.Attack(true, false, 1.0f, 1f);
            var shot = BattleTactTiming.Attack(false, false, 1.0f, 1f);
            Assert.AreEqual(1.0f * BattleTactTiming.MeleeImpactFraction, melee.ImpactAt, 1e-4f);
            Assert.Less(shot.ImpactAt, melee.ImpactAt, "постріл — на спуску, раніше за удар замахом");
        }

        [Test]
        public void SpeedMultiplier_ShortensEveryBeatProportionally()
        {
            Assert.AreEqual(BattleTactTiming.Tile(false, 1f) / 2f, BattleTactTiming.Tile(false, 2f), 1e-5f);
            Assert.AreEqual(BattleTactTiming.Down(false, 1f).Duration / 2f, BattleTactTiming.Down(false, 2f).Duration, 1e-5f);
            Assert.AreEqual(BattleTactTiming.Ability(false, 1f).Duration / 1.5f, BattleTactTiming.Ability(false, 1.5f).Duration, 1e-5f);
            var slow = BattleTactTiming.Attack(true, false, 1.0f, 1f);
            var fast = BattleTactTiming.Attack(true, false, 1.0f, 2f);
            Assert.AreEqual(slow.ImpactAt / 2f, fast.ImpactAt, 1e-4f);
            Assert.AreEqual(1f, BattleTactTiming.ClampSpeed(float.NaN));
            Assert.AreEqual(0.25f, BattleTactTiming.ClampSpeed(0f), "нуль із налаштувань не зупиняє бій");
        }

        [Test]
        public void ImpactSound_EveryImpactEventHasACue_AndTheArenaPlaysTheSameKinds()
        {
            foreach (var key in new[] { "combat.attack.hit", "combat.attack.crit", "combat.attack.graze", "combat.attack.miss" })
            {
                Assert.IsTrue(BattleTactTiming.IsImpactEvent(key), key);
                Assert.AreNotEqual(SoundCue.None, SoundCueTable.ForEvent(key), "режисер звуку знає цю подію — арена її перебирає");
            }
            Assert.AreEqual(SoundCue.HitFlesh, BattleTactTiming.ImpactCue(BattleLogKind.Hit));
            Assert.AreEqual(SoundCue.HitArmor, BattleTactTiming.ImpactCue(BattleLogKind.Crit));
            Assert.AreEqual(SoundCue.Block, BattleTactTiming.ImpactCue(BattleLogKind.Graze));
            Assert.AreEqual(SoundCue.Swing, BattleTactTiming.ImpactCue(BattleLogKind.Miss));
            Assert.AreEqual(SoundCue.None, BattleTactTiming.ImpactCue(BattleLogKind.Move));
            Assert.IsFalse(BattleTactTiming.IsImpactEvent("combat.battle.started"));
        }

        [Test]
        public void SpeedLabels_AreUkrainianDecimals()
        {
            CollectionAssert.AreEqual(new[] { "×1", "×1,5", "×2" }, BattleTactTiming.SpeedOptions.Select(BattleTactTiming.SpeedLabel).ToArray());
        }

        // ---------------- П4: значок щита ----------------

        [Test]
        public void ShieldIcon_HalfFillsOnlyTheLeft_FullFillsMore_EdgesAreClear()
        {
            const int n = 32;
            int fullFill = 0, halfFill = 0;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    if (ShieldIcon.Cell(x, y, n, true) == ShieldIcon.Fill) fullFill++;
                    int half = ShieldIcon.Cell(x, y, n, false);
                    if (half == ShieldIcon.Fill)
                    {
                        halfFill++;
                        Assert.Less(x, n / 2, "пів-щит заливає лише ліву половину");
                    }
                }
            Assert.Greater(halfFill, 0);
            Assert.Greater(fullFill, halfFill);
            Assert.AreEqual(ShieldIcon.Transparent, ShieldIcon.Cell(0, n - 1, n, true), "нижній кут — поза щитом");
            Assert.AreEqual(ShieldIcon.Transparent, ShieldIcon.Cell(-1, 0, n, true));
        }
    }
}
