using System;
using Game.Core.Balance;
using Game.Core.Characters.Perks;
using Game.Core.Characters.Scars;
using Game.Core.Characters.Traits;
using Game.Core.Items;
using Game.Core.Stats;

namespace Game.Core.Characters
{
    public enum CompanionStatus
    {
        Idle = 0,      // у резерві, вільний
        Assigned = 1,  // призначений на позицію бази
        OnMission = 2, // у вилазці (данж)
        Injured = 3,   // поранений, потрібне відновлення
        Resting = 4,   // відпочиває/лікується в лазареті
        Dead = 5,      // загинув. НЕЗВОРОТНО (US-9.1, US-11.1)

        // ---- B4 (Social): аудит §4.5 ----
        // Пішов в антагоністи (дефекція, R2/§2 №25). НЕЗВОРОТНО, як і Dead:
        // не допускається ні на пост, ні у відряд, ні в присутні —
        // див. ArchitectureGuardTests.Antagonist_NeverAssignable_NeverDispatchable.
        Antagonist = 6
    }

    /// <summary>
    /// Полоса лояльності напарника (R2, GDD Е9): 0..100 -> 5 полос. Якір —
    /// наскільки напарник вірить у твій шлях і в громаду; споживач —
    /// <see cref="Game.Core.Companions.Defection"/> (низька полоса N днів ->
    /// зрада) і <see cref="Game.Core.Companions.CompanionArc"/> (глави гейтяться
    /// полосою); сигнал — подія зміни полоси на кожну зміну (інваріант 4,
    /// див. <see cref="Companion.ApplyLoyaltyDelta"/>), текстовий ключ
    /// «loyalty.band.&lt;band&gt;» (§7.21).
    /// </summary>
    public enum LoyaltyBand
    {
        Broken = 0,
        Resentful = 1,
        Wary = 2,
        Steady = 3,
        Devoted = 4
    }

    /// <summary>
    /// Результат <see cref="Companion.ApplyLoyaltyDelta"/>: дані для події
    /// D1 (§2 №23: «loyalty.band_changed{companionId,band}»). Сире значення
    /// («Delta») навмисно internal — той самий контур приховування, що в <see
    /// cref="Companion.Loyalty"/> (інваріант 3): за межі Game.Core нічого,
    /// крім полос, не виходить.
    /// </summary>
    public readonly struct LoyaltyChange
    {
        public readonly string CompanionId;
        public readonly LoyaltyBand From;
        public readonly LoyaltyBand To;
        internal readonly int Delta;
        internal readonly string SourceId;

        internal LoyaltyChange(string companionId, int delta, LoyaltyBand from, LoyaltyBand to, string sourceId)
        {
            CompanionId = companionId;
            Delta = delta;
            From = from;
            To = to;
            SourceId = sourceId;
        }

        /// <summary>Немає німого переходу полоси (інваріант 4) — ось що перевіряють на це.</summary>
        public bool BandChanged => From != To;
    }

    /// <summary>
    /// Рантайм-екземпляр напарника (GDD Е2).
    ///
    /// Три незалежні осі, як у дизайні: атрибути (майже статичні, ростуть
    /// лише аугментом), скіли (ростуть за очки рівня) і трейти в слотах.
    /// Шрами лежать на окремому вічному треку, перки — на своєму: це різні
    /// системи, і правило «один ефект — одна система» тримається тим, що в
    /// кожної свій контейнер, а не тим, що про нього пам'ятають.
    ///
    /// Числа персонажа ніхто не читає з цих полів напряму — тільки через
    /// Resolve. Інакше трейт чи шрам довелося б враховувати в кожній формулі
    /// окремо, і подвійний рахунок став би питанням уважності.
    /// </summary>
    public sealed class Companion
    {
        public string Id { get; }
        public CompanionArchetype Archetype { get; }
        public string DisplayName { get; set; }

        public int Level { get; private set; } = 1;
        public int Xp { get; private set; }

        public CompanionStatus Status { get; set; } = CompanionStatus.Idle;

        /// <summary>Id слота бази, на який призначений (null, якщо не призначений).</summary>
        public string AssignedSlotId { get; internal set; }

        /// <summary>Поточне «здоров'я відновлення»: 0 = здоровий, >0 = лікується.</summary>
        public double InjuryPoints { get; internal set; }

        /// <summary>Атрибути. Копія архетипних — екземпляр живе своїм життям.</summary>
        public AttributeSet Attributes { get; }

        /// <summary>Скіли. Єдине, що росте за рівні.</summary>
        public SkillSet Skills { get; }

