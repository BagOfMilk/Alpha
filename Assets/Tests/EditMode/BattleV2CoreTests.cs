using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Game.Core.Balance;
using Game.Core.Combat;
using Game.Core.Loop;
using Game.Core.Session;
using Game.Core.Session.Views;
using Game.Core.Stats;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Бій v2, частина «ядро» (docs/COMBAT_V2.md §7.1): нові поля GetBattleView,
    /// розклад шансу (HitChanceCalculator.Decompose), прев'ю-команди GameSession
    /// (PreviewAttack/PreviewMovePath/PreviewOverwatchCone), CombatStabilize,
    /// журнал з path/ap/cover, і регресія на скаргу власника «нічо не зрозуміло
    /// було... коли наступив ход опонентів гра тупа зупинилась».
    ///
    /// Два тести (PreviewAttack_Ability*) читають/пишуть <c>GameSession._battle</c>
    /// через рефлексію: за замовчуванням (NewTrainingBattle/DefaultCombatContent)
    /// жоден юніт не знає жодної здібності (гейт скіла — Companion/Roster, яких
    /// тут немає), тож єдиний спосіб дійти до контракту "PreviewAttack з
    /// abilityId" без повного розгортання кампанії — підмінити приватне поле
    /// вручну зібраним CombatState (ті самі AbilityDefinition з
    /// DefaultCombatContent). PreviewAttack/PreviewMovePath/PreviewOverwatchCone
    /// свідомо не перевіряють GameSession.State — тому цей прийом чесний: він не
    /// обходить жодну валідацію, яку ці методи насправді роблять.
    /// </summary>
    public class BattleV2CoreTests
    {
        private static readonly BalanceConfig Cfg = new BalanceConfig();

        // ---------------------------------------------------------------
        // Допоміжне
        // ---------------------------------------------------------------

        private static CombatState NewCombat(GridMap map) => new CombatState(map, Cfg, new ThresholdRule(Cfg), null);

        private static WeaponDefinition RangedWeapon(int dmg = 4, int apCost = 3, int range = 6)
            => new WeaponDefinition("w_ranged", "W", SkillType.Ranged)
            { DamageMin = dmg, DamageMax = dmg, CritDamageBonus = 2, ApCost = apCost, OptimalRange = range };

        private static CombatUnit U(string id, Side side, string name, int init, int hp = 20, int ap = 10,
                                    int acc = 70, int defense = 0, WeaponDefinition w = null)
        {
            var p = new UnitProfile
            {
                DisplayName = name, MaxHp = hp, MaxAp = ap, Accuracy = acc, Defense = defense,
                Initiative = init, CritChance = 0, Armor = 0, Resolve = 0, MoveApPerTile = 1
            };
            return new CombatUnit(id, side, p, w);
        }

        private static BattleUnitView FindUnit(BattleView view, string id)
        {
            foreach (var u in view.Units)
                if (u.Id == id) return u;
            return null;
        }

        private static NewGameOptions SkipCreationOptions()
            => new NewGameOptions { SkipCreation = true, HitRule = HitRuleKind.Threshold };

        private static SceneStepView RunSceneToFinish(GameSession s)
        {
            var step = s.AdvanceScene();
            while (!step.IsFinished)
                step = step.IsChoice ? s.ChooseSceneOption(0) : s.AdvanceScene();
            return step;
        }

        private static void FastForwardOpeningToMorning(GameSession s)
        {
            Assert.AreEqual(SessionState.Scene, s.State);
            RunSceneToFinish(s);
            Assert.AreEqual(SessionState.Morning, s.State);
        }

        /// <summary>Єдине місце рефлексії у файлі — див. class summary.</summary>
        private static void InjectBattle(GameSession session, CombatState battle)
        {
            var field = typeof(GameSession).GetField("_battle", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "GameSession._battle мало існувати (контракт §7.1 не змінює це поле)");
            field.SetValue(session, battle);
        }

        private static CombatState BuildLungeScenario(out CombatUnit brawler, out CombatUnit enemy)
        {
            var map = new GridMap(10, 1);
            var cs = NewCombat(map);
            var weapon = new WeaponDefinition("w_melee", "W", SkillType.Melee)
            { DamageMin = 4, DamageMax = 4, CritDamageBonus = 2, ApCost = 3, OptimalRange = 1 };

            var brawlerUnit = U("brawler", Side.Player, "Brawler", init: 10, w: weapon);
            brawlerUnit.Abilities.Add(DefaultCombatContent.Lunge());
            var enemyUnit = U("target", Side.Enemy, "Target", init: 1, w: weapon);

            cs.AddUnit(brawlerUnit, new GridPos(0, 0));
            cs.AddUnit(enemyUnit, new GridPos(5, 0)); // поза дальністю мілі — Ривок мав зблизити
            cs.Begin();

            brawler = brawlerUnit;
            enemy = enemyUnit;
            return cs;
        }

        private static CombatState BuildVolleyScenario(out CombatUnit shooter, out CombatUnit target)
        {
            var map = new GridMap(10, 1);
            var cs = NewCombat(map);
            var weapon = DefaultCombatContent.HordeBow();

            var shooterUnit = U("shooter", Side.Player, "Shooter", init: 10, w: weapon);
            shooterUnit.Abilities.Add(DefaultCombatContent.Volley());
            var targetUnit = U("target", Side.Enemy, "Target", init: 1, w: weapon);

            cs.AddUnit(shooterUnit, new GridPos(0, 0));
            cs.AddUnit(targetUnit, new GridPos(4, 0));
            cs.Begin();

            shooter = shooterUnit;
            target = targetUnit;
            return cs;
        }

        // ---------------------------------------------------------------
        // 2. HitChanceCalculator.Decompose — розклад
        // ---------------------------------------------------------------

        [Test]
        public void Decompose_ReturnsExactlyTheClosedKeySet_InOrder()
        {
            var terms = HitChanceCalculator.Decompose(50, false, 0, CoverType.None, false, 0, 6, Cfg);
            var keys = terms.Select(t => t.Key).ToList();
            CollectionAssert.AreEqual(new[]
            {
                "accuracy", "ability", "defense", "knocked_down", "marked",
                "cover_half", "cover_full", "distance", "suppressed", "clamp"
            }, keys);
        }

        /// <summary>Охоронець §7.2: сума ChanceDelta (разом із clamp) ДОРІВНЮЄ Compute на переборі комбінацій.</summary>
        [Test]
        public void Decompose_SumsToCompute_OnAllCombinations()
        {
            int[] accuracies = { 0, 40, 65, 120 };
            int[] defenses = { 0, 3, 10 };
            CoverType[] covers = { CoverType.None, CoverType.Half, CoverType.Full };
            bool[] bools = { false, true };
            int[] distances = { 0, 1, 6, 10 };
            int[] optimalRanges = { 1, 6 };
            int[] accuracyBonuses = { 0, -10, 15 };

            int checkedCombos = 0;
            foreach (var acc in accuracies)
            foreach (var suppressed in bools)
            foreach (var def in defenses)
            foreach (var cover in covers)
            foreach (var ignoreCover in bools)
            foreach (var dist in distances)
            foreach (var optimal in optimalRanges)
            foreach (var marked in bools)
            foreach (var knocked in bools)
            foreach (var bonus in accuracyBonuses)
            {
                var terms = HitChanceCalculator.Decompose(acc, suppressed, def, cover, ignoreCover, dist, optimal, Cfg, marked, knocked, bonus);
                int sum = terms.Sum(t => t.ChanceDelta);
                int expected = HitChanceCalculator.Compute(acc, suppressed, def, cover, ignoreCover, dist, optimal, Cfg, marked, knocked, bonus);
                Assert.AreEqual(expected, sum,
                    $"acc={acc} suppr={suppressed} def={def} cover={cover} ignore={ignoreCover} dist={dist} opt={optimal} marked={marked} knocked={knocked} bonus={bonus}");
                checkedCombos++;
            }
            Assert.Greater(checkedCombos, 10000, "перебір мав бути справді широким, не жменькою прикладів");
        }

        /// <summary>
        /// Прикріплене вручну прораховане число: Compute тут дорівнює сумі
        /// Decompose ЗАВЖДИ (Compute реалізований через Decompose, §7.2), тож
        /// сам собою тест на суму («SumsToCompute» вище) не ловить перевернутий
        /// знак усередині ОДНОГО доданка — обидва числа зрушаться однаково.
        /// Цей тест — незалежний якір: очікуване число прораховане вручну поза
        /// кодом, тому справді ловить помилку в конкретному доданку.
        /// 65(accuracy)+5(ability)-10(defense)+10(marked)-20(cover_half)
        /// -10(distance: (8-6)×5) = 40; без clamp (у межах 1..99).
        /// </summary>
        [Test]
        public void Decompose_KnownScenario_MatchesHandComputedChance()
        {
            var terms = HitChanceCalculator.Decompose(
                attackerAccuracy: 65, attackerSuppressed: false, targetDefense: 10,
                cover: CoverType.Half, ignoreCover: false, distance: 8, optimalRange: 6,
                cfg: Cfg, targetMarked: true, targetKnockedDown: false, accuracyBonus: 5);

            var byKey = terms.ToDictionary(t => t.Key, t => t.ChanceDelta);
            Assert.AreEqual(65, byKey["accuracy"]);
            Assert.AreEqual(5, byKey["ability"]);
            Assert.AreEqual(-10, byKey["defense"]);
            Assert.AreEqual(0, byKey["knocked_down"]);
            Assert.AreEqual(10, byKey["marked"]);
            Assert.AreEqual(-20, byKey["cover_half"]);
            Assert.AreEqual(0, byKey["cover_full"]);
            Assert.AreEqual(-10, byKey["distance"]);
            Assert.AreEqual(0, byKey["suppressed"]);
            Assert.AreEqual(0, byKey["clamp"]);

            int sum = terms.Sum(t => t.ChanceDelta);
            Assert.AreEqual(40, sum);
            Assert.AreEqual(40, HitChanceCalculator.Compute(65, false, 10, CoverType.Half, false, 8, 6, Cfg, true, false, 5));
        }

        [Test]
        public void Decompose_KnockedDown_NeverExceedsFullDefenseReduction()
        {
            // Знижений захист не може стати менше нуля (Compute: Max(0, def-penalty)) —
            // knocked_down делта не повинна "перекомпенсувати" defense.
            var terms = HitChanceCalculator.Decompose(50, false, 5, CoverType.None, false, 0, 6, Cfg, targetKnockedDown: true);
            var byKey = terms.ToDictionary(t => t.Key, t => t.ChanceDelta);
            Assert.AreEqual(-5, byKey["defense"]);
            Assert.AreEqual(Math.Min(5, Cfg.Combat.KnockdownDefensePenalty), byKey["knocked_down"]);
        }

        // ---------------------------------------------------------------
        // 1. GetBattleView — нові поля
        // ---------------------------------------------------------------

        [Test]
        public void GetBattleView_ReachableTileCosts_MatchesCoreReachableFor_SameOrderAndValues()
        {
            var session = new GameSession();
            session.NewTrainingBattle(new TrainingBattleOptions());
            var view = session.GetBattleView();

            var raw = DefaultCombatContent.Training(); // той самий детермінований набір (Threshold, без roller)
            var expected = raw.ReachableFor(raw.Current);

            Assert.AreEqual(expected.Count, view.ReachableTiles.Count);
            Assert.AreEqual(view.ReachableTiles.Count, view.ReachableTileCosts.Count);
            for (int i = 0; i < view.ReachableTiles.Count; i++)
            {
                var pos = new GridPos(view.ReachableTiles[i].X, view.ReachableTiles[i].Y);
                Assert.IsTrue(expected.TryGetValue(pos, out var cost), $"тайл {pos} відсутній у CombatState.ReachableFor");
                Assert.AreEqual(cost, view.ReachableTileCosts[i], $"ціна тайла {pos} розійшлась із ядром");
            }
        }

        [Test]
        public void GetBattleView_IsAiTurn_TrueOnlyWhenCurrentUnitIsNotPlayer()
        {
            var session = new GameSession();
            session.NewTrainingBattle(new TrainingBattleOptions());

            int guard = 0;
            while (guard++ < 200)
            {
                var view = session.GetBattleView();
                if (view == null || view.Outcome != "Ongoing") break;

                var current = FindUnit(view, view.CurrentUnitId);
                Assert.IsNotNull(current, "CurrentUnitId мав вказувати на реального юніта поки Outcome==Ongoing");
                Assert.AreEqual(current.Side != "Player", view.IsAiTurn);

                if (view.IsAiTurn) session.CombatAiStepOneAction();
                else session.CombatEndTurn();
            }
        }

        [Test]
        public void GetBattleView_WeaponFields_And_IsAiControlled_MatchUnderlyingWeapon()
        {
            var session = new GameSession();
            session.NewTrainingBattle(new TrainingBattleOptions());
            var view = session.GetBattleView();
            var raw = DefaultCombatContent.Training();

            foreach (var uv in view.Units)
            {
                var unit = raw.GetUnit(uv.Id);
                Assert.IsNotNull(unit, uv.Id);
                var w = unit.Weapon;

                Assert.AreEqual(w?.ApCost ?? 0, uv.AttackApCost, uv.Id);
                Assert.AreEqual(w?.OptimalRange ?? 0, uv.WeaponRange, uv.Id);
                Assert.AreEqual(w?.OptimalRange ?? 0, uv.WeaponOptimalRange, uv.Id);
                Assert.AreEqual(w != null && w.IsMelee, uv.WeaponIsMelee, uv.Id);
                Assert.AreEqual(unit.Side != Side.Player, uv.IsAiControlled, uv.Id);
                Assert.AreEqual(0, uv.DownWindowRemaining, uv.Id + ": ніхто ще не звалений на старті бою");
            }
        }

        [Test]
        public void GetBattleView_DownWindowRemaining_OnlyPopulatedForDownedUnit()
        {
            var session = new GameSession();
            session.NewTrainingBattle(new TrainingBattleOptions());

            BattleUnitView downed = null;
            int guard = 0;
            while (guard++ < 300 && downed == null)
            {
                var view = session.GetBattleView();
                if (view == null || view.Outcome != "Ongoing") break;
                foreach (var u in view.Units)
                    if (u.IsDowned) { downed = u; break; }
                if (downed != null) break;

                if (view.IsAiTurn) session.CombatAiStepOneAction();
                else session.CombatEndTurn();
            }

            Assert.IsNotNull(downed, "за 300 кроків тренувального бою хтось мав звалитись — вороги б'ють беззахисних трейні");
            Assert.GreaterOrEqual(downed.DownWindowRemaining, 1);
            Assert.LessOrEqual(downed.DownWindowRemaining, Cfg.Combat.DownWindowTurns);

            foreach (var u in session.GetBattleView().Units)
                if (!u.IsDowned) Assert.AreEqual(0, u.DownWindowRemaining, $"{u.Id}: DownWindowRemaining лише для зваленого");
        }

        [Test]
        public void GetBattleView_OverwatchAim_ReflectsRealStance_AfterCombatEnterOverwatch()
        {
            var session = new GameSession();
            session.NewTrainingBattle(new TrainingBattleOptions());
            var before = session.GetBattleView();
            var currentId = before.CurrentUnitId;
            Assert.IsFalse(FindUnit(before, currentId).HasOverwatchAim);

            var aim = new GridPos(6, 1);
            Assert.AreEqual(CombatActionResult.Success, session.CombatEnterOverwatch(aim));

            var after = session.GetBattleView();
            var unitView = FindUnit(after, currentId);
            Assert.IsTrue(unitView.HasOverwatchAim);
            Assert.AreEqual(aim.X, unitView.OverwatchAim.X);
            Assert.AreEqual(aim.Y, unitView.OverwatchAim.Y);
        }

        /// <summary>Node1BloodyEnemyIds несе двох "horde_scout" — справжня причина, чому Ordinal взагалі потрібен.</summary>
        [Test]
        public void GetBattleView_Ordinal_DistinguishesDuplicateEnemyNames_OnRealNode1Battle()
        {
            var s = new GameSession();
            s.NewGame(SkipCreationOptions());
            FastForwardOpeningToMorning(s);
            s.ConfirmMorning();
            var report = s.AdvanceDay();
            Assert.IsTrue(report.AwaitsDecision);

            s.ResolveIncident(IncidentPath.Bloody);
            Assert.AreEqual(SessionState.Battle, s.State);

            var view = s.GetBattleView();
            var byName = new Dictionary<string, List<int>>();
            foreach (var u in view.Units)
            {
                if (!byName.TryGetValue(u.DisplayNameKey, out var list)) byName[u.DisplayNameKey] = list = new List<int>();
                list.Add(u.Ordinal);
            }

            bool sawDuplicateGroup = false;
            foreach (var kv in byName)
            {
                if (kv.Value.Count > 1)
                {
                    sawDuplicateGroup = true;
                    kv.Value.Sort();
                    CollectionAssert.AreEqual(new[] { 1, 2 }, kv.Value, $"дублікати «{kv.Key}» мають отримати 1,2,… без прогалин і повторів");
                }
                else
                {
                    Assert.AreEqual(0, kv.Value[0], $"унікальне ім'я «{kv.Key}» має Ordinal=0");
                }
            }
            Assert.IsTrue(sawDuplicateGroup, "вузол 1 (Node1BloodyEnemyIds) зобов'язаний давати двох однойменних ворогів");
        }

        /// <summary>
        /// Ін'єкція статусу з тривалістю через <c>CombatState.ApplyStatus</c> (публічний
        /// метод ядра) — жодна здібність за замовчуванням (DefaultCombatContent)
        /// не накладає статус із тривалістю без повного розгортання кампанії
        /// (гейт скіла — Companion/Roster), тож пряме застосування — єдиний
        /// прямий шлях перевірити САМЕ мапування GetBattleView, без рефлексії.
        /// </summary>
        [Test]
        public void GetBattleView_StatusDetails_CarriesRemainingTurnsAndDot()
        {
            var map = new GridMap(4, 1);
            var cs = NewCombat(map);
            var unit = U("u", Side.Player, "U", init: 10);
            cs.AddUnit(unit, new GridPos(0, 0));
            cs.AddUnit(U("e", Side.Enemy, "E", init: 1), new GridPos(3, 0));
            cs.Begin();

            cs.ApplyStatus(unit, StatusType.Bleeding);

            var session = new GameSession();
            InjectBattle(session, cs);

            var view = session.GetBattleView();
            var uv = FindUnit(view, "u");
            Assert.AreEqual(1, uv.StatusDetails.Count);
            var status = uv.StatusDetails[0];
            Assert.AreEqual("Bleeding", status.Type);
            Assert.AreEqual(Cfg.Combat.DotDurationTurns, status.RemainingTurns);
            Assert.AreEqual(Cfg.Combat.DotDamagePerTurn, status.DotDamagePerTurn);
            CollectionAssert.Contains(uv.Statuses, "Bleeding");
        }

        // ---------------------------------------------------------------
        // 5. PreviewOverwatchCone (ядро + фасад)
        // ---------------------------------------------------------------

        [Test]
        public void CombatState_PreviewOverwatchCone_MatchesOverwatchCovers_ForRealStance_WithWallBlockingLos()
        {
            var map = new GridMap(8, 8);
            map.SetWall(new GridPos(4, 1)); // блокує LOS для частини конуса
            var cs = NewCombat(map);
            var watcher = U("watcher", Side.Player, "W", init: 10, w: RangedWeapon(range: 6));
            cs.AddUnit(watcher, new GridPos(1, 1));
            cs.AddUnit(U("dummy", Side.Enemy, "D", init: 1), new GridPos(7, 7));
            cs.Begin();

            var aim = new GridPos(7, 1);
            var predicted = cs.PreviewOverwatchCone(watcher, aim);
            var predictedSet = new HashSet<GridPos>(predicted);

            watcher.Overwatch = new OverwatchStance(watcher.Pos, aim, reservedAp: 0);
            for (int y = 0; y < map.Height; y++)
                for (int x = 0; x < map.Width; x++)
                {
                    var tile = new GridPos(x, y);
                    bool actual = cs.OverwatchCovers(watcher, tile);
                    Assert.AreEqual(actual, predictedSet.Contains(tile), $"тайл {tile}");
                }
        }

        [Test]
        public void GameSession_PreviewOverwatchCone_IsPureReadOnly_NoMutation()
        {
            var session = new GameSession();
            session.NewTrainingBattle(new TrainingBattleOptions());
            var before = session.GetBattleView();
            var currentId = before.CurrentUnitId;
            int apBefore = FindUnit(before, currentId).Ap;

            var cone = session.PreviewOverwatchCone(new GridPos(6, 1));
            Assert.IsNotNull(cone);
            Assert.Greater(cone.Count, 0);

            var after = session.GetBattleView();
            Assert.AreEqual(currentId, after.CurrentUnitId, "прев'ю не повинно передавати хід");
            var unit = FindUnit(after, currentId);
            Assert.AreEqual(apBefore, unit.Ap, "прев'ю не повинно списувати AP");
            Assert.IsFalse(unit.IsOverwatching, "прев'ю не повинно фактично ставити дозор");
        }

        // ---------------------------------------------------------------
        // 4. PreviewMovePath
        // ---------------------------------------------------------------

        [Test]
        public void PreviewMovePath_NotReachable_BeyondApBudget()
        {
            var session = new GameSession();
            session.NewTrainingBattle(new TrainingBattleOptions());

            var preview = session.PreviewMovePath(new GridPos(7, 7)); // далеко за 9 AP при ціні 1/тайл
            Assert.AreEqual("NotReachable", preview.Result);
        }

        [Test]
        public void PreviewMovePath_ApCost_MatchesActualCombatMoveSpend_AndTilesMatchPathfinder()
        {
            var session = new GameSession();
            session.NewTrainingBattle(new TrainingBattleOptions());
            var dest = new GridPos(3, 1); // trainee_1 (1,1) -> (3,1)

            var view = session.GetBattleView();
            int apBefore = FindUnit(view, view.CurrentUnitId).Ap;

            var preview = session.PreviewMovePath(dest);
            Assert.AreEqual("Success", preview.Result);
            CollectionAssert.AreEqual(
                new[] { (2, 1), (3, 1) },
                preview.Tiles.Select(t => (t.X, t.Y)).ToList());

            Assert.AreEqual(CombatActionResult.Success, session.CombatMove(dest));

            var afterView = session.GetBattleView();
            var afterUnit = FindUnit(afterView, view.CurrentUnitId);
            Assert.AreEqual(dest.X, afterUnit.Pos.X);
            Assert.AreEqual(dest.Y, afterUnit.Pos.Y);
            Assert.AreEqual(apBefore - preview.ApCost, afterUnit.Ap, "ApCost прев'ю мав дорівнювати реально списаному");
        }

        [Test]
        public void PreviewMovePath_OverwatchThreatTiles_FlagsPathUnderRealEnemyStance()
        {
            var session = new GameSession();
            session.NewTrainingBattle(new TrainingBattleOptions());

            // Порядок ходу Training(): trainee_1, trainee_2, training_scout_1(Enemy), training_scout_2(Enemy).
            Assert.AreEqual("trainee_1", session.GetBattleView().CurrentUnitId);
            session.CombatEndTurn();
            Assert.AreEqual("trainee_2", session.GetBattleView().CurrentUnitId);
            session.CombatEndTurn();

            Assert.AreEqual("training_scout_1", session.GetBattleView().CurrentUnitId);
            Assert.AreEqual(CombatActionResult.Success, session.CombatEnterOverwatch(new GridPos(1, 1)));

            Assert.AreEqual("training_scout_2", session.GetBattleView().CurrentUnitId);
            session.CombatEndTurn();

            Assert.AreEqual("trainee_1", session.GetBattleView().CurrentUnitId); // раунд 2

            var preview = session.PreviewMovePath(new GridPos(3, 1));
            Assert.AreEqual("Success", preview.Result);
            Assert.AreEqual(2, preview.OverwatchThreatTiles.Count, "обидва тайли шляху лежать уздовж лінії дозору (7,1)->(1,1)");
        }

        // ---------------------------------------------------------------
        // 3. PreviewAttack
        // ---------------------------------------------------------------

        [Test]
        public void PreviewAttack_NoBattle_ReturnsNull()
        {
            var session = new GameSession();
            Assert.IsNull(session.PreviewAttack("a", "b"));
        }

        [Test]
        public void PreviewAttack_WeaponAttack_ChanceEqualsSumOfTerms_AndMatchesLoggedOutcome()
        {
            var session = new GameSession();
            session.NewTrainingBattle(new TrainingBattleOptions()); // Threshold — детермінований урон

            // trainee_1 (перший хід) б'є мілі-списом (OptimalRange=1) з (1,1) —
            // на старті бою обидва ворога поза контактом; пропускаємо його хід
            // до trainee_2 (лук, дальність не гейтить Attack — лише LOS).
            session.CombatEndTurn();

            var view = session.GetBattleView();
            var attackerId = view.CurrentUnitId;
            string targetId = view.Units.First(u => u.Side == "Enemy").Id;

            var preview = session.PreviewAttack(attackerId, targetId);
            Assert.AreEqual("Success", preview.Result);
            Assert.IsTrue(preview.HasAttackRoll);
            Assert.AreEqual(attackerId, preview.AttackerId);
            Assert.AreEqual(targetId, preview.TargetId);
            Assert.IsNull(preview.AbilityId);
            Assert.IsFalse(preview.IsPercent);
            Assert.IsTrue(preview.IsDamageDeterministic);

            int sum = preview.Terms.Sum(t => t.ChanceDelta);
            Assert.AreEqual(preview.Chance, sum, "Chance зобов'язаний дорівнювати сумі власних Terms");

            Assert.AreEqual(CombatActionResult.Success, session.CombatAttack(targetId));

            var log = session.GetBattleView().Log;
            var entry = log.Last(e => e.Key.StartsWith("combat.log.attack."));
            Assert.AreEqual(preview.Chance.ToString(CultureInfo.InvariantCulture), entry.Args["chance"],
                "прев'ю розійшлося з фактично залогованим шансом");

            if (entry.Key == CombatLogKeys.AttackHit)
                Assert.AreEqual(preview.DamageExpected.ToString(CultureInfo.InvariantCulture), entry.Args["damage"],
                    "DamageExpected мав збігтися з фактичною шкодою звичайного влучання під ThresholdRule");
        }

        [Test]
        public void PreviewAttack_NotEnoughAp_MirrorsCoreValidation()
        {
            var session = new GameSession();
            session.NewTrainingBattle(new TrainingBattleOptions());
            var view = session.GetBattleView();
            var attackerId = view.CurrentUnitId;
            string targetId = view.Units.First(u => u.Side == "Enemy").Id;

            var battle = InjectBattleGetter(session);
            var attacker = battle.GetUnit(attackerId);
            attacker.Ap = attacker.Weapon.ApCost - 1;

            var preview = session.PreviewAttack(attackerId, targetId);
            Assert.AreEqual("NotEnoughAp", preview.Result);
            Assert.AreEqual(CombatActionResult.NotEnoughAp, battle.Attack(targetId), "прев'ю мало збігтись із реальною відмовою CombatState.Attack");
        }

        [Test]
        public void PreviewAttack_InvalidTarget_ForAllySide()
        {
            var session = new GameSession();
            session.NewTrainingBattle(new TrainingBattleOptions());
            var view = session.GetBattleView();
            var attackerId = view.CurrentUnitId;
            string allyId = view.Units.First(u => u.Side == "Player" && u.Id != attackerId).Id;

            var preview = session.PreviewAttack(attackerId, allyId);
            Assert.AreEqual("InvalidTarget", preview.Result);
        }

        [Test]
        public void PreviewAttack_AbilityWithoutWeaponAttack_HasAttackRollFalse_ButValidatesRangeApAndCooldown()
        {
            var battle = BuildLungeScenario(out var brawler, out var enemy);
            var session = new GameSession();
            InjectBattle(session, battle);

            var preview = session.PreviewAttack(brawler.Id, enemy.Id, DefaultCombatContent.Lunge().Id);
            Assert.AreEqual("Success", preview.Result);
            Assert.IsFalse(preview.HasAttackRoll);
            Assert.AreEqual(0, preview.Chance);
            Assert.AreEqual(DefaultCombatContent.Lunge().ApCost, preview.ApCost);
            Assert.AreEqual(brawler.Id, preview.AttackerId);
            Assert.AreEqual(enemy.Id, preview.TargetId);
            Assert.AreEqual(DefaultCombatContent.Lunge().Id, preview.AbilityId);

            // Відкат: мовиться той самий OnCooldown, що дав би UseAbility
            // (CombatUnit.SetCooldown — internal, доступний тесту через InternalsVisibleTo).
            brawler.SetCooldown(DefaultCombatContent.Lunge().Id, 2);
            var onCooldown = session.PreviewAttack(brawler.Id, enemy.Id, DefaultCombatContent.Lunge().Id);
            Assert.AreEqual("OnCooldown", onCooldown.Result);
        }

        [Test]
        public void PreviewAttack_AbilityWithWeaponAttack_ChanceIncludesAbilityBonus_AndMatchesActualRoll()
        {
            var battle = BuildVolleyScenario(out var shooter, out var target);
            var session = new GameSession();
            InjectBattle(session, battle);

            var volley = DefaultCombatContent.Volley();
            var preview = session.PreviewAttack(shooter.Id, target.Id, volley.Id);
            Assert.AreEqual("Success", preview.Result);
            Assert.IsTrue(preview.HasAttackRoll);
            Assert.AreEqual(volley.ApCost, preview.ApCost);

            int sum = preview.Terms.Sum(t => t.ChanceDelta);
            Assert.AreEqual(preview.Chance, sum);

            var abilityTerm = preview.Terms.First(t => t.Key == "ability");
            Assert.AreEqual(volley.PreviewAccuracyBonus(), abilityTerm.ChanceDelta);

            int before = battle.Attacks.Count;
            Assert.AreEqual(CombatActionResult.Success, battle.UseAbility(volley.Id, target.Id));
            Assert.AreEqual(before + 2, battle.Attacks.Count, "Черга — два постріли за один виклик");
            Assert.AreEqual(preview.Chance, battle.Attacks[before].Chance, "прев'ю мало показати шанс ПЕРШОГО пострілу Черги");
        }

        // ---------------------------------------------------------------
        // 6. CombatStabilize
        // ---------------------------------------------------------------

        [Test]
        public void CombatStabilize_ThrowsWhenNoBattle()
        {
            var session = new GameSession();
            Assert.Throws<InvalidOperationException>(() => session.CombatStabilize("x"));
        }

        [Test]
        public void CombatStabilize_MirrorsCoreValidation_MedicineGate()
        {
            var session = new GameSession();
            session.NewTrainingBattle(new TrainingBattleOptions());
            // Жоден трейні не має Медицини (MedicineSkill=0 обом) — фасад мусить
            // повернути ту саму InvalidAction, що дав би CombatState.Stabilize.
            var result = session.CombatStabilize("trainee_2");
            Assert.AreEqual(CombatActionResult.InvalidAction, result);
            Assert.AreEqual(SessionState.Battle, session.State, "невдала спроба стабілізації не мала завершити бій");
        }

        // ---------------------------------------------------------------
        // 7. Журнал: "path" на русі, "ap"/"cover" на атаці
        // ---------------------------------------------------------------

        [Test]
        public void CombatMove_LogsPathArg_MatchingPreviewTiles()
        {
            var session = new GameSession();
            session.NewTrainingBattle(new TrainingBattleOptions());
            var dest = new GridPos(3, 1);

            var preview = session.PreviewMovePath(dest);
            Assert.AreEqual("Success", preview.Result);

            Assert.AreEqual(CombatActionResult.Success, session.CombatMove(dest));

            var log = session.GetBattleView().Log;
            var moveEntry = log.Last(e => e.Key == CombatLogKeys.Move);
            Assert.IsTrue(moveEntry.Args.TryGetValue("path", out var pathArg));

            string expected = string.Join(";", preview.Tiles.Select(t => t.X + "," + t.Y));
            Assert.AreEqual(expected, pathArg);
            Assert.AreEqual("2,1;3,1", pathArg);
        }

        [Test]
        public void CombatAttack_LogsApAndCover_ForTheOutcome()
        {
            var session = new GameSession();
            session.NewTrainingBattle(new TrainingBattleOptions());
            session.CombatEndTurn(); // trainee_1 (мілі, поза контактом) пасує до trainee_2 (лук)
            var view = session.GetBattleView();
            string targetId = view.Units.First(u => u.Side == "Enemy").Id;

            Assert.AreEqual(CombatActionResult.Success, session.CombatAttack(targetId));

            var log = session.GetBattleView().Log;
            var entry = log.Last(e => e.Key.StartsWith("combat.log.attack."));
            Assert.IsTrue(entry.Args.ContainsKey("ap"));
            Assert.IsTrue(entry.Args.ContainsKey("cover"));
            CollectionAssert.Contains(new[] { "None", "Half", "Full" }, entry.Args["cover"]);
        }

        [Test]
        public void AbilityLunge_LogsFromAndPath_ForTheJump()
        {
            var battle = BuildLungeScenario(out var brawler, out var enemy);
            var session = new GameSession();
            InjectBattle(session, battle);

            Assert.AreEqual(CombatActionResult.Success, battle.UseAbility(DefaultCombatContent.Lunge().Id, enemy.Id));

            var entry = battle.Journal.Last(e => e.Key == CombatLogKeys.Lunge);
            Assert.IsTrue(entry.Args.TryGetValue("from", out var from));
            Assert.AreEqual("0,0", from);
            Assert.IsTrue(entry.Args.TryGetValue("path", out var path));
            Assert.AreEqual($"{brawler.Pos.X},{brawler.Pos.Y}", path);
        }

        // ---------------------------------------------------------------
        // 9. Регресія скарги власника: хід ІІ ніколи не зависає
        // ---------------------------------------------------------------

        /// <summary>
        /// Точна регресія скарги («Я походив своїми чуваками і коли наступив
        /// ход опонентів гра тупа зупинилась і воні нічого не робили»): цикл
        /// «поки IsAiTurn — CombatAiStepOneAction()», інакше CombatEndTurn на
        /// своєму ході, доводить бій до кінця (Victory/Defeat/Draw) за обмежену
        /// кількість кроків — ніколи не кидає InvalidOperationException, поки
        /// State==Battle.
        /// </summary>
        [Test]
        public void CombatAiStepOneAction_DrivesEnemyTurnsToCompletion_NeverThrowsWhileBattleOngoing()
        {
            var session = new GameSession();
            session.NewTrainingBattle(new TrainingBattleOptions());

            int steps = 0;
            const int maxSteps = 3000;
            while (session.State == SessionState.Battle)
            {
                var view = session.GetBattleView();
                Assert.IsNotNull(view, "у стані Battle GetBattleView не повинен повертати null");

                if (view.IsAiTurn)
                    Assert.DoesNotThrow(() => session.CombatAiStepOneAction(),
                        "CombatAiStepOneAction не повинен кидати, поки State==Battle");
                else
                    session.CombatEndTurn();

                steps++;
                Assert.Less(steps, maxSteps,
                    "Бій завис: ні гравець, ні ШІ не завершують хід за розумну кількість кроків — рівно та скарга власника.");
            }

            Assert.AreNotEqual(SessionState.Battle, session.State, "бій дійсно мав завершитись (Victory/Defeat/Draw), а не зависнути");
        }

        /// <summary>
        /// Дзеркало знахідки аудиту (CORE-1): CombatAiStepOneAction кидає
        /// InvalidOperationException РІВНО тоді, коли бою вже немає (State
        /// повернувся з Battle) — ніколи, поки State==Battle.
        /// </summary>
        [Test]
        public void CombatAiStepOneAction_ThrowsInvalidOperationException_OnlyAfterBattleHasEnded()
        {
            var session = new GameSession();
            session.NewTrainingBattle(new TrainingBattleOptions());

            int guard = 0;
            while (session.State == SessionState.Battle && guard++ < 3000)
            {
                var view = session.GetBattleView();
                if (view.IsAiTurn) session.CombatAiStepOneAction();
                else session.CombatEndTurn();
            }

            Assert.AreNotEqual(SessionState.Battle, session.State);
            Assert.Throws<InvalidOperationException>(() => session.CombatAiStepOneAction());
        }

        private static CombatState InjectBattleGetter(GameSession session)
        {
            var field = typeof(GameSession).GetField("_battle", BindingFlags.NonPublic | BindingFlags.Instance);
            return (CombatState)field.GetValue(session);
        }

        /// <summary>
        /// Тренувальний бій з титулу йде без партії. Оболонка після кожної
        /// команди оновлює сцену села (ростер + місто) — раніше ці види кидали
        /// NullReferenceException, і кнопка «Тренувальний бій» щоразу валила
        /// команду (знайдено туром -autoplay-battle, 25.09.2026).
        /// </summary>
        [Test]
        public void TrainingBattleFromTitle_WorldViewsAreEmpty_NotThrowing()
        {
            var s = new GameSession();
            s.NewTrainingBattle(new TrainingBattleOptions { HitRule = HitRuleKind.Threshold });
            Assert.AreEqual(SessionState.Battle, s.State);

            RosterView roster = null;
            CityView city = null;
            Assert.DoesNotThrow(() => roster = s.GetRosterView());
            Assert.DoesNotThrow(() => city = s.GetCityView());
            Assert.IsNotNull(roster.Companions);
            Assert.IsEmpty(roster.Companions);
            Assert.IsNotNull(city.Built);
            Assert.IsEmpty(city.Built);
            Assert.IsNotNull(s.GetBattleView(), "сам бій при цьому живий");
        }
    }
}
