using System.Collections.Generic;
using Game.Core.Characters.Build;
using Game.Core.Characters.Creation;
using Game.Core.Expeditions;
using Game.Core.Session;
using Game.Core.Session.Views;

namespace Game.Gameplay.UI
{
    /// <summary>
    /// Те, що панелі просять в оболонки, чого немає в самому ядрі: відкрити
    /// іншу панель, повести героя до місця, почати сцену, записати слот на
    /// диск. Реалізує <c>GameShell</c>; тести — підставна реалізація. Моделі
    /// лишаються без рушія (HP-4): жодного типу UnityEngine тут немає.
    /// </summary>
    public interface IUxHost
    {
        GameSession Session { get; }
        Gender Gender { get; }
        UxPanelState PanelState { get; }

        /// <summary>Відкрити панель (UX-04: панель веде, а не дублює дію).</summary>
        void OpenPanel(UxPanelId panel, string context);

        /// <summary>Повести героя до місця (<c>WalkPlace.Id</c>) — «Показати в селі».</summary>
        void WalkTo(string placeId);

        /// <summary>Показати сцену, яку ядро щойно почало (перший кадр не губиться).</summary>
        void BeginScene(SceneStepView first);

        /// <summary>Слоти збереження на диску.</summary>
        IReadOnlyList<SaveSlotView> SaveSlots();

        UxOutcome SaveToSlot(int slot);

        /// <summary>Завантажити слот у СВІЖУ сесію (FreshSessionRestoreTests).</summary>
        UxOutcome LoadSlot(int slot);
    }

    /// <summary>
    /// Стан панелей між кадрами, що жив у полях старого хаба: загін і прогноз
    /// зборів, план розвитку, кеш пропозицій квестів за добу, обрана людина.
    /// </summary>
    public sealed class UxPanelState
    {
        // Збори (Застава)
        public string MusterSite = "outskirts";
        public ExpeditionApproach MusterApproach = ExpeditionApproach.Quiet;
        public readonly List<string> MusterParty = new List<string>();
        public ExpeditionPreviewView MusterPreview;

        /// <summary>
        /// Заступники на пости, що звільняє загін (Поправка №8.3, M1.6): id поста →
        /// id заступника; порожній рядок — гравець явно залишив пост порожнім.
        /// Автопризначення немає — тут лише вибір гравця до «Вирушати».
        /// </summary>
        public readonly Dictionary<string, string> MusterDeputies = new Dictionary<string, string>();

        // Розвиток протагоніста
        public BuildPlan Plan = new BuildPlan();

        public readonly QuestOfferCache Quests = new QuestOfferCache();

        /// <summary>Склад рейду на кожного нашого в полоні (ключ — id бранця), як загін вилазки.</summary>
        public readonly Dictionary<string, List<string>> CaptiveRaidParty = new Dictionary<string, List<string>>();

        /// <summary>Скидає все, що належить партії (нова гра чи завантаження).</summary>
        public void Reset()
        {
            MusterSite = "outskirts";
            MusterApproach = ExpeditionApproach.Quiet;
            MusterParty.Clear();
            MusterDeputies.Clear();
            MusterPreview = null;
            Plan = new BuildPlan();
            Quests.Clear();
            CaptiveRaidParty.Clear();
        }
    }

    /// <summary>
    /// Пропозиція квесту — раз на добу на квест. <c>OfferQuestStage</c> сам
    /// пише «Нова пропозиція» в журнал подій на кожен виклик, тож кликати його
    /// щокадру не можна (стрічка тонула в дублях). Пропонувати можна вранці,
    /// увечері й уночі; у вільній грі — ні (раніше вкладка щокадру отримувала
    /// відмову, і рядок відмови блимав).
    /// </summary>
    public sealed class QuestOfferCache
    {
        private readonly Dictionary<string, QuestOfferView> _offers = new Dictionary<string, QuestOfferView>();
        private readonly Dictionary<string, int> _day = new Dictionary<string, int>();

        public static bool CanOffer(SessionState state) =>
            state == SessionState.Morning || state == SessionState.Evening || state == SessionState.Night;

        /// <summary>Поточна пропозиція квесту або null (немає, не зареєстрований, або зараз не час).</summary>
        public QuestOfferView Get(GameSession session, string questId)
        {
            if (session == null || string.IsNullOrEmpty(questId)) return null;
            int day = session.CurrentView?.Day ?? 0;
            int cachedDay;
            if (_day.TryGetValue(questId, out cachedDay) && cachedDay == day)
            {
                QuestOfferView cached;
                return _offers.TryGetValue(questId, out cached) ? cached : null;
            }
            if (!CanOffer(session.State)) return null;
            QuestOfferView offer = null;
            try { offer = session.OfferQuestStage(questId); }
            catch (System.InvalidOperationException) { offer = null; }
            _offers[questId] = offer;
            _day[questId] = day;
            return offer;
        }

        /// <summary>Після вибору етап міг змінитись — наступний показ спитає ядро знову.</summary>
        public void Invalidate(string questId)
        {
            _offers.Remove(questId);
            _day.Remove(questId);
        }

        public void Clear()
        {
            _offers.Clear();
            _day.Clear();
        }
    }
}
