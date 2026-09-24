using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Randomness;
using Game.Core.Stats;

namespace Game.Core.Combat
{
    /// <summary>
    /// Канонический боевой контент среза (Б1): оружие/враги/способности из
    /// первой игровой доби (§3.1 TEST_BUILD.md) — авангард орди, бояри Тугара,
    /// Бурунда-бегадир (фінальний бос), плюс тренувальний бій для швидкої
    /// оцінки бою з титульного меню. Ідентифікатори (`enemy.*`) — майбутні
    /// текстові ключі (R7): справжні українські рядки складе E3, тут —
    /// службовий DisplayName для логів/тестів, гравець його не бачить.
    /// </summary>
    public static class DefaultCombatContent
    {
        // ---- Оружие ----

        public static WeaponDefinition HordeBow() => new WeaponDefinition("weapon.horde_bow", "horde_bow", SkillType.Ranged)
        {
            Damage = DamageType.Ballistic, DamageMin = 2, DamageMax = 4, CritDamageBonus = 2,
            ApCost = 3, OptimalRange = 6
        };

        public static WeaponDefinition HordeSpear() => new WeaponDefinition("weapon.horde_spear", "horde_spear", SkillType.Melee)
        {
            Damage = DamageType.Ballistic, DamageMin = 3, DamageMax = 5, CritDamageBonus = 2,
            ApCost = 3, OptimalRange = 1
        };

        public static WeaponDefinition BoyarSaber() => new WeaponDefinition("weapon.boyar_saber", "boyar_saber", SkillType.Melee)
        {
            Damage = DamageType.Ballistic, DamageMin = 4, DamageMax = 6, CritDamageBonus = 3,
            ApCost = 3, OptimalRange = 1, ShredOnHit = 1
        };

        public static WeaponDefinition BurundaMace() => new WeaponDefinition("weapon.burunda_mace", "burunda_mace", SkillType.Melee)
        {
            Damage = DamageType.Ballistic, DamageMin = 5, DamageMax = 8, CritDamageBonus = 4,
            ApCost = 4, OptimalRange = 1, ShredOnHit = 1, StatusOnHit = StatusType.KnockedDown
        };

        // ---- Способности (общий пул: и напарники по гейту скила, и враги напрямую) ----

        /// <summary>Рывок в ближний контакт — клинч-юниты вне дистанции.</summary>
        public static AbilityDefinition Lunge() =>
            new AbilityDefinition("ability.lunge", "lunge", SkillType.Melee, 3)
                .Costs(ap: 2, cooldown: 3)
                .Targets(AbilityTarget.Enemy, range: 6, needsLos: true)
                .WithEffect(new AbilityEffect(AbilityEffectKind.LungeToTarget));

        /// <summary>Ловушка на тайле — активка Выживания.</summary>
        public static AbilityDefinition SetTrap() =>
            new AbilityDefinition("ability.set_trap", "set_trap", SkillType.Survival, 4)
                .Costs(ap: 2, cooldown: 4)
                .Targets(AbilityTarget.Tile, range: 3, needsLos: true)
                .WithEffect(new AbilityEffect { Kind = AbilityEffectKind.PlaceTrap, Amount = 3, Damage = DamageType.True });

        /// <summary>Командный рывок: переставить союзника — активка Тактики.</summary>
        public static AbilityDefinition MoveOrder() =>
            new AbilityDefinition("ability.move_order", "move_order", SkillType.Tactics, 4)
                .Costs(ap: 2, cooldown: 3)
                .Targets(AbilityTarget.Ally, range: 8, needsLos: true)
                .WithEffect(new AbilityEffect(AbilityEffectKind.RepositionTarget, amount: 4));

        /// <summary>Очередь/два удара подряд текущим оружием — усиленный залп при уверенном шансе.</summary>
        public static AbilityDefinition Volley() =>
            new AbilityDefinition("ability.volley", "volley", SkillType.Ranged, 5)
                .Costs(ap: 4, cooldown: 2)
                .Targets(AbilityTarget.Enemy, range: 8, needsLos: true)
                .WithEffect(new AbilityEffect { Kind = AbilityEffectKind.WeaponAttack, AccuracyBonus = -10 })
                .WithEffect(new AbilityEffect { Kind = AbilityEffectKind.WeaponAttack, AccuracyBonus = -10 });

