namespace Game.Core.Session.Views
{
    /// <summary>
    /// Ворог, що здався в бою (Поправка №14.2): його долю гравець вирішує на
    /// панелі результату — відпустити, взяти в полон чи добити. Кого не
    /// вирішили до наступної доби — відпускають.
    /// </summary>
    public sealed class SurrenderView
    {
        public string UnitId;
        public string EnemyDefinitionId;

        /// <summary>Ключ назви ворога (UkrainianText), як у BattleUnitView.DisplayNameKey.</summary>
        public string DisplayNameKey;

        /// <summary>"Grunt" | "MiniBoss".</summary>
        public string Rank;

        /// <summary>Чи можна потім переманити (правило кастингу №12.9).</summary>
        public bool CanRecruitLater;
    }

    /// <summary>
    /// Полонений громади (Поправка №14.2): полоси замість чисел (інваріант 3) —
    /// наскільки готовий перейти і чи неспокійний. Долю вирішує віче.
    /// </summary>
    public sealed class PrisonerView
    {
        public string Id;
        public string EnemyDefinitionId;
        public string DisplayNameKey;
        public string Rank;

        /// <summary>"Hostile" | "Wavering" | "Ready".</summary>
        public string Disposition;

        /// <summary>"Calm" | "Restless".</summary>
        public string Restlessness;

        /// <summary>Переманити можна зараз: готовий і не з російського першоджерела.</summary>
        public bool CanRecruitNow;

        /// <summary>Не переманити ніколи (№12.9) — лише викуп, відпустити.</summary>
        public bool NeverRecruitable;

        /// <summary>Скільки золота дасть викуп — видно до кліку.</summary>
        public int RansomGold;

        /// <summary>Збудована Сторожа стереже полонених (утеча повільніша).</summary>
        public bool Guarded;

        public int DayTaken;
    }
}