        /// <summary>
        /// Картка (Поправка №5.6 п. 1): ім'я, звідки взятий, що пам'ятає. У
        /// безіменних її немає — і за задумом бути не повинно: безіменних
        /// мобів у грі немає взагалі (Поправка №5.2).
        /// </summary>
        public CharacterCard Card { get; set; }

        public TraitSlots Traits { get; }
        public ScarTrack Scars { get; } = new ScarTrack();
        public CompanionPerks Perks { get; } = new CompanionPerks();

        // ---- B3 (Items/Equipment/Craft): гір як провайдер агрегатора (US-6.2) ----
        // Надіте спорядження крутить числа так само, як трейти/шрами/перки —
        // окремого «бойового» шляху для гіра немає (правило «один ефект — одна система»).
        public Equipment Equipment { get; } = new Equipment();

        // Порядок провайдерів фіксований: від нього залежить, чий Override переможе,
        // а результат зобов'язаний бути відтворюваним.
        private readonly IModifierProvider[] _providers;

        public Companion(string id, CompanionArchetype archetype, BalanceConfig cfg = null)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Archetype = archetype ?? throw new ArgumentNullException(nameof(archetype));
            DisplayName = archetype.DisplayName;

            Attributes = archetype.Attributes.Clone();
            Skills = archetype.Skills.Clone();

            // null означає «дефолти балансу», а не «нуль слотів»: інакше
            // стартові трейти архетипу мовчки зникали б у тестах і демках.
            Traits = TraitSlots.FromConfig(cfg ?? new BalanceConfig());
            var starting = archetype.StartingTraits;
            if (starting != null)
                for (int i = 0; i < starting.Count; i++) Traits.TryAdd(starting[i]);

            _providers = new IModifierProvider[] { Traits, Scars, Perks, Equipment };
        }

        /// <summary>
        /// Рахує стати заново через єдиний агрегатор (US-18.2).
        ///
        /// Не кешується навмисно: кеш довелося б скидати при кожному новому
        /// трейті, шрамі, перку і предметі, а непомічений протухлий кеш — це
        /// рівно ті розбіжності чисел, заради усунення яких агрегатор і
        /// заводився. Доба рахує десятки резолвів, а не десятки тисяч.
        /// </summary>
        public StatSnapshot Resolve(BalanceConfig cfg)
            => StatResolver.Resolve(Attributes, Skills, _providers, cfg);

        /// <summary>Сире значення скіла, без трейтів і шрамів.</summary>
        public int Skill(SkillType skill) => Skills[skill];

        /// <summary>Сире значення атрибута, без модифікаторів.</summary>
        public int Attribute(AttributeType attribute) => Attributes[attribute];

        /// <summary>
        /// Повернути пост зі зліпка. Окремий метод, а не публічний сеттер:
        /// призначенням, як і раніше, розпоряджається тільки BaseState, а це —
        /// відновлення вже прийнятого рішення.
        /// </summary>
        internal void RestoreAssignmentForSave(string slotId) => AssignedSlotId = slotId;

        /// <summary>
        /// Повернути рівень і досвід зі зліпка (Foundation/A1, фрагмент eco=).
        /// Окремий метод, а не GainXp: GainXp перераховує прогресію з нуля
        /// і витратив би очки скілів повторно — тут же просто ставиться вже
        /// прожите число, без побічних ефектів.
        /// </summary>
        internal void RestoreProgressForSave(int level, int xp)
        {
            Level = level < 1 ? 1 : level;
            Xp = xp < 0 ? 0 : xp;
        }

        public bool IsAssigned => !string.IsNullOrEmpty(AssignedSlotId);
        public bool IsInjured => InjuryPoints > 0.0;

        /// <summary>Загинув. З цього стану немає шляху назад.</summary>
        public bool IsDead => Status == CompanionStatus.Dead;

        /// <summary>
        /// Убити напарника. Перехід незворотний — це усвідомлена жорсткість GDD:
        /// втрати повинні бути справжніми, інакше прив'язаність до ростера нічого
        /// не варта. Знімає з позиції: мертвий пост не тримає.
        /// </summary>
        internal void MarkDead()
        {
            Status = CompanionStatus.Dead;
            AssignedSlotId = null;
            InjuryPoints = 0;
        }

        /// <summary>
        /// Нараховує досвід і застосовує підвищення рівня, видаючи очки СКІЛІВ за
        /// ростовим профілем архетипу. Атрибути рівень не чіпає (US-2.1) —
        /// їх піднімає тільки крафт аугмента.
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

