namespace Game.Core.Settlement
{
    /// <summary>
    /// Мінімальний лічильник населення — рівно стільки, скільки потрібно кризі,
    /// щоб у відпливу людей був адресат. Повна модель (місткість, приріст,
    /// досяжність) з'явиться на Е3.
    ///
    /// ЯКІР: 200 — стартове село. Число гравцю не показується: назовні виходить
    /// лише полоса людності, яка читається по вулицях і мудборду.
    /// </summary>
    public sealed class PopulationState
    {
        internal int Count { get; private set; }

        public PopulationState(int start = 200)
        {
            Count = start < 0 ? 0 : start;
        }

        /// <summary>Полоса людності 0..4 — те, що видно очима.</summary>
        public int CrowdBand
        {
            get
            {
                if (Count < 100) return 0;
                if (Count < 200) return 1;
                if (Count < 400) return 2;
                if (Count < 800) return 3;
                return 4;
            }
        }

        internal int Remove(int amount)
        {
            if (amount <= 0) return 0;
            int before = Count;
            Count -= amount;
            if (Count < 0) Count = 0;
            return before - Count;
        }

        internal void RestoreForSave(int count)
        {
            Count = count < 0 ? 0 : count;
        }

        internal void Add(int amount)
        {
            if (amount <= 0) return;
            Count += amount;
        }
    }
}
