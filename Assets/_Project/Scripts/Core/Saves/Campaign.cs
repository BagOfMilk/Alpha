using System;
using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Characters;
using Game.Core.Combat;
using Game.Core.Companions;
using Game.Core.Economy;
using Game.Core.Expeditions;
using Game.Core.Factions;
using Game.Core.Health;
using Game.Core.Threats;

namespace Game.Core.Saves
{
    /// <summary>Итог кампании: game over — только финал и айронмен-смерть протагониста (US-16.2).</summary>
    public enum CampaignOutcome
    {
        Ongoing = 0,
        Won = 1,
        Lost = 2
    }

    /// <summary>
    /// Сессия кампании — то, что сохраняется/загружается (US-16.1), и лёгкий
    /// оркестратор поверх систем: единый ход времени (база + совет как time-sink),
    /// жизненный цикл вылазки (InExpedition для гибридных сейвов), арки напарников,
    /// трофеи перебежчиков и исход кампании. Гибридные сейвы: свободно в базе, под
    /// айронменом в вылазке быстрый сейв заблокирован.
    /// </summary>
    public sealed class Campaign
    {
        public BalanceConfig Cfg { get; }
        public BaseState Base { get; }
        public FactionRegistry Factions { get; }
        public readonly HashSet<string> Flags = new HashSet<string>();

        /// <summary>
        /// Айронмен-тумблер (US-16.1). Единственный источник истины — Cfg.Ironman:
        /// им же живут Expedition (game over) и CombatUnit (смертность протагониста),
        /// иначе переключение/загрузка рассинхронизировали бы сейв-гейт и бой.
        /// </summary>
        public bool Ironman { get => Cfg.Ironman; set => Cfg.Ironman = value; }

        /// <summary>Отряд вне базы (в пути/вылазке) — влияет на доступность быстрого сейва.</summary>
        public bool InExpedition { get; set; }

        /// <summary>Итог кампании: победа в финале / game over (US-16.2, US-11.4).</summary>
        public CampaignOutcome Outcome { get; set; } = CampaignOutcome.Ongoing;

        /// <summary>
        /// Сид кампании: источник ВСЕХ потоков случайностей (угрозы, лут вылазок).
        /// Генерируется в NewGame, хранится в сейве — каждая кампания уникальна,
        /// а загрузка продолжает потоки, не перезапуская их.
        /// </summary>
        public int Seed { get; set; }

        /// <summary>Производный сид подсистемы: один сид кампании → независимые потоки.</summary>
        public static int DeriveSeed(int campaignSeed, int stream)
            => unchecked(campaignSeed * 486187739 + stream * 1000003);

        /// <summary>Совет города (опц.): подключается как time-sink календаря.</summary>
        public Council.Council Council { get; private set; }

        /// <summary>Текущая вылазка (между Launch и Conclude), null — отряд дома.</summary>
        public Expedition ActiveExpedition { get; private set; }

        /// <summary>Личные арки напарников (US-9.5) — персистятся в сейве.</summary>
        public readonly List<CompanionArcRun> Arcs = new List<CompanionArcRun>();

        /// <summary>Снимки ушедших в антагонисты (US-9.4): гир вернётся с босса — персистятся.</summary>
        public readonly List<AntagonistRecord> Antagonists = new List<AntagonistRecord>();

        /// <summary>Взятые ачивки (US-16.1: только в айронмене) — персистятся.</summary>
        public readonly HashSet<string> Achievements = new HashSet<string>();

        /// <summary>Ачивки берутся ТОЛЬКО в айронмене (US-16.1). true — взята впервые.</summary>
        public bool TryUnlockAchievement(string id)
        {
            if (!Ironman || string.IsNullOrEmpty(id)) return false;
            return Achievements.Add(id);
        }

        public Roster Roster => Base.Roster;

        public Campaign(BalanceConfig cfg, BaseState baseState, FactionRegistry factions)
        {
            Cfg = cfg ?? new BalanceConfig();
            Base = baseState;
            Factions = factions;
        }

        /// <summary>Свободный сейв в базе; в вылазке под айронменом — нельзя (US-16.1).</summary>
        public bool CanQuickSave => !(Ironman && InExpedition);

        /// <summary>Подключает совет: его КД/инвестиции тикаются единым ходом времени.</summary>
        public void AttachCouncil(Council.Council council)
        {
            Council = council;
            Base.AttachTimeSink(council);
        }

        /// <summary>Единый ход времени кампании: база + все time-sinks (совет) без рассинхрона.</summary>
        public CycleReport AdvanceDays(int days) => Base.AdvanceDays(days);

