namespace Game.Core.Settlement
{
    /// <summary>
    /// Минимальный счётчик населения — ровно столько, сколько нужно кризису,
    /// чтобы у оттока людей был адресат. Полная модель (вместимость, прирост,
    /// Досягаемость) появится на Э3.
    ///
    /// ЯКОРЬ: 200 — стартовое село. Число игроку не показывается: наружу уходит
    /// только полоса людности, которая читается по улицам и мудборду.
    /// </summary>
    public sealed class PopulationState
    {
        internal int Count { get; private set; }

        public PopulationState(int start = 200)
        {
            Count = start < 0 ? 0 : start;
        }

        /// <summary>Полоса людности 0..4 — то, что видно глазами.</summary>
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

        internal void Add(int amount)
        {
            if (amount <= 0) return;
            Count += amount;
        }
    }
}
