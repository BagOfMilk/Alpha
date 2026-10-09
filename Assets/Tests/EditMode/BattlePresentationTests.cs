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

        // ---------------- П8: журнал називає джерело шкоди ----------------

        private static string DamageLine(params string[] pairs)
        {
            var args = new System.Collections.Generic.Dictionary<string, string> { { "unitId", "victim" }, { "damage", "4" }, { "damageType", "fire" } };
            for (int i = 0; i + 1 < pairs.Length; i += 2) args[pairs[i]] = pairs[i + 1];
            var entry = new Game.Core.Session.Views.BattleLogLineView { Round = 1, Key = CombatLogKeys.Damage, Args = args };
            return BattleLogText.Line(entry, true, id => id == "caster" ? "Гафія" : "Мирослава", id => false);
        }

        [Test]
        public void DamageLine_NamesTheSource_UnitKegOrVolley()
        {
            StringAssert.Contains("від: Гафія", DamageLine("sourceId", "caster"));
            StringAssert.Contains("вибух бочки", DamageLine("source", CombatLogKeys.SourceKeg));
            StringAssert.Contains("обстріл", DamageLine("source", CombatLogKeys.SourceVolley));
            string plain = DamageLine();
            StringAssert.DoesNotContain("{", plain, "без джерела — жодного сліду плейсхолдера");
            StringAssert.DoesNotContain("від:", DamageLine("sourceId", "victim"), "сам собі — не джерело");
        }

        [Test]
        public void Core_AbilityDamage_RecordsTheCaster_AsSource()
        {
            var cs = new CombatState(new GridMap(8, 3), Cfg, new ThresholdRule(Cfg), null);
            var hero = Unit("hero", Side.Player, accuracy: 200);
            var foe = Unit("foe", Side.Enemy);
            hero.Abilities.Add(new AbilityDefinition
            {
                Id = "ability.test_burn", DisplayName = "Burn", ApCost = 1, CooldownTurns = 0, Range = 6,
                Targeting = AbilityTarget.Enemy, RequiresLineOfSight = false,
                Effects = { new AbilityEffect(AbilityEffectKind.FlatDamage, 3) { Damage = DamageType.Fire } }
            });
            cs.AddUnit(hero, new GridPos(0, 0));
            cs.AddUnit(foe, new GridPos(3, 0));
            cs.Begin();
            cs.UseAbility("ability.test_burn", foe.Id);

            var damage = cs.Journal.Last(e => e.Key == CombatLogKeys.Damage);
            Assert.AreEqual(hero.Id, damage.Args["sourceId"]);
        }

        // ---------------- П9: прев'ю витрати ОД ----------------

        [Test]
        public void ApForecast_ListsOnlyAbilitiesLostByThisAction()
        {
            var abilities = new[]
            {
                new Game.Core.Session.Views.BattleAbilityView { Id = "cheap", ApCost = 2 },
                new Game.Core.Session.Views.BattleAbilityView { Id = "dear", ApCost = 5 },
                new Game.Core.Session.Views.BattleAbilityView { Id = "cooling", ApCost = 1, CooldownRemaining = 2 },
                new Game.Core.Session.Views.BattleAbilityView { Id = "already_too_dear", ApCost = 9 },
            };
            Assert.AreEqual(3, ApForecast.Left(6, 3));
            Assert.AreEqual(0, ApForecast.Left(2, 5), "не нижче нуля");
            CollectionAssert.AreEqual(new[] { "dear" }, ApForecast.NewlyUnaffordable(abilities, 6, 3),
                "дешеву ще можна, на відкаті й так недоступна, надто дорога — недоступна й зараз");
            CollectionAssert.IsEmpty(ApForecast.NewlyUnaffordable(abilities, 6, 6));
            CollectionAssert.IsEmpty(ApForecast.NewlyUnaffordable(abilities, 6, 3, exceptAbilityId: "dear"), "сама дія — не втрата");
        }

        // ---------------- П6: уповільнення на вбивстві ----------------

        [Test]
        public void KillSlowMo_Killer_KeepsNormalPace_WhileWorldSlows()
        {
            var slow = new KillSlowMo();
            Assert.AreEqual(1f, slow.ActorScale(0f), "поза вікном — звичайний темп");
            slow.Trigger(1f);
            Assert.AreEqual(1f, slow.TimeScale(1.1f) * slow.ActorScale(1.1f), 1e-4f, "у вікні той, хто вбив, — у звичайному темпі");
            Assert.AreEqual(1f, slow.ActorScale(1f + KillSlowMo.DurationSeconds + 0.01f));
        }

        [Test]
        public void CameraEvents_QueueByPriority_EachGetsItsTurn_ThenBackToActive()
        {
            var cam = new BattleCameraEvents();
            cam.Focus("a", BattleCameraEvents.PriorityDeath);
            cam.Focus("b", BattleCameraEvents.PriorityReinforcement);
            cam.Focus("c", BattleCameraEvents.PriorityDeath);
            Assert.AreEqual(2, cam.QueuedCount);
            Assert.AreEqual("a", cam.Tick(0.1f, "hero", false));
            Assert.AreEqual("c", cam.Tick(BattleCameraEvents.HoldSeconds, "hero", false), "смерть раніше за підкріплення");
            Assert.AreEqual("b", cam.Tick(BattleCameraEvents.HoldSeconds + 0.01f, "hero", false));
            Assert.AreEqual("hero", cam.Tick(BattleCameraEvents.HoldSeconds + 0.01f, "hero", false), "черга скінчилась — назад");
            Assert.IsFalse(cam.IsActive);
        }

        [Test]
        public void CameraEvents_HigherPriority_Preempts_QueueIsCapped()
        {
            var cam = new BattleCameraEvents();
            cam.Focus("minor", BattleCameraEvents.PriorityMinor);
            cam.Focus("death", BattleCameraEvents.PriorityDeath);
            Assert.AreEqual("death", cam.Tick(0.1f, "hero", true), "важливіша подія перебиває поточну");
            for (int i = 0; i < 6; i++) cam.Focus("x" + i, BattleCameraEvents.PriorityMinor);
            Assert.AreEqual(BattleCameraEvents.MaxQueued, cam.QueuedCount);
        }

        [Test]
        public void CameraFraming_SafeArea_IsTheCentreOfTheFreeArea()
        {
            Assert.IsTrue(BattleCameraFraming.IsInsideSafeArea(500f, 400f, 0f, 0f, 1000f, 800f));
            Assert.IsFalse(BattleCameraFraming.IsInsideSafeArea(950f, 400f, 0f, 0f, 1000f, 800f), "край кадру — летимо");
            Assert.IsFalse(BattleCameraFraming.IsInsideSafeArea(-10f, 400f, 0f, 0f, 1000f, 800f), "поза екраном — летимо");
        }

        [Test]
        public void ActionCamera_EveryThirdStrike_WithCooldown_OffOnLow()
        {
            var ac = new BattleActionCamera();
            int shots = 0;
            for (int i = 0; i < 9; i++)
                if (ac.ShouldTrigger(i * 10f, 1f, enabled: true, lowGraphics: false)) shots++;
            Assert.AreEqual(3, shots, "шанс 0,34 без кубика — рівно кожен третій удар");

            var cd = new BattleActionCamera();
            cd.ShouldTrigger(0f, 1f, true, false); cd.ShouldTrigger(0.1f, 1f, true, false);
            Assert.IsTrue(cd.ShouldTrigger(0.2f, 1f, true, false));
            for (int i = 0; i < 5; i++) Assert.IsFalse(cd.ShouldTrigger(1f + i * 0.5f, 1f, true, false), "пауза між кадрами");

            var off = new BattleActionCamera();
            var low = new BattleActionCamera();
            for (int i = 0; i < 6; i++)
            {
                Assert.IsFalse(off.ShouldTrigger(i * 10f, 1f, enabled: false, lowGraphics: false));
                Assert.IsFalse(low.ShouldTrigger(i * 10f, 1f, enabled: true, lowGraphics: true), "PERF-01: на Низькій немає");
            }
            Assert.IsFalse(new BattleActionCamera().ShouldTrigger(0f, 0.1f, true, false) , "надто короткий удар");
        }

        [Test]
        public void KillSlowMo_Rare_SkipsKillsSoonAfterTheWindow()
        {
            var slow = new KillSlowMo { MinGapSeconds = KillSlowMo.RareGapSeconds };
            slow.Trigger(10f);
            float end = 10f + KillSlowMo.DurationSeconds;
            slow.Trigger(end + 1f);
            Assert.AreEqual(1f, slow.TimeScale(end + 1.1f), "рідко: друге вбивство одразу після вікна — без уповільнення");
            slow.Trigger(end + KillSlowMo.RareGapSeconds + 0.1f);
            Assert.AreEqual(KillSlowMo.Scale, slow.TimeScale(end + KillSlowMo.RareGapSeconds + 0.2f), "після паузи — знову");
        }

        [Test]
        public void KillSlowMo_NeverStacks_EndsOnTime_AndCanBeOff()
        {
            var slow = new KillSlowMo();
            Assert.AreEqual(1f, slow.TimeScale(0f));
            slow.Trigger(10f);
            slow.Trigger(10.1f);
            slow.Trigger(10.2f);
            Assert.AreEqual(KillSlowMo.Scale, slow.TimeScale(10.3f), "три вбивства — та сама глибина, без стакання");
            Assert.AreEqual(1f, slow.TimeScale(10.2f + KillSlowMo.DurationSeconds + 0.01f), "вікно скінчилось від останнього вбивства");

            var off = new KillSlowMo { Enabled = false };
            off.Trigger(0f);
            Assert.AreEqual(1f, off.TimeScale(0.1f), "вимкнено в налаштуваннях — жодного уповільнення");
            Assert.IsFalse(KillSlowMo.DefaultEnabled(lowGraphics: true), "PERF-01: на Низькій типово вимкнено");
        }

        // ---------------- П7: камера на подію без залипання ----------------

        [Test]
        public void CameraEvents_AlwaysReturnToTheActiveUnit()
        {
            var cam = new BattleCameraEvents();
            Assert.AreEqual("hero", cam.Tick(0.1f, "hero", false));
            cam.Focus("foe");
            Assert.AreEqual("foe", cam.Tick(0.1f, "hero", true));
            Assert.AreEqual("foe", cam.Tick(BattleCameraEvents.HoldSeconds, "hero", true), "такти ще йдуть — тримаємо подію");
            Assert.AreEqual("hero", cam.Tick(BattleCameraEvents.MaxSeconds, "hero", true), "що б не сталося — повернення до того, хто ходить");
            Assert.IsFalse(cam.IsActive);

            cam.Focus("foe");
            Assert.AreEqual("hero", cam.Tick(BattleCameraEvents.HoldSeconds + 0.01f, "hero", false), "такти скінчились — одразу назад");
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
