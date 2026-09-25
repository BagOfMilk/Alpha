using Game.Core.Balance;
using Game.Core.Characters;
using Game.Core.Economy;
using Game.Core.Loop;
using Game.Core.Pressure;
using Game.Core.Stats;

namespace Game.Core.Base
{
    /// <summary>
    /// Рачительний господар: проста політика «доброї гри» (Поправка №6.5).
    ///
    /// НАВІЩО ВОНА В ЯДРІ. Приймання Поправки №6 звучить так: за доброї гри
    /// кампанія не зобов'язана закінчуватися Розколом. Щоб це було твердженням
    /// тесту, а не надією, «добра гра» має бути записана кодом — один
    /// раз, і однаково для тестів, харнеса і плівки діб у сцені.
    ///
    /// Це НЕ штучний інтелект гравця і не підказка йому. Політика
    /// навмисно проста: будувати по порядку, що по кишені; кликати облаву, коли
    /// на вулицях ропіт; приймати людей, коли їжі із запасом.
    /// </summary>
    public sealed class Steward
    {
        /// <summary>Порядок будівництва: спочатку лікувати, потім кликати людей, потім заспокоювати.</summary>
        public string[] BuildPriority =
        {
            DefaultBuildings.Infirmary,
            DefaultBuildings.Tavern,
            DefaultBuildings.Temple,
            DefaultBuildings.Market,
            DefaultBuildings.Fortifications,
            DefaultBuildings.Workshop
        };

        /// <summary>Скільки їжі тримати про запас, перш ніж годувати нових людей.</summary>
        public int FoodReserve = 40;

        /// <summary>З якої смуги кликати облаву. Раніше — витрачати золото на тишу.</summary>
        public TensionBand RaidFrom = TensionBand.Murmur;

        /// <summary>Один хід господаря між добами. Повертає, що було замовлено.</summary>
        public string Act(CityWorks works, BaseState state, DayProcessor processor, BalanceConfig balance)
        {
            if (works == null || state == null || processor == null || balance == null) return null;

            string did = Staff(state);

            // Одне будівництво за раз по порядку: перше, що по кишені.
            foreach (var id in BuildPriority)
            {
                if (works.Has(id) || works.IsBuilding(id)) continue;
                if (works.Order(id, state) == BuildOrderResult.Started)
                {
                    did = (did == null ? "" : did + " ") + "build:" + id;
                    break;
                }
            }

            if (processor.Tension.Band >= RaidFrom &&
                works.OrderRaid(state, processor.CurrentDay + 1, balance) == CouncilOrderResult.Queued)
                did = (did == null ? "" : did + " ") + "raid";

            if (state.Resources.Get(ResourceType.Food) >= balance.City.SettlersFoodCost + FoodReserve &&
                works.OrderSettlers(state, processor.CurrentDay + 1, balance) == CouncilOrderResult.Queued)
                did = (did == null ? "" : did + " ") + "settlers";

            return did;
        }

        /// <summary>
        /// Ставить вільних людей на відкриті порожні пости.
        ///
        /// НАВІЩО. Будівля відкриває пост, але сама його не займає. Плівка діб
        /// показала ціну цього пробілу: лазарет стояв із четвертої доби, а на
        /// сімнадцяту ніч «Хвора дитина» скінчилася погано — «на посту
        /// нікого не було». Лікар весь цей час сидів без діла.
        ///
        /// Правило просте і детерміноване: пости по порядку, на кожен —
        /// вільний із найвищою профільною навичкою, за рівності — перший по
        /// ростеру. Ставить тільки того, хто в справі хоч щось тямить (навичка
        /// не нижче одиниці): інакше лікар пішов би в поле раніше, ніж відкриється
        /// лазарет. Зайнятих не переставляє: це рішення гравця, а не господаря.
        /// Повертає «staff:&lt;хто&gt;@&lt;пост&gt;» через пробіл або null.
        /// </summary>
        public static string Staff(BaseState state)
        {
            if (state == null) return null;

            // Пост загиблого — вільний пост: спочатку звірка, потім розстановка.
            state.ReleaseFallen();

            string did = null;

            foreach (var slot in state.Slots)
            {
                if (!slot.Unlocked || slot.IsOccupied) continue;

                Companion best = null;
                foreach (var companion in state.Roster.All)
                {
                    // B4-аудит §4.5: дозволений ТІЛЬКИ Idle (allow-list, не «!= Dead») —
                    // Antagonist явно не підходить, як і будь-який інший зайнятий статус.
                    if (companion.IsAssigned || companion.Status != CompanionStatus.Idle) continue;
                    if (SkillFor(companion, slot) < 1) continue;
                    if (best == null || SkillFor(companion, slot) > SkillFor(best, slot))
                        best = companion;
                }

                if (best != null && state.TryAssign(best.Id, slot.Id) == AssignmentResult.Success)
                    did = (did == null ? "" : did + " ") + "staff:" + best.Id + "@" + slot.Id;
            }

            return did;
        }

        private static int SkillFor(Companion companion, AssignmentSlot slot)
        {
            var skill = slot.Definition.PrimarySkill;
            return skill == SkillType.None ? 0 : companion.Skill(skill);
        }
    }
}
