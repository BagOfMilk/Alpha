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

        /// <summary>
        /// День, когда сюжет открыл финал (0 — ещё не открыт). От него считается,
        /// сколько орда успела набрать, пока город тянул (US-11.4).
        /// </summary>
        public int FinaleReadyDay { get; set; }

        /// <summary>Сколько дней прошло с открытия финала (0, если веха ещё не стоит).</summary>
        public int DaysSinceFinaleReady()
            => FinaleReadyDay <= 0 ? 0 : System.Math.Max(0, Base.CurrentDay - FinaleReadyDay);

        /// <summary>Производный сид подсистемы: один сид кампании → независимые потоки.</summary>
        public static int DeriveSeed(int campaignSeed, int stream)
            => unchecked(campaignSeed * 486187739 + stream * 1000003);

        /// <summary>Совет города (опц.): подключается как time-sink календаря.</summary>
        public Council.Council Council { get; private set; }

        /// <summary>Журнал квестов — доска города (US-14.3); статусы персистятся в сейве.</summary>
        public Quests.QuestLog Quests { get; } = new Quests.QuestLog();

        /// <summary>Текущая вылазка (между Launch и Conclude), null — отряд дома.</summary>
        public Expedition ActiveExpedition { get; private set; }

        /// <summary>
        /// Идущий прогон квеста/главы арки (US-14.3/9.5). Владелец — кампания, а не
        /// экран: прогон обязан переживать и смену сцены, и сейв. Пока он жил только
        /// в статике UI, загрузка начинала квест заново, а уже применённые
        /// последствия выбора (репутация/Напряжение/лояльность) оставались — их
        /// можно было фармить перезагрузкой.
        /// </summary>
        public Quests.QuestRun ActiveQuest { get; private set; }

        /// <summary>Id арки, чья глава идёт сейчас (null — обычный квест доски).</summary>
        public string ActiveArcId { get; private set; }

        /// <summary>Взять прогон в работу (null — закрыть текущий).</summary>
        public void SetActiveQuest(Quests.QuestRun run, string arcId = null)
        {
            ActiveQuest = run;
            ActiveArcId = run != null ? arcId : null;
        }

        /// <summary>Личные арки напарников (US-9.5) — персистятся в сейве.</summary>
        public readonly List<CompanionArcRun> Arcs = new List<CompanionArcRun>();

        /// <summary>Снимки ушедших в антагонисты (US-9.4): гир вернётся с босса — персистятся.</summary>
        public readonly List<AntagonistRecord> Antagonists = new List<AntagonistRecord>();

        /// <summary>Взятые ачивки (US-16.1: только в айронмене) — персистятся.</summary>
        public readonly HashSet<string> Achievements = new HashSet<string>();

        /// <summary>
        /// Драма ростера (US-9.6): рябь лояльности от смертей и предательств.
        /// Связи считаются на лету из тегов ценностей — состояния не хранит.
        /// </summary>
        public Companions.RosterDrama Drama =>
            _drama ?? (_drama = new Companions.RosterDrama(
                new Companions.RosterBonds(Companions.DefaultValues.System()), Cfg));
        private Companions.RosterDrama _drama;

        /// <summary>
        /// Смерть напарника отзывается в ростере (US-9.6): соратники скорбят,
        /// соперники — нет. Зовётся ВСЕМИ путями гибели, иначе потеря никого не
        /// трогает и «дорог ли ростер» проверить нечем.
        /// </summary>
        public Companions.RippleReport NotifyDeath(string companionId)
            => Drama.OnDeath(Roster, companionId);

        /// <summary>
        /// Ход времени опрашивает ростер на уход (US-9.2): боец на дне лояльности
        /// уходит к врагу со своим гиром и становится боссом. Возвращает запись
        /// антагониста (или null) — рябь предательства уже применена.
        /// </summary>
        public Companions.AntagonistRecord TryDefectOnTimePass()
        {
            foreach (var c in Roster.All)
            {
                if (!Companions.DefectionSystem.ShouldDefect(c)) continue;
                var record = Companions.DefectionSystem.Defect(c, Base);
                Antagonists.Add(record);
                OpenRevengeBoard();
                Drama.OnBetrayal(Roster, c.Id);
                return record;
            }
            return null;
        }

        /// <summary>
        /// Кто выйдет боссом расплаты сейчас (US-9.4) — первый в очереди ушедших,
        /// null, если счёт закрыт. Именно этот id обязан прийти в ResolveBossRevenge:
        /// пейоф адресный, а не «снять первого».
        /// </summary>
        public Companions.AntagonistRecord NextRevengeTarget()
            => Antagonists.Count > 0 ? Antagonists[0] : null;

        /// <summary>
        /// Пейоф расплаты с КОНКРЕТНЫМ перебежчиком (US-9.4): гир возвращается в
        /// сташ, сам он выбывает окончательно. Идемпотентно по-настоящему: повторный
        /// вызов с тем же id не находит записи и возвращает false — раньше метод
        /// снимал «первого в очереди», и путь победы, зовущий его дважды (бой +
        /// закрытие квеста), убивал второго перебежчика без боя.
        /// Живёт в Core, чтобы применяться ДО автосейва, закрывающего квест: иначе
        /// выход из игры на панели итога терял единственную награду навсегда.
        /// Если перебежчики ещё остались — расплата открывается заново.
        /// </summary>
        public bool ResolveBossRevenge(string companionId)
        {
            if (string.IsNullOrEmpty(companionId)) return false;
            int index = Antagonists.FindIndex(r => r != null && r.CompanionId == companionId);
            if (index < 0) return false;

            var record = Antagonists[index];
            var traitor = Roster.Get(record.CompanionId);
            // Гир возвращается РОВНО ОДИН раз: снимок в записи и надетое на
            // перебежчике — одни и те же экземпляры (Defect их не снимал).
            if (Base.Inventory.RecoverGearFrom(traitor) == 0)
                Companions.DefectionSystem.ReturnGearOnKill(record, Base.Inventory); // бойца нет — берём снимок
            if (traitor != null && traitor.IsAlive) traitor.Kill();
            Antagonists.RemoveAt(index);

            if (Antagonists.Count > 0) OpenRevengeBoard(); // счёт не закрыт — дело возвращается на доску
            return true;
        }

        /// <summary>
        /// Расплата ПРОИГРАНА (US-9.4): перебежчик ушёл окончательно вместе с гиром —
        /// это и есть цена выбора, второй попытки по нему нет. Запись снимается,
        /// иначе он остаётся головой очереди, и доска, переоткрытая под СЛЕДУЮЩЕГО
        /// перебежчика, вывела бы боссом снова его — с повторной наградой за бой,
        /// который уже проигран.
        /// </summary>
        public bool EscapeBossRevenge(string companionId)
        {
            if (string.IsNullOrEmpty(companionId)) return false;
            int index = Antagonists.FindIndex(r => r != null && r.CompanionId == companionId);
            if (index < 0) return false;
            Antagonists.RemoveAt(index); // гир НЕ возвращается: он ушёл с ним
            return true;
        }

        /// <summary>
        /// Возвращает расплату на доску: снимает «дело закрыто» и переоткрывает
        /// квест, если он уже пройден. Зовётся при КАЖДОМ новом перебежчике —
        /// иначе второй уход после закрытой расплаты не открывал боя вообще, и его
        /// гир пропадал навсегда (единственный путь возврата — босс-бой).
        /// Открывается только когда есть КОГО выводить боссом: иначе снятый
        /// «дело закрыто» переигрывал бы уже отыгранную расплату.
        /// </summary>
        private void OpenRevengeBoard()
        {
            if (NextRevengeTarget() == null) return;
            Flags.Add(Story.Prologue.BossSeededFlag);
            Flags.Remove(Game.Core.Quests.DefaultQuests.BossDefeatedFlag);
            Quests.Reopen("boss_revenge"); // no-op, если квест ещё не видели или он уже на доске
        }

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

        /// <summary>
        /// Единый ход времени кампании: база + все time-sinks (совет) без рассинхрона.
        /// Здесь же проверяется рост города (US-7.6): условия тира зависят от
        /// населения/зданий/репутации, а двигаются они именно ходом времени — без
        /// этого вызова тир навсегда оставался 1, и половина карты была заперта.
        /// </summary>
        public CycleReport AdvanceDays(int days)
        {
            MarkFinaleReadyDay(); // якорь ставим ДО продвижения: дни клока не «съедаются»
            var report = Base.AdvanceDays(days);
            int before = Base.CityTier;
            int grown = GrowCity();
            if (grown > 0) { report.CityTierAdvancedFrom = before; report.CityTierAdvancedTo = grown; }
            return report;
        }

        /// <summary>
        /// Единственный владелец инварианта «город растёт, когда условия сошлись»:
        /// зовётся ВСЕМИ путями хода времени (ожидание, выход и возвращение вылазки),
        /// иначе рост зависел бы от того, какой кнопкой игрок двигал календарь.
        /// Возвращает новый тир или 0, если не рос.
        /// </summary>
        private int GrowCity()
        {
            int grown = 0;
            while (Base.TryAdvanceCityTier(Factions)) grown = Base.CityTier;
            return grown;
        }

        /// <summary>Фиксирует день появления вехи финала — точку отсчёта сбора орды.</summary>
        private void MarkFinaleReadyDay()
        {
            if (FinaleReadyDay <= 0 && Story.FinalBattle.IsUnlocked(Flags))
                FinaleReadyDay = Base.CurrentDay;
        }

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

        /// <summary>
        /// Отмена несостоявшегося сбора (например, TrySend вернул ошибку): допустима только
        /// пока отряд пуст — после успешного TrySend напарники уже сняты с позиций.
        /// </summary>
        public void CancelExpedition()
        {
            if (ActiveExpedition == null) return;
            if (ActiveExpedition.Phase != ExpeditionPhase.Mustering || ActiveExpedition.SquadIds.Count > 0)
                throw new InvalidOperationException("Отменить можно только несобранную вылазку");
            ActiveExpedition = null;
        }

        /// <summary>Выход в путь: календарь двигается, быстрый сейв гейтится айронменом.</summary>
        public CycleReport DepartExpedition()
        {
            if (ActiveExpedition == null) throw new InvalidOperationException("Вылазка не собрана");
            MarkFinaleReadyDay(); // вылазки двигают календарь так же, как «ждать день»
            var report = ActiveExpedition.Depart();
            int before = Base.CityTier;
            int grown = GrowCity(); // дни марша — тоже ход времени
            if (grown > 0) { report.CityTierAdvancedFrom = before; report.CityTierAdvancedTo = grown; }
            InExpedition = true;
            return report;
        }

        /// <summary>Возврат вылазки: последствия в ростер; game over айронмена = кампания проиграна.</summary>
        public ExpeditionReport ConcludeExpedition(CombatState combat)
        {
            if (ActiveExpedition == null) throw new InvalidOperationException("Вылазка не собрана");
            MarkFinaleReadyDay(); // веха могла встать квестом — клок стартует сразу
            var report = ActiveExpedition.Conclude(combat);
            InExpedition = false;
            ActiveExpedition = null;
            // Дни дороги домой — тоже ход времени: город мог дорасти до нового тира.
            // Факт роста доезжает до отчёта возвращения, иначе новая точка на карте
            // появлялась бы без всякого объяснения.
            int before = Base.CityTier;
            int grown = GrowCity();
            if (grown > 0) { report.CityTierAdvancedFrom = before; report.CityTierAdvancedTo = grown; }

            // Рябь от ВСЕХ потерь этой вылазки применяется ЗДЕСЬ (US-9.6): и от
            // погибших в отряде, и от тех, кого кризис убил дома, пока отряд
            // возвращался. Раньше это делал экран отчёта — то есть ПОСЛЕ автосейва
            // (выход из игры на отчёте терял рябь целиком), а смерти дома не
            // покрывались вовсе: они едут отдельным списком IncidentsWhileAway.
            // Списки ПЕРЕСЕКАЮТСЯ: переживший бой боец к моменту дороги домой уже
            // не InSquad, поэтому кризис вправе выбрать жертвой ЕГО — и тогда он
            // лежит и в IncidentsWhileAway.CrisisVictimId, и в Companions
            // (Died/DiedOnReturn). AdjustLoyalty не идемпотентен — оплакиваем
            // каждого ровно один раз.
            var mourned = new HashSet<string>();
            foreach (var oc in report.Companions)
                if (oc.Died && mourned.Add(oc.CompanionId))
                    report.DeathRipples.AddRange(NotifyDeath(oc.CompanionId).Effects);
            foreach (var incident in report.IncidentsWhileAway)
                if (incident.CrisisVictimId != null && mourned.Add(incident.CrisisVictimId))
                    report.DeathRipples.AddRange(NotifyDeath(incident.CrisisVictimId).Effects);

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

            var campaign = new Campaign(cfg, baseState, DefaultFactions.NewRegistry())
            {
                Ironman = cfg.Ironman,
                Seed = campaignSeed
            };
            campaign.SeedArcs(ContentCatalog.Default());
            return campaign;
        }

        /// <summary>
        /// Заводит личные арки напарников (US-9.5) по каталогу. Без этого шага
        /// Campaign.Arcs всегда оставался пустым — весь контент арок был мёртв,
        /// а полоса лояльности не имела ни одного долгосрочного пейофа.
        /// </summary>
        public void SeedArcs(ContentCatalog catalog)
        {
            if (catalog == null) return;
            foreach (var arc in catalog.Arcs)
            {
                if (arc == null) continue;
                bool already = false;
                foreach (var existing in Arcs)
                    if (existing.Arc.Id == arc.Id) { already = true; break; }
                if (already) continue;

                var run = new CompanionArcRun(arc, Flags);
                run.Refresh(Roster.Get(arc.CompanionId));
                Arcs.Add(run);
            }
        }
    }
}
