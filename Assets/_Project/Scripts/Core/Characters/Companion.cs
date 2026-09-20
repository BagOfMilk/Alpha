using System;
using Game.Core.Balance;
using Game.Core.Characters.Perks;
using Game.Core.Characters.Scars;
using Game.Core.Characters.Traits;
using Game.Core.Stats;

namespace Game.Core.Characters
{
    public enum CompanionStatus
    {
        Idle = 0,      // в резерве, свободен
        Assigned = 1,  // назначен на позицию базы
        OnMission = 2, // в вылазке (Даж)
        Injured = 3,   // ранен, нужно восстановление
        Resting = 4,   // отдыхает/лечится в лазарете
        Dead = 5       // погиб. НЕОБРАТИМО (US-9.1, US-11.1)
    }

    /// <summary>
    /// Рантайм-экземпляр напарника (GDD Э2).
    ///
    /// Три независимые оси, как в дизайне: атрибуты (почти статичны, растут
    /// только аугментом), скилы (растут за очки уровня) и трейты в слотах.
    /// Шрамы лежат на отдельном вечном треке, перки — на своём: это разные
    /// системы, и правило «один эффект — одна система» держится тем, что у
    /// каждой свой контейнер, а не тем, что про него помнят.
    ///
    /// Числа персонажа никто не читает из этих полей напрямую — только через
    /// Resolve. Иначе трейт или шрам пришлось бы учитывать в каждой формуле
    /// отдельно, и двойной счёт стал бы вопросом внимательности.
    /// </summary>
    public sealed class Companion
    {
        public string Id { get; }
        public CompanionArchetype Archetype { get; }
        public string DisplayName { get; set; }

        public int Level { get; private set; } = 1;
        public int Xp { get; private set; }

        public CompanionStatus Status { get; set; } = CompanionStatus.Idle;

        /// <summary>Id слота базы, на который назначен (null если не назначен).</summary>
        public string AssignedSlotId { get; internal set; }

        /// <summary>Текущее «здоровье восстановления»: 0 = здоров, >0 = лечится.</summary>
        public double InjuryPoints { get; internal set; }

        /// <summary>Атрибуты. Копия архетипных — экземпляр живёт своей жизнью.</summary>
        public AttributeSet Attributes { get; }

        /// <summary>Скилы. Единственное, что растёт за уровни.</summary>
        public SkillSet Skills { get; }

        public TraitSlots Traits { get; }
        public ScarTrack Scars { get; } = new ScarTrack();
        public CompanionPerks Perks { get; } = new CompanionPerks();

        // Порядок провайдеров фиксирован: от него зависит, чей Override победит,
        // а результат обязан быть воспроизводимым.
        private readonly IModifierProvider[] _providers;

        public Companion(string id, CompanionArchetype archetype, BalanceConfig cfg = null)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Archetype = archetype ?? throw new ArgumentNullException(nameof(archetype));
            DisplayName = archetype.DisplayName;

            Attributes = archetype.Attributes.Clone();
            Skills = archetype.Skills.Clone();

            // null означает «дефолты баланса», а не «ноль слотов»: иначе
            // стартовые трейты архетипа молча пропадали бы в тестах и демках.
            Traits = TraitSlots.FromConfig(cfg ?? new BalanceConfig());
            var starting = archetype.StartingTraits;
            if (starting != null)
                for (int i = 0; i < starting.Count; i++) Traits.TryAdd(starting[i]);

            _providers = new IModifierProvider[] { Traits, Scars, Perks };
        }

        /// <summary>
        /// Считает статы заново через единый агрегатор (US-18.2).
        ///
        /// Не кэшируется намеренно: кэш пришлось бы сбрасывать при каждом новом
        /// трейте, шраме, перке и предмете, а незамеченный протухший кэш — это
        /// ровно те расхождения чисел, ради устранения которых агрегатор и
        /// заводился. День считает десятки резолвов, а не десятки тысяч.
        /// </summary>
        public StatSnapshot Resolve(BalanceConfig cfg)
            => StatResolver.Resolve(Attributes, Skills, _providers, cfg);

        /// <summary>Сырой уровень скила, без трейтов и шрамов.</summary>
        public int Skill(SkillType skill) => Skills[skill];

        /// <summary>Сырое значение атрибута, без модификаторов.</summary>
        public int Attribute(AttributeType attribute) => Attributes[attribute];

        public bool IsAssigned => !string.IsNullOrEmpty(AssignedSlotId);
        public bool IsInjured => InjuryPoints > 0.0;

        /// <summary>Погиб. Из этого состояния нет пути назад.</summary>
        public bool IsDead => Status == CompanionStatus.Dead;

        /// <summary>
        /// Убить напарника. Переход необратим — это осознанная жёсткость GDD:
        /// потери должны быть настоящими, иначе привязанность к ростеру ничего
        /// не стоит. Снимает с позиции: мёртвый пост не держит.
        /// </summary>
        internal void MarkDead()
        {
            Status = CompanionStatus.Dead;
            AssignedSlotId = null;
            InjuryPoints = 0;
        }

        /// <summary>
        /// Начисляет опыт и применяет повышения уровня, выдавая очки СКИЛОВ по
        /// ростовому профилю архетипа. Атрибуты уровень не трогает (US-2.1) —
        /// их поднимает только крафт аугмента.
        /// </summary>
        public ProgressionMath.LevelUpResult GainXp(int amount, BalanceConfig cfg)
        {
            var result = ProgressionMath.GrantXp(Level, Xp, amount, cfg);
            if (result.LeveledUp)
            {
                int points = result.LevelsGained * cfg.SkillPointsPerLevel;
                Skills.AddClamped(Archetype.Growth.AllocatePoints(points), cfg);
            }
            Level = result.Level;
            Xp = result.RemainderXp;
            return result;
        }
    }
}