        // ---- B7: банк очок протагоніста (R11) ----
        //
        // Той самий приріст рівня, що і в GainXp, але БЕЗ авто-витрати очок
        // скілів: вони лишаються на руках викликача (SpendablePoints), який
        // сам вирішує, куди їх покласти. Єдиний споживач — протагоніст;
        // напарники, як і раніше, ідуть через GainXp і витрачають очки одразу
        // (US-2.1 для них не змінюється).

        /// <summary>
        /// Нараховує досвід і рахує підвищення рівня, НЕ витрачаючи очки скілів
        /// автоматично — на відміну від <see cref="GainXp"/>. Повертає,
        /// скільки рівнів отримано, щоб викликач (BaseState/GameSession)
        /// поклав відповідні очки в <c>SpendablePoints</c>.
        /// </summary>
        public ProgressionMath.LevelUpResult GainXpNoAutoSpend(int amount, BalanceConfig cfg)
        {
            var result = ProgressionMath.GrantXp(Level, Xp, amount, cfg);
            Level = result.Level;
            Xp = result.RemainderXp;
            return result;
        }

        // ==================================================================
        // B4 (Social): Лояльність (R2) і аудит CompanionStatus.Antagonist.
        // Єдиний суцільний блок пакета B4 у спільному файлі (§5.1 — B3
        // додає Equipment, B7 — GainXpNoAutoSpend; сюди не заходять).
        // ==================================================================

        /// <summary>
        /// Пороги полос читаються з плейсхолдер-дефолту секції балансу, а не з
        /// того, що передали в конструктор: конструктор — спільний код, який
        /// пакету B4 чіпати не можна (див. правило "тільки свій блок" §5.1).
        /// Щойно SO-обгортка балансу (R14, відкладено) стане читаною на
        /// інстансі, сюди зайде перенесення на реальний BalanceConfig.CompanionSocial.
        /// </summary>
        private static readonly CompanionSocialBalance DefaultSocialBalance = new CompanionSocialBalance();

        /// <summary>
        /// Сире значення лояльності 0..100. Internal — як і Напруга
        /// (інваріант 3): Game.Gameplay фізично не бачить число, тільки
        /// <see cref="LoyaltyBand"/>. Перевіряється рефлексією в
        /// ArchitectureGuardTests (лінт "не читається з Game.Gameplay").
        /// </summary>
        internal int Loyalty { get; private set; } = 50;

        /// <summary>Полоса — єдине, що видно назовні (інваріант 3/6).</summary>
        public LoyaltyBand LoyaltyBand => DefaultSocialBalance.BandFor(Loyalty);

        /// <summary>
        /// Змінює сире значення (клемп 0..100) і повертає дані для сигналу
        /// (інваріант 4: зміна полоси зобов'язана дати подію — <see
        /// cref="LoyaltyChange.BandChanged"/> відповідає на це; викликач,
        /// а не Companion, вирішує, у що перетворювати зміну полоси далі —
        /// GameEvent збере D1). Внутрішній метод: лояльність рухають тільки
        /// системи Core/Companions (LoyaltyRules/RosterDrama/Defection).
        /// </summary>
        internal LoyaltyChange ApplyLoyaltyDelta(int delta, string sourceId = null)
        {
            var from = LoyaltyBand;
            int next = Loyalty + delta;
            Loyalty = next < 0 ? 0 : (next > 100 ? 100 : next);
            var to = LoyaltyBand;
            return new LoyaltyChange(Id, delta, from, to, sourceId);
        }

        /// <summary>
        /// Зліпок господарства (Foundation/A1, фрагмент roster=) додає
        /// Лояльність як значення із сейву (аудит §4.8 R13) без побічних
        /// ефектів "дельти" — як і <see cref="RestoreProgressForSave"/>.
        /// </summary>
        internal void RestoreLoyaltyForSave(int loyalty)
        {
            Loyalty = loyalty < 0 ? 0 : (loyalty > 100 ? 100 : loyalty);
        }

        /// <summary>
        /// Незворотний перехід в антагоністи (R2/§2 №25). Знімає з поста, якщо
        /// був призначений — викликач (<see cref="Game.Core.Companions.Defection"/>)
        /// робить це до виклику, тут лише фіксується статус, дзеркалом
        /// <see cref="MarkDead"/>.
        /// </summary>
        internal void MarkAntagonist()
        {
            Status = CompanionStatus.Antagonist;
            AssignedSlotId = null;
        }
    }
}
