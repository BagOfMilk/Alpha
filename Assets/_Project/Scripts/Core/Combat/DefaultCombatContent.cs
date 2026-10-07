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

        /// <summary>Сокира сокирника орди (Поправка №11) — б'є боляче й без броні, щоб короткий бій кімнати данжу лишався тяжким без роздування HP.</summary>
        public static WeaponDefinition HordeAxe() => new WeaponDefinition("weapon.horde_axe", "horde_axe", SkillType.Melee)
        {
            Damage = DamageType.Ballistic, DamageMin = 6, DamageMax = 9, CritDamageBonus = 3,
            ApCost = 3, OptimalRange = 1, ShredOnHit = 1
        };

        /// <summary>Тесак лісового розбійника (Поправка №11) — одинак старого скиту б'є за двох.</summary>
        public static WeaponDefinition BanditCleaver() => new WeaponDefinition("weapon.bandit_cleaver", "bandit_cleaver", SkillType.Melee)
        {
            Damage = DamageType.Ballistic, DamageMin = 6, DamageMax = 9, CritDamageBonus = 3,
            ApCost = 4, OptimalRange = 1
        };

        // ---- Зброя загону, яку можна надіти (Поправка №19.2: надіта зброя = зброя в бою) ----
        // Числа — ПЛЕЙСХОЛДЕР у межах уже наявної зброї (спис 3–5 за 3 AP): вибір зброї — це
        // характер бою (швидко/дешево проти важко/пробиває), а не просте «більше шкоди».

        public static WeaponDefinition Dagger() => new WeaponDefinition("weapon.dagger", "dagger", SkillType.Melee)
        { Damage = DamageType.Ballistic, DamageMin = 2, DamageMax = 4, CritDamageBonus = 3, ApCost = 2, OptimalRange = 1 };

        public static WeaponDefinition Sword() => new WeaponDefinition("weapon.sword", "sword", SkillType.Melee)
        { Damage = DamageType.Ballistic, DamageMin = 3, DamageMax = 5, CritDamageBonus = 2, ApCost = 3, OptimalRange = 1 };

        public static WeaponDefinition Sabre() => new WeaponDefinition("weapon.sabre", "sabre", SkillType.Melee)
        { Damage = DamageType.Ballistic, DamageMin = 3, DamageMax = 5, CritDamageBonus = 3, ApCost = 3, OptimalRange = 1 };

        public static WeaponDefinition CurvedBlade() => new WeaponDefinition("weapon.curved_blade", "curved_blade", SkillType.Melee)
        { Damage = DamageType.Ballistic, DamageMin = 4, DamageMax = 6, CritDamageBonus = 3, ApCost = 3, OptimalRange = 1 };

        public static WeaponDefinition Axe() => new WeaponDefinition("weapon.axe", "axe", SkillType.Melee)
        { Damage = DamageType.Ballistic, DamageMin = 4, DamageMax = 6, CritDamageBonus = 2, ApCost = 3, OptimalRange = 1, ShredOnHit = 1 };

        public static WeaponDefinition Mace() => new WeaponDefinition("weapon.mace", "mace", SkillType.Melee)
        { Damage = DamageType.Ballistic, DamageMin = 3, DamageMax = 6, CritDamageBonus = 2, ApCost = 3, OptimalRange = 1, ArmorPierce = 1 };

        public static WeaponDefinition Club() => new WeaponDefinition("weapon.club", "club", SkillType.Melee)
        { Damage = DamageType.Ballistic, DamageMin = 2, DamageMax = 5, CritDamageBonus = 2, ApCost = 3, OptimalRange = 1 };

        public static WeaponDefinition Musket() => new WeaponDefinition("weapon.musket", "musket", SkillType.Ranged)
        { Damage = DamageType.Ballistic, DamageMin = 5, DamageMax = 8, CritDamageBonus = 3, ApCost = 4, OptimalRange = 5, ArmorPierce = 2 };

        /// <summary>Бойова зброя за id предмета (<c>ItemDefinition.CombatWeaponId</c>).</summary>
        public static Dictionary<string, WeaponDefinition> PlayerWeaponCatalog()
        {
            var all = new[] { HordeBow(), HordeSpear(), Dagger(), Sword(), Sabre(), CurvedBlade(), Axe(), Mace(), Club(), Musket() };
            var map = new Dictionary<string, WeaponDefinition>();
            foreach (var w in all) map[w.Id] = w;
            return map;
        }

        // ---- Здібності (спільний пул: і напарники за гейтом скіла, і вороги напряму) ----

        /// <summary>Ривок у ближній контакт — клінч-юніти поза дистанцією.</summary>
        public static AbilityDefinition Lunge() =>
            new AbilityDefinition("ability.lunge", "lunge", SkillType.Melee, 3)
                .Costs(ap: 2, cooldown: 3)
                .Targets(AbilityTarget.Enemy, range: 6, needsLos: true)
                .WithEffect(new AbilityEffect(AbilityEffectKind.LungeToTarget));

        /// <summary>Пастка на тайлі — активка Виживання.</summary>
        public static AbilityDefinition SetTrap() =>
            new AbilityDefinition("ability.set_trap", "set_trap", SkillType.Survival, 4)
                .Costs(ap: 2, cooldown: 4)
                .Targets(AbilityTarget.Tile, range: 3, needsLos: true)
                .WithEffect(new AbilityEffect { Kind = AbilityEffectKind.PlaceTrap, Amount = 3, Damage = DamageType.True });

        /// <summary>Командний ривок: переставити союзника — активка Тактики.</summary>
        public static AbilityDefinition MoveOrder() =>
            new AbilityDefinition("ability.move_order", "move_order", SkillType.Tactics, 4)
                .Costs(ap: 2, cooldown: 3)
                .Targets(AbilityTarget.Ally, range: 8, needsLos: true)
                .WithEffect(new AbilityEffect(AbilityEffectKind.RepositionTarget, amount: 4));

        /// <summary>Черга/два удари поспіль поточною зброєю — посилений залп при впевненому шансі.</summary>
        public static AbilityDefinition Volley() =>
            new AbilityDefinition("ability.volley", "volley", SkillType.Ranged, 5)
                .Costs(ap: 4, cooldown: 2)
                .Targets(AbilityTarget.Enemy, range: 8, needsLos: true)
                .WithEffect(new AbilityEffect { Kind = AbilityEffectKind.WeaponAttack, AccuracyBonus = -10 })
                .WithEffect(new AbilityEffect { Kind = AbilityEffectKind.WeaponAttack, AccuracyBonus = -10 });

        // ---- Перша партія docs/ABILITIES.md (власник, 29.09.2026: «ок»; «Тенета норм») ----
        // Числа — ПЛЕЙСХОЛДЕРИ з карток; поріг видно до кліку (інваріант 8).

        /// <summary>«Підбадьорити» (Переконання ≥ 3): зняти придушення/збиття зі свого, інакше +1 ОД; раз за бій на союзника.</summary>
        public static AbilityDefinition Rally() =>
            new AbilityDefinition("ability.rally", "rally", SkillType.Persuade, 3)
                .Costs(ap: 2, cooldown: 0)
                .Targets(AbilityTarget.Ally, range: 4, needsLos: false) // голос чути з-за укриття
                .WithEffect(new AbilityEffect(AbilityEffectKind.Rally, amount: 1));

        /// <summary>«Розлютити» (Залякування ≥ Воля цілі): ціль наступного ходу б'є лише провокатора.</summary>
        public static AbilityDefinition Enrage() =>
            new AbilityDefinition("ability.enrage", "enrage", SkillType.Intimidate, 1)
                .Costs(ap: 2, cooldown: 2)
                .Targets(AbilityTarget.Enemy, range: 6, needsLos: true)
                .WithEffect(new AbilityEffect(AbilityEffectKind.Enrage));

        /// <summary>«Залякати» (Залякування ≥ Воля цілі + 1): придушення, здасться раніше; звір тікає.</summary>
        public static AbilityDefinition Intimidate() =>
            new AbilityDefinition("ability.intimidate", "intimidate", SkillType.Intimidate, 1)
                .Costs(ap: 2, cooldown: 1)
                .Targets(AbilityTarget.Enemy, range: 6, needsLos: true)
                .WithEffect(new AbilityEffect(AbilityEffectKind.Intimidate));

        /// <summary>Тенета (Виживання ≥ 2): сітка без шкоди — хто ступить, той придушений; дозор у радіусі 2 збито.</summary>
        public static AbilityDefinition Net() =>
            new AbilityDefinition("ability.net", "net", SkillType.Survival, 2)
                .Costs(ap: 2, cooldown: 3)
                .Targets(AbilityTarget.Tile, range: 3, needsLos: true)
                .WithEffect(new AbilityEffect { Kind = AbilityEffectKind.PlaceTrap, Amount = 0, Status = StatusType.Suppressed })
                .WithEffect(new AbilityEffect(AbilityEffectKind.BreakOverwatchAround, amount: 2));

        /// <summary>«Пробити» (Ближній бій ≥ 4): стерто броні ≥ 3 — гарантований удар без броні.</summary>
        public static AbilityDefinition Pierce() =>
            new AbilityDefinition("ability.pierce", "pierce", SkillType.Melee, 4)
                .Costs(ap: 3, cooldown: 2)
                .Targets(AbilityTarget.Enemy, range: 1, needsLos: true)
                .WithEffect(new AbilityEffect(AbilityEffectKind.PierceIfShredded, amount: 3));

        /// <summary>«Милосердя на полі» (Медицина ≥ 1, дзеркало «Стабілізувати»): звалений ворог, що може здатися, — полонений.</summary>
        public static AbilityDefinition Mercy() =>
            new AbilityDefinition("ability.mercy", "mercy", SkillType.Medicine, 1)
                .Costs(ap: 2, cooldown: 0)
                .Targets(AbilityTarget.DownedEnemy, range: 1, needsLos: false)
                .WithEffect(new AbilityEffect(AbilityEffectKind.SpareEnemy));

        /// <summary>Спільний пул здібностей, доступних напарникам за гейтом скіла (передається в CombatUnit.FromCompanion).</summary>
        public static List<AbilityDefinition> AbilityCatalog() => new List<AbilityDefinition>
        {
            Lunge(), SetTrap(), MoveOrder(), Volley(),
            Rally(), Enrage(), Intimidate(), Net(), Pierce(), Mercy()
        };

        // ---- Враги (§3.1: авангард орди доби 1, бояри Тугара, фінальний бос) ----

        public static EnemyDefinition HordeScout() =>
            new EnemyDefinition("enemy.horde_scout", "horde_scout", EnemyRole.Skirmisher, EnemyFamily.Human)
            {
                MaxHp = 8, MaxAp = 8, Accuracy = 55, Defense = 0, Initiative = 6, CritChance = 5, Armor = 0,
                Rank = EnemyRank.Grunt, CanSurrender = true, // №14.2: рядовий розвідник може здатися
                Weapon = HordeBow()
            };

        public static EnemyDefinition HordeSkirmisher() =>
            new EnemyDefinition("enemy.horde_skirmisher", "horde_skirmisher", EnemyRole.Skirmisher, EnemyFamily.Human)
            {
                MaxHp = 10, MaxAp = 8, Accuracy = 60, Defense = 0, Initiative = 5, CritChance = 5, Armor = 0,
                Rank = EnemyRank.Grunt, CanSurrender = true,
                Weapon = HordeBow()
            };

        public static EnemyDefinition TuharBoyar() =>
            new EnemyDefinition("enemy.tuhar_boyar", "tuhar_boyar", EnemyRole.Breacher, EnemyFamily.Human)
            {
                MaxHp = 14, MaxAp = 8, Accuracy = 62, Defense = 2, Initiative = 6, CritChance = 8, Armor = 1,
                Rank = EnemyRank.MiniBoss, CanSurrender = true, // №14.2: міні-боса можна взяти в полон і потім переманити
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
                MaxHp = 20, MaxAp = 10, Accuracy = 80, Defense = 4, Initiative = 7, CritChance = 12, Armor = 1, // HP/броня знижені 01.10.2026 (ПЛЕЙСХОЛДЕР, M1.3): див. FinalePacingTests
                Rank = EnemyRank.Boss, // №14.2: бос не здається ніколи
                Resolve = 3,
                Weapon = BurundaMace(),
                Abilities = { Lunge() }
            };

        /// <summary>
        /// Сокирник орди — прорив покинутого табору (Поправка №11, 25.09.2026 —
        /// дослівно: «обрізання на 5-му раунді - обрізання не треба, це
        /// дизайнерсьеке рішення, щоб данжі були побудовані так що саме бої в
        /// ниж були короткими але тяжкими»): бойова кімната данжу мала бути КОРОТКОЮ ТА
        /// ТЯЖКОЮ, а не роздутою по HP — тому це не «ще один розвідник», а
        /// одна небезпечна ціль з високим уроном і без броні (легко влучити,
        /// боляче отримати), з Ривком (спільний пул здібностей) — зустрічає
        /// відряд ударом ще в перший хід, а не тільки після зближення пішки.
        /// SkirmishPacingTests тримає це число.
        /// </summary>
        public static EnemyDefinition HordeVanguard() =>
            new EnemyDefinition("enemy.horde_vanguard", "horde_vanguard", EnemyRole.Breacher, EnemyFamily.Human)
            {
                MaxHp = 15, MaxAp = 8, Accuracy = 80, Defense = 0, Initiative = 7, CritChance = 8, Armor = 0,
                Rank = EnemyRank.Grunt, // №14.2: сокирник орди не здається («далеко не всі вороги»)
                Weapon = HordeAxe(),
                Abilities = { Lunge() }
            };

        /// <summary>
        /// Одинак старого скиту (Поправка №11): раніше <c>forest_bandit</c>
        /// не мав жодного EnemyDefinition — GameSession.ResolveEnemyById
        /// мовчки повертав null, CombatBattleBuilder мовчки пропускав спавн
        /// (<c>if (def == null) continue;</c>), і кімната «Дозор скиту»
        /// розв'язувалась кровавим шляхом проти НУЛЯ ворогів — перемога за
        /// один раунд без жодного ризику. Один ворог на всю кімнату (за
        /// задумом сайту — «менший данж») тепер несе загрозу пари звичайних
        /// розвідників сам: живучий настільки, щоб пережити перший залп загону
        /// (інакше бій вирішувала ініціатива — ворог гинув, не вдаривши), а
        /// тесак за 4 ОД дає два удари за хід, а не три (інакше одинак сам
        /// вибивав загін). Той самий Ривок з контакту. Числа підібрані
        /// SkirmishPacingTests: 2–3 раунди, перемога, у типовому бою падає боєць.
        ///
        /// Родовий ворог (не іменний персонаж) — Поправка №2 («Першоджерело»)
        /// на нього не поширюється так само, як і на horde_scout/
        /// horde_skirmisher/tuhar_boyar/horde_vanguard: тут ім'я — роль
        /// («лісовий розбійник»), а не переосмислена історична особа з
        /// суспільного надбання.
        /// </summary>
        public static EnemyDefinition ForestBandit() =>
            new EnemyDefinition("enemy.forest_bandit", "forest_bandit", EnemyRole.Breacher, EnemyFamily.Human)
            {
                MaxHp = 26, MaxAp = 9, Accuracy = 82, Defense = 1, Initiative = 6, CritChance = 10, Armor = 0,
                Rank = EnemyRank.MiniBoss, CanSurrender = true, SurrenderAtHpPercent = 10, // №14.2: ватажок скиту — міні-бос; поріг низький — бій лишається тяжким (№11)
                Greed = 3, // «Відкуп» (docs/ABILITIES.md §4.6): розбійник продається; орда — ні
                Weapon = BanditCleaver(),
                Abilities = { Lunge() }
            };

        /// <summary>Каталог врагів за id (EnemySpawn.EnemyDefinitionId → EnemyDefinition) — вхід у CombatBattleBuilder.</summary>
        public static Dictionary<string, EnemyDefinition> EnemyCatalog()
        {
            var scout = HordeScout();
            var skirmisher = HordeSkirmisher();
            var boyar = TuharBoyar();
            var burunda = Burunda();
            var vanguard = HordeVanguard();
            var bandit = ForestBandit();
            return new Dictionary<string, EnemyDefinition>
            {
                [scout.Id] = scout,
                [skirmisher.Id] = skirmisher,
                [boyar.Id] = boyar,
                [burunda.Id] = burunda,
                [vanguard.Id] = vanguard,
                [bandit.Id] = bandit
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
            // ThresholdRule взагалі не чіпає roller — CombatState допускає null тут.
            var cs = new CombatState(map, cfg, rule, roller);

            var trainee1 = new CombatUnit("trainee_1", Side.Player, TraineeProfile("Провідник"), HordeSpear());
            var trainee2 = new CombatUnit("trainee_2", Side.Player, TraineeProfile("Максим"), HordeBow());
            // Бій v2 (власник, 25.09.2026: «всі кнопки працювали»): тренування —
            // місце, де гравець пробує КОЖНУ кнопку бою без наслідків для партії.
            // Раніше в тренувальних бійців не було жодної здібності й медицини,
            // тож кнопки здібностей і «Стабілізувати» тут не з'являлись узагалі.
            // По дві здібності зі спільного пулу — усі чотири разом.
            trainee1.Abilities.Add(Lunge());
            trainee1.Abilities.Add(SetTrap());
            trainee2.Abilities.Add(Volley());
            trainee2.Abilities.Add(MoveOrder());
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
            // Медицина 1 — мінімум для «Стабілізувати» (CombatState.Stabilize).
            Resolve = 3, DamageBonus = 0, MoveApPerTile = 1, MedicineSkill = 1, CanBeDowned = true
        };
    }
}