        // ---- Жизненный цикл вылазки (оркестрация US-16.1: InExpedition ведётся сам) ----
        /// <summary>Собирает вылазку по плану. Отправка отряда — через TrySend у результата.</summary>
        public Expedition LaunchExpedition(ExpeditionPlan plan,
                                           Func<Companion, Scar> scarPicker = null, IRng lootRng = null)
        {
            if (ActiveExpedition != null && ActiveExpedition.Phase != ExpeditionPhase.Concluded)
                throw new InvalidOperationException("Вылазка уже идёт");
            // Лут — из потока кампании (сид + день): сейв/лоад не рероллит дроп.
            lootRng = lootRng ?? new SeededRng(DeriveSeed(Seed, 200 + Base.CurrentDay));
            ActiveExpedition = new Expedition(Base, plan, Cfg, scarPicker, lootRng);
            return ActiveExpedition;
        }

        /// <summary>Выход в путь: календарь двигается, быстрый сейв гейтится айронменом.</summary>
        public CycleReport DepartExpedition()
        {
            if (ActiveExpedition == null) throw new InvalidOperationException("Вылазка не собрана");
            var report = ActiveExpedition.Depart();
            InExpedition = true;
            return report;
        }

        /// <summary>Возврат вылазки: последствия в ростер; game over айронмена = кампания проиграна.</summary>
        public ExpeditionReport ConcludeExpedition(CombatState combat)
        {
            if (ActiveExpedition == null) throw new InvalidOperationException("Вылазка не собрана");
            var report = ActiveExpedition.Conclude(combat);
            InExpedition = false;
            ActiveExpedition = null;
            if (report.GameOver) Outcome = CampaignOutcome.Lost;
            return report;
        }

        /// <summary>Новая игра: дефолтный стартовый ростер/база/фракции/угрозы; ядро-здания стоят.</summary>
        public static Campaign NewGame(BalanceConfig cfg) => NewGame(cfg, null, null);

        /// <summary>
        /// Новая игра с СОЗДАННЫМ протагонистом (US-2.7, ProtagonistBuilder) и/или
        /// явным сидом. Кастом занимает место дефолтного лидера (id должен быть
        /// "leader" — на него завязан спайн US-14.1). seed == null → уникальный сид
        /// (каждая кампания — своя последовательность инцидентов/лута).
        /// </summary>
        public static Campaign NewGame(BalanceConfig cfg, Companion protagonist, int? seed = null)
        {
            if (protagonist != null && protagonist.Id != "leader")
                throw new ArgumentException(
                    "Кастомный протагонист обязан иметь id \"leader\" — на него завязан спайн " +
                    "(US-14.1: StoryBeat/React) и замещение дефолтного командира.", nameof(protagonist));

            cfg = cfg ?? new BalanceConfig();
            int campaignSeed = seed ?? Guid.NewGuid().GetHashCode();

            var roster = new Roster();
            foreach (var bg in DefaultContent.AllBackgrounds())
            {
                if (protagonist != null && bg.Id == "leader") continue; // место занято кастомом
                roster.Add(bg.CreateInstance(bg.Id, cfg));
            }
            if (protagonist != null)
            {
                protagonist.IsProtagonist = true;
                roster.Add(protagonist);
            }
            var leader = roster.Get("leader");
            if (leader != null) leader.IsProtagonist = true;

            // Перки — производная скилов (US-3.10): считаем частью жизненного цикла,
            // а не заботой UI. Стартовый ростер сразу с открытыми перками.
            foreach (var c in roster.All) c.RefreshPerks(DefaultContent.PerkCatalog());

            var baseState = new BaseState(roster, new ResourceLedger(), cfg);
            foreach (var slot in DefaultContent.AllSlots()) baseState.AddSlot(slot);
            baseState.AttachThreats(new ThreatSystem(cfg, new SeededRng(DeriveSeed(campaignSeed, 1)),
                DefaultContent.IncidentPool(), DefaultContent.TensionSpikes()));

            // Ядро-здания стоят с самого начала (US-7.1): их позиции открыты по умолчанию.
            baseState.MarkBuilt(BaseSectionType.Council);
            baseState.MarkBuilt(BaseSectionType.Infirmary);
            baseState.MarkBuilt(BaseSectionType.Workshop);
            baseState.MarkBuilt(BaseSectionType.Storehouse);

            return new Campaign(cfg, baseState, DefaultFactions.NewRegistry())
            {
                Ironman = cfg.Ironman,
                Seed = campaignSeed
            };
        }
    }
}