        /// <summary>Общий пул способностей, доступных напарникам по гейту скила (передаётся в CombatUnit.FromCompanion).</summary>
        public static List<AbilityDefinition> AbilityCatalog() => new List<AbilityDefinition>
        {
            Lunge(), SetTrap(), MoveOrder(), Volley()
        };

        // ---- Враги (§3.1: авангард орди доби 1, бояри Тугара, фінальний бос) ----

        public static EnemyDefinition HordeScout() =>
            new EnemyDefinition("enemy.horde_scout", "horde_scout", EnemyRole.Skirmisher, EnemyFamily.Human)
            {
                MaxHp = 8, MaxAp = 8, Accuracy = 55, Defense = 0, Initiative = 6, CritChance = 5, Armor = 0,
                Weapon = HordeBow()
            };

        public static EnemyDefinition HordeSkirmisher() =>
            new EnemyDefinition("enemy.horde_skirmisher", "horde_skirmisher", EnemyRole.Skirmisher, EnemyFamily.Human)
            {
                MaxHp = 10, MaxAp = 8, Accuracy = 60, Defense = 0, Initiative = 5, CritChance = 5, Armor = 0,
                Weapon = HordeBow()
            };

        public static EnemyDefinition TuharBoyar() =>
            new EnemyDefinition("enemy.tuhar_boyar", "tuhar_boyar", EnemyRole.Breacher, EnemyFamily.Human)
            {
                MaxHp = 14, MaxAp = 8, Accuracy = 62, Defense = 2, Initiative = 6, CritChance = 8, Armor = 1,
                Weapon = BoyarSaber(),
                Abilities = { Lunge() }
            };

        /// <summary>
        /// Бурунда-бегадир — фінальний бос (R8): тримає удар і б'є боляче, без
        /// роздування HP окремо від ролі.
        ///
        /// Дебаг §6.1 №32 (24.09.2026): Accuracy був 65 — проти реалістичного
        /// Defense напарника/протагоніста з Epic 2 (= Agility 1..10 напряму,
        /// DerivedStats.cs: DefenseBase 0 + Agility×1, стеля 10) показане число
        /// падало в 55..64, а StatusOnHit/ShredOnHit гейтовані ЛИШЕ на Hit/Crit
        /// (CombatState.ExecuteAttackRoll — навмисно, граза = лише половина
        /// урону, без проків). Під ThresholdRule (інваріант 1, за замовчуванням
        /// у GameSession) це не "рідко" — це ПОСТІЙНО: margin 5..14 назавжди
        /// нижче ThresholdGrazeBand=15, і жодного природного Hit не буває, а
        /// Strike-метр (єдиний детермінований вихід на гарантований удар) сам
        /// копиться лише з Hit/Crit (GDD.md:119 "копится с попаданий" — не з
        /// будь-якої атаки, і це навмисно, не правиться тут) — то без Hit
        /// Strike ніколи не набереться, і пастка не відкривається НІКОЛИ для
        /// цього конкретного бою. Accuracy 65 -> 80: margin (chance=Accuracy−
        /// Defense, margin=chance−ThresholdBaseline(50)) проти Defense 0..10
        /// стає 20..30 — цілком у смузі Hit (ThresholdCritBand=35, до Crit тут
        /// не дістає навіть при найкращому Defense=0) — гарантований Hit, не
        /// "інколи Crit", саме "б'є боляче" з коментаря вище, тепер
        /// справджується. Доведено
        /// <see cref="Game.Tests.EditMode.CombatStatusDebugTests"/>.
        /// </summary>
        public static EnemyDefinition Burunda() =>
            new EnemyDefinition("enemy.burunda", "burunda", EnemyRole.Tank, EnemyFamily.Human)
            {
                MaxHp = 30, MaxAp = 10, Accuracy = 80, Defense = 4, Initiative = 7, CritChance = 12, Armor = 2,
                Resolve = 3,
                Weapon = BurundaMace(),
                Abilities = { Lunge() }
            };

