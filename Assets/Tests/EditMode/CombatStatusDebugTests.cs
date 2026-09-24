using System.Linq;
using Game.Core.Balance;
using Game.Core.Combat;
using Game.Core.Stats;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// Дебаг §6.1 №32 (24.09.2026): чому <c>StatusOnHit</c> (KnockedDown на
    /// BurundaMace) НІ РАЗУ не спрацьовував за детермінований кровавий фінал —
    /// ні в бот-прогоні (AllMechanicsCoverageTests.Row32), ні в UI-турі.
    ///
    /// Корінь: гейт статус-проку на попаданні (Hit/Crit, НЕ Graze —
    /// CombatState.ExecuteAttackRoll, навмисно, "граза = лише половина урону,
    /// без проків") у ThresholdRule (детермінований дефолт GameSession)
    /// відкривається лише з margin ≥ ThresholdGrazeBand (15), тобто shown ≥ 65.
    /// Accuracy Бурунди (65) проти РЕАЛІСТИЧНОГО Defense напарника/протагоніста
    /// з моделі персонажа Епіка 2 (Defense = Agility 1..10 напряму,
    /// DerivedStats.cs, стеля 10) завжди давало shown 55..64 — ЗАВЖДИ Graze,
    /// НІКОЛИ Hit. Strike-метр (єдиний детермінований вихід на гарантований
    /// удар) сам копиться лише з Hit/Crit (GDD.md:119, навмисно) — без
    /// природного Hit пастка не відкривалась ніколи для цього конкретного
    /// бою: не рідкість, а структурна недосяжність.
    ///
    /// Виправлено: <c>DefaultCombatContent.Burunda().Accuracy</c> 65 -> 80
    /// (margin проти Defense 0..10 стає 10..30 — завжди Hit, при Defense ≤ 5 —
    /// Crit). AllMechanicsCoverageTests.Row32 також вимагав другого виправлення
    /// (не тут): наївний тестовий водій бою (BotRunner.ExecuteCombatAction) не
    /// вміє зближувати мілі-юнітів способністю і програвав явно нерівний бій
    /// раніше, ніж Бурунда встигав дійти до контакту — <c>CombatIntent.
    /// SmartAiTurn</c> / <c>GameSession.CombatAiStepOneAction</c> дають той
    /// самий "розумний" ІІ, що веде АвтоБій, поштучно для спостережуваності.
    ///
    /// Цей файл лишається як регресія на сам гейт (не на бот-водія — те
    /// покриває Row32) і як систематична перевірка "застосування-ефект-згасання"
    /// для кожного типу стану ЧЕРЕЗ АТАКУ (не лише прямий
    /// <see cref="CombatState.ApplyStatus"/>, як TurnAndStatusTests), в ОБОХ
    /// правилах попадання.
    /// </summary>
    public class CombatStatusDebugTests
    {
        private static readonly BalanceConfig Cfg = new BalanceConfig();

        private static CombatUnit Target(int defense, int hp = 1000, int resolve = 0)
        {
            var p = new UnitProfile { DisplayName = "target", MaxHp = hp, MaxAp = 8, Accuracy = 50,
                Defense = defense, Initiative = 5, CritChance = 0, Armor = 0, Resolve = resolve, MoveApPerTile = 1 };
            return new CombatUnit("target", Side.Player, p, null);
        }

        // =====================================================================
        // Карта "показане число -> проц статусу" в обох правилах попадання
        // =====================================================================

        /// <summary>
        /// По одному разу на "показане число" 0..100: в яку смугу вихіду
        /// ThresholdRule кладе margin, і з якого margin реально починає
        /// викликатись StatusOnHit (лише Hit/Crit, CombatState.
        /// ExecuteAttackRoll). Це карта, а не судження про баланс.
        /// </summary>
        [Test]
        public void ThresholdRule_StatusProcs_OnlyFromShownAtOrAboveHitBand()
        {
            var rule = new ThresholdRule(Cfg);
            int firstShownThatProcs = -1;

            for (int shown = 0; shown <= 100; shown += 5)
            {
                var outcome = rule.Resolve(null, null, shown, null);
                bool procs = outcome == AttackOutcome.Hit || outcome == AttackOutcome.Crit;
                if (procs && firstShownThatProcs < 0) firstShownThatProcs = shown;
            }

            // Документуємо факт: смуга Graze [Baseline, Baseline+GrazeBand) — не
            // нульова ширина, і будь-яке shown у ній НЕ пускає StatusOnHit далі.
            Assert.AreEqual(Cfg.Combat.ThresholdBaseline + Cfg.Combat.ThresholdGrazeBand, firstShownThatProcs,
                "Перше 'shown', на якому Hit/Crit (і відповідно StatusOnHit) настає під Threshold");
        }

        /// <summary>
        /// Те саме питання під Percent-правилом: тут Hit/Crit настає
        /// приблизно у shown% випадків (roll менший за shown) — принципово
        /// інша форма розподілу при тому самому "показаному числі", ніж
        /// margin-смуги Threshold вище.
        /// </summary>
        [Test]
        public void PercentRule_StatusProcs_AtApproximatelyShownPercentOfAttacks()
        {
            var rule = new PercentRule(Cfg);
            var a = CombatUnit.FromEnemy(DefaultCombatContent.Burunda(), "attacker");
            var t = Target(0);
            const int shown = 60;
            const int samples = 100;

            int hitOrCrit = 0;
            for (int i = 0; i < samples; i++)
            {
                double roll = i / (double)samples; // рівномірна сітка 0.00..0.99
                var roller = new ScriptedDiceRoller(roll, 0.99); // другий бросок (crit) свідомо не крит
                var outcome = rule.Resolve(a, t, shown, roller);
                if (outcome == AttackOutcome.Hit || outcome == AttackOutcome.Crit) hitOrCrit++;
            }

            // roll<60 => Hit на рівномірній сітці 0..99 — рівно 60 зі 100.
            Assert.AreEqual(60, hitOrCrit,
                "Percent-правило пускає StatusOnHit ~на shown% атак — інакше, ніж margin-смуги Threshold");
        }

        // =====================================================================
        // Регресія самої причини №32: BurundaMace проти реалістичного Defense
        // =====================================================================

        /// <summary>
        /// Прямий прогін <see cref="CombatState.Attack"/> (не низькорівневий
        /// IHitRule окремо, а бойовий цикл цілком) для BurundaMace проти цілі
        /// з РЕАЛІСТИЧНИМ Defense (0..10 — весь діапазон DerivedStat.Defense =
        /// DefenseBase(0) + Agility(1..10) × DefensePerAgility(1) з поточної
        /// моделі персонажа). До виправлення Accuracy це завжди був Graze
        /// (0 разів з 6) — тепер завжди Hit/Crit.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(3)]
        [TestCase(5)]
        [TestCase(8)]
        [TestCase(10)]
        public void BurundaMace_VsRealisticDefense_AlwaysProcsKnockedDown_UnderThreshold(int targetDefense)
        {
            var map = new GridMap(5, 1);
            var cs = new CombatState(map, Cfg, new ThresholdRule(Cfg), null);
            var burunda = CombatUnit.FromEnemy(DefaultCombatContent.Burunda(), "burunda");
            var target = Target(targetDefense);
            cs.AddUnit(burunda, new GridPos(0, 0));
            cs.AddUnit(target, new GridPos(1, 0));
            cs.Begin();

            int shown = cs.HitChancePreview(burunda, target);
            int margin = shown - Cfg.Combat.ThresholdBaseline;
            Assert.GreaterOrEqual(margin, Cfg.Combat.ThresholdGrazeBand,
                $"Defense={targetDefense}: shown={shown}, margin={margin} мав бути ≥ смуги Graze після Accuracy 65->80");

            Assert.AreEqual(CombatActionResult.Success, cs.Attack(target.Id));
            var outcome = cs.Attacks.Last().Outcome;

            Assert.IsTrue(outcome == AttackOutcome.Hit || outcome == AttackOutcome.Crit,
                $"Defense={targetDefense}: outcome={outcome} мав бути Hit/Crit");
            Assert.IsTrue(target.HasStatus(StatusType.KnockedDown),
                $"Defense={targetDefense}: StatusOnHit мав застосуватись на {outcome}");
        }

        /// <summary>Те саме, під Percent-правилом (roller, що завжди дає влучання) — обидва правила попадання мають пускати прок.</summary>
        [TestCase(0)]
        [TestCase(5)]
        [TestCase(10)]
        public void BurundaMace_VsRealisticDefense_ProcsKnockedDown_UnderPercent(int targetDefense)
        {
            var map = new GridMap(5, 1);
            var roller = new ScriptedDiceRoller(0.0, 0.99); // завжди влучання, ніколи не крит
            var cs = new CombatState(map, Cfg, new PercentRule(Cfg), roller);
            var burunda = CombatUnit.FromEnemy(DefaultCombatContent.Burunda(), "burunda");
            var target = Target(targetDefense);
            cs.AddUnit(burunda, new GridPos(0, 0));
            cs.AddUnit(target, new GridPos(1, 0));
            cs.Begin();

            Assert.AreEqual(CombatActionResult.Success, cs.Attack(target.Id));
            Assert.AreEqual(AttackOutcome.Hit, cs.Attacks.Last().Outcome);
            Assert.IsTrue(target.HasStatus(StatusType.KnockedDown),
                $"Defense={targetDefense}: StatusOnHit мав застосуватись під Percent так само, як під Threshold");
        }

        // =====================================================================
        // Кожен тип стану через АТАКУ (не прямий ApplyStatus, як
        // TurnAndStatusTests) — застосування, ефект, згасання — в ОБОХ
        // правилах попадання. ApplyStatus/тік — той самий код незалежно від
        // IHitRule, тож "ефект"/"згасання" тут — та ж перевірка під обома
        // правилами; відрізняється лише ЯК атака доходить до Hit/Crit.
        // =====================================================================

        private static WeaponDefinition StatusWeapon(StatusType status)
            => new WeaponDefinition("w.status", "status_weapon", SkillType.Melee)
            {
                DamageMin = 1, DamageMax = 1, ApCost = 4, OptimalRange = 1, StatusOnHit = status
            };

        private static CombatUnit Attacker(WeaponDefinition w)
        {
            var p = new UnitProfile { DisplayName = "attacker", MaxHp = 30, MaxAp = 10, Accuracy = 100,
                Defense = 0, Initiative = 10, CritChance = 0, Armor = 0, Resolve = 0, MoveApPerTile = 1 };
            return new CombatUnit("attacker", Side.Enemy, p, w);
        }

        private static CombatState ThresholdCombat(out CombatUnit attacker, out CombatUnit target, StatusType status, int hp = 20, int targetDefense = 0)
        {
            var map = new GridMap(5, 1);
            var cs = new CombatState(map, Cfg, new ThresholdRule(Cfg), null);
            attacker = Attacker(StatusWeapon(status));
            target = Target(defense: targetDefense, hp: hp);
            cs.AddUnit(attacker, new GridPos(0, 0));
            cs.AddUnit(target, new GridPos(1, 0));
            cs.Begin(); // attacker (Initiative 10 > 5) ходить першим
            return cs;
        }

        private static CombatState PercentCombat(out CombatUnit attacker, out CombatUnit target, StatusType status, int hp = 20, int targetDefense = 0)
        {
            var map = new GridMap(5, 1);
            var roller = new ScriptedDiceRoller(0.0, 0.99); // завжди влучання, ніколи не крит
            var cs = new CombatState(map, Cfg, new PercentRule(Cfg), roller);
            attacker = Attacker(StatusWeapon(status));
            target = Target(defense: targetDefense, hp: hp);
            cs.AddUnit(attacker, new GridPos(0, 0));
            cs.AddUnit(target, new GridPos(1, 0));
            cs.Begin();
            return cs;
        }

        // ---- Suppressed: застосування + подорожчання руху весь СВІЙ хід, згасання наприкінці свого ходу ----
        [TestCase(false)]
        [TestCase(true)]
        public void Suppressed_ViaAttack_AppliesEffectAndExpires(bool percent)
        {
            var cs = percent ? PercentCombat(out var a1, out var t1, StatusType.Suppressed)
                              : ThresholdCombat(out a1, out t1, StatusType.Suppressed);
            var attacker = cs.Units[0];
            var target = cs.Units[1];

            Assert.AreEqual(CombatActionResult.Success, cs.Attack(target.Id));
            Assert.IsTrue(target.HasStatus(StatusType.Suppressed), "застосування: статус мав з'явитись на цілі");

            cs.EndTurn(); // хід переходить до target
            Assert.AreSame(target, cs.Current);
            Assert.IsTrue(target.HasStatus(StatusType.Suppressed), "ефект: діє під час власного ходу цілі");
            Assert.AreEqual(2, cs.MoveCostPerTile(target), "ефект: подорожчання руху (ceil(1 × 1.5))");

            cs.EndTurn(); // кінець ходу target — статус згасає
            Assert.IsFalse(target.HasStatus(StatusType.Suppressed), "згасання: статус має спасти наприкінці ходу цілі");
        }

        // ---- Stunned: застосування + пропуск ходу (AP=0) на початку свого ходу, статус витрачається ----
        [TestCase(false)]
        [TestCase(true)]
        public void Stunned_ViaAttack_SkipsTargetTurn_ThenConsumed(bool percent)
        {
            var cs = percent ? PercentCombat(out var a1, out var t1, StatusType.Stunned)
                              : ThresholdCombat(out a1, out t1, StatusType.Stunned);
            var target = cs.Units[1];

            Assert.AreEqual(CombatActionResult.Success, cs.Attack(target.Id));
            Assert.IsTrue(target.HasStatus(StatusType.Stunned), "застосування");

            cs.EndTurn(); // початок ходу target: Оглушення з'їдає AP
            Assert.AreSame(target, cs.Current);
            Assert.AreEqual(0, target.Ap, "ефект: оглушений пропускає хід");
            Assert.IsFalse(target.HasStatus(StatusType.Stunned), "згасання: статус витрачається одразу");
        }

        // ---- Bleeding: застосування + DoT на початку свого ходу, тривалість тікає до нуля ----
        [TestCase(false)]
        [TestCase(true)]
        public void Bleeding_ViaAttack_TicksDot_ThenExpires(bool percent)
        {
            var cs = percent ? PercentCombat(out var a1, out var t1, StatusType.Bleeding)
                              : ThresholdCombat(out a1, out t1, StatusType.Bleeding);
            var target = cs.Units[1];
            int hpBefore = target.Hp;

            Assert.AreEqual(CombatActionResult.Success, cs.Attack(target.Id));
            var st = target.GetStatus(StatusType.Bleeding);
            Assert.IsNotNull(st, "застосування");

            cs.EndTurn(); // початок ходу target: тік DoT
            Assert.Less(target.Hp, hpBefore, "ефект: кровотеча знімає HP на початку ходу цілі");

            RunUntilStatusGone(cs, target, StatusType.Bleeding);
            Assert.IsNull(target.GetStatus(StatusType.Bleeding), "згасання: статус має спасти після вичерпання тривалості");
        }

        // ---- Burning: застосування + типізований вогняний DoT (з урахуванням резисту), тривалість тікає ----
        [TestCase(false)]
        [TestCase(true)]
        public void Burning_ViaAttack_TicksFireDot_ThenExpires(bool percent)
        {
            var cs = percent ? PercentCombat(out var a1, out var t1, StatusType.Burning)
                              : ThresholdCombat(out a1, out t1, StatusType.Burning);
            var target = cs.Units[1];
            int hpBefore = target.Hp;

            Assert.AreEqual(CombatActionResult.Success, cs.Attack(target.Id));
            var st = target.GetStatus(StatusType.Burning);
            Assert.IsNotNull(st, "застосування");
            Assert.AreEqual(DamageType.Fire, st.DotType, "ефект: DoT типізований як вогонь (резист/уразливість застосовні)");

            cs.EndTurn();
            Assert.Less(target.Hp, hpBefore, "ефект: підпал знімає HP на початку ходу цілі");

            RunUntilStatusGone(cs, target, StatusType.Burning);
            Assert.IsNull(target.GetStatus(StatusType.Burning), "згасання");
        }

        // ---- Poisoned: застосування + типізований токсичний DoT, тривалість тікає ----
        [TestCase(false)]
        [TestCase(true)]
        public void Poisoned_ViaAttack_TicksToxinDot_ThenExpires(bool percent)
        {
            var cs = percent ? PercentCombat(out var a1, out var t1, StatusType.Poisoned)
                              : ThresholdCombat(out a1, out t1, StatusType.Poisoned);
            var target = cs.Units[1];
            int hpBefore = target.Hp;

            Assert.AreEqual(CombatActionResult.Success, cs.Attack(target.Id));
            var st = target.GetStatus(StatusType.Poisoned);
            Assert.IsNotNull(st, "застосування");
            Assert.AreEqual(DamageType.Toxin, st.DotType, "ефект: DoT типізований як токсин");

            cs.EndTurn();
            Assert.Less(target.Hp, hpBefore, "ефект: отрута знімає HP на початку ходу цілі");

            RunUntilStatusGone(cs, target, StatusType.Poisoned);
            Assert.IsNull(target.GetStatus(StatusType.Poisoned), "згасання");
        }

        // ---- KnockedDown: застосування + падіння захисту цілі (легша ціль) + AP на підйом, статус витрачається ----
        [TestCase(false)]
        [TestCase(true)]
        public void KnockedDown_ViaAttack_LowersTargetDefense_ThenConsumedOnStandUp(bool percent)
        {
            // Defense=20 (не 0): дає запас під HitChanceMax=99, щоб зростання
            // від KnockdownDefensePenalty (10) було видимим, а не з'їденим клампом.
            var cs = percent ? PercentCombat(out var a1, out var t1, StatusType.KnockedDown, hp: 100, targetDefense: 20)
                              : ThresholdCombat(out a1, out t1, StatusType.KnockedDown, hp: 100, targetDefense: 20);
            var attacker = cs.Units[0];
            var target = cs.Units[1];

            int chanceBefore = cs.HitChancePreview(attacker, target);
            Assert.AreEqual(CombatActionResult.Success, cs.Attack(target.Id));
            Assert.IsTrue(target.HasStatus(StatusType.KnockedDown), "застосування");

            int chanceAfter = cs.HitChancePreview(attacker, target);
            Assert.Greater(chanceAfter, chanceBefore,
                "ефект: збита з ніг ціль легша (захист проседає, KnockdownDefensePenalty)");

            cs.EndTurn(); // кінець ходу attacker -> початок ходу target: підйом на ноги (BeginTurn), AP −StandUpApCost
            Assert.AreSame(target, cs.Current);
            Assert.IsFalse(target.HasStatus(StatusType.KnockedDown), "згасання: статус витрачається, коли ціль підводиться на початку свого ходу");
            Assert.AreEqual(target.Profile.MaxAp - Cfg.Combat.StandUpApCost, target.Ap, "підйом коштує AP");
        }

        // ---- Marked: застосування + бонус атакуючому по цій цілі, згасання за тривалістю ----
        [TestCase(false)]
        [TestCase(true)]
        public void Marked_ViaAttack_GrantsAttackerBonus_ThenExpires(bool percent)
        {
            // Defense=20: запас під клампом HitChanceMax=99 для MarkedHitBonus (10).
            var cs = percent ? PercentCombat(out var a1, out var t1, StatusType.Marked, hp: 100, targetDefense: 20)
                              : ThresholdCombat(out a1, out t1, StatusType.Marked, hp: 100, targetDefense: 20);
            var attacker = cs.Units[0];
            var target = cs.Units[1];

            int chanceBefore = cs.HitChancePreview(attacker, target);
            Assert.AreEqual(CombatActionResult.Success, cs.Attack(target.Id));
            Assert.IsNotNull(target.GetStatus(StatusType.Marked), "застосування");

            int chanceAfter = cs.HitChancePreview(attacker, target);
            Assert.Greater(chanceAfter, chanceBefore, "ефект: мітка дає бонус до влучання по цілі (MarkedHitBonus)");

            RunUntilStatusGone(cs, target, StatusType.Marked);
            Assert.IsNull(target.GetStatus(StatusType.Marked), "згасання: статус має спасти після вичерпання тривалості");
        }

        /// <summary>
        /// Доганяє <paramref name="type"/> до зникнення: <c>TickStatusDurations</c>
        /// зменшує довговічність лише в кінці ВЛАСНОГО ходу носія
        /// (<see cref="CombatState.EndTurn"/>), а при двох юнітах, що
        /// чергуються, це кожен ДРУГИЙ виклик EndTurn — рахувати вручну
        /// крихко (і <c>StatusInstance.RemainingTurns</c> — клас, тож "живе"
        /// поле в умові циклу саме змінюється під час цього ж циклу). Простіше
        /// й надійніше — крутити ходи, доки статус не спаде сам, з запобіжником.
        /// </summary>
        private static void RunUntilStatusGone(CombatState cs, CombatUnit unit, StatusType type)
        {
            int guard = 20;
            while (unit.GetStatus(type) != null && guard-- > 0) cs.EndTurn();
        }
    }
}