        /// <summary>Каталог врагів за id (EnemySpawn.EnemyDefinitionId → EnemyDefinition) — вхід у CombatBattleBuilder.</summary>
        public static Dictionary<string, EnemyDefinition> EnemyCatalog()
        {
            var scout = HordeScout();
            var skirmisher = HordeSkirmisher();
            var boyar = TuharBoyar();
            var burunda = Burunda();
            return new Dictionary<string, EnemyDefinition>
            {
                [scout.Id] = scout,
                [skirmisher.Id] = skirmisher,
                [boyar.Id] = boyar,
                [burunda.Id] = burunda
            };
        }

        // ---- Тренувальний бій (титульне меню, §2 рядок 32) ----

        /// <summary>
        /// Канонічний ростер/вороги, без кампанії — пісочниця для швидкої
        /// оцінки бою (обидва правила попадання, overwatch). Не читає
        /// Companion/Roster взагалі: юніти — «канонічні» профілі напряму,
        /// щоб «Тренувальний бій» працював з титулу, коли жодного напарника
        /// ще не існує (GameSession.NewTrainingBattle, §4.1, D1).
        ///
        /// roller обов'язковий лише для HitRuleKind.Percent — Core сам не
        /// реалізує IDiceRoller (R1, ArchitectureGuardTests.
        /// Core_NoTypeImplementsIDiceRoller), тому справжній кубик
        /// (Gameplay.Combat.SeededDiceRoller) підмішує викликач (D1/UI).
        /// Для ThresholdRule roller не потрібен — бій повністю детермінований.
        /// </summary>
        public static CombatState Training(BalanceConfig cfg = null, HitRuleKind hitRule = HitRuleKind.Threshold,
                                            IDiceRoller roller = null)
        {
            cfg = cfg ?? new BalanceConfig();
            if (hitRule == HitRuleKind.Percent && roller == null)
                throw new System.ArgumentException("PercentRule требует roller — передайте SeededDiceRoller снаружи Core", nameof(roller));

            var map = new GridMap(8, 8);
            map.SetCover(new GridPos(4, 4), Direction.West, CoverType.Half);

            IHitRule rule = hitRule == HitRuleKind.Percent ? new PercentRule(cfg) : (IHitRule)new ThresholdRule(cfg);
            // ThresholdRule не трогает roller вовсе — CombatState допускает null здесь.
            var cs = new CombatState(map, cfg, rule, roller);

            var trainee1 = new CombatUnit("trainee_1", Side.Player, TraineeProfile("Провідник"), HordeSpear());
            var trainee2 = new CombatUnit("trainee_2", Side.Player, TraineeProfile("Максим"), HordeBow());
            cs.AddUnit(trainee1, new GridPos(1, 1));
            cs.AddUnit(trainee2, new GridPos(1, 3));

            // Полірування (ціль 3 «Бойові декорації», owner: "not a single
            // column"): раніше обидва вороги стояли на тому самому x=6 —
            // тепер зсунуті й КОЖЕН несе власне укриття на своєму тайлі
            // (GridMap.CoverAgainst читає укриття із тайла ЗАХИСНИКА, не
            // сусіднього — одна декоративна плитка в центрі нікого не
            // захищала).
            cs.AddUnit(CombatUnit.FromEnemy(HordeScout(), "training_scout_1"), new GridPos(6, 1));
            map.SetCover(new GridPos(6, 1), Direction.West, CoverType.Half);
            cs.AddUnit(CombatUnit.FromEnemy(HordeSkirmisher(), "training_scout_2"), new GridPos(5, 4));
            map.SetCover(new GridPos(5, 4), Direction.West, CoverType.Full);

            cs.Begin();
            return cs;
        }

        private static UnitProfile TraineeProfile(string name) => new UnitProfile
        {
            DisplayName = name,
            MaxHp = 14, MaxAp = 9, Accuracy = 65, Defense = 2, Initiative = 6, CritChance = 8, Armor = 1,
            Resolve = 3, DamageBonus = 0, MoveApPerTile = 1, MedicineSkill = 0, CanBeDowned = true
        };
    }
}
