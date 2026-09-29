using System;
using System.Collections.Generic;
using Game.Core.Characters;
using Game.Core.Session.Views;

namespace Game.Core.Session.Bots
{
    /// <summary>
    /// Спільна, не-політикозалежна механіка ботів (§4.10 TEST_BUILD.md): усе, що
    /// однаково потрібне кільком політикам і не несе власного "характеру" —
    /// розстановка за замовчуванням, синтетичні PendingOfferView/QuestOfferView
    /// для точок рішення, які GameSession НЕ супроводжує природним офером
    /// (криза/фінал/бойова кімната данжу — команди <c>ReactToCrisis</c>/
    /// <c>ResolveFinale</c>/<c>ResolveDungeonRoom</c> приймають шлях напряму,
    /// без проміжного <c>GetPendingOffer()</c>), і геометрія бою для
    /// покрокового керування (§4.2.1 <see cref="BattleView"/> не позначає, хто
    /// саме зараз ходить).
    /// </summary>
    public static class BotSupport
    {
        /// <summary>Троє, кого жодна політика не саджає на пост — лишаються "в полі" для вилазки/данжу (§3.0 FIRST_HOUR).</summary>
        public static bool IsReservedForField(string companionId)
        {
            return string.Equals(companionId, GameSession.ProtagonistId, StringComparison.Ordinal)
                   || string.Equals(companionId, "maksym", StringComparison.Ordinal)
                   || string.Equals(companionId, "myroslava", StringComparison.Ordinal);
        }

        /// <summary>
        /// Розстановка за замовчуванням: перший вільний (Idle, без поста)
        /// напарник — на перший порожній пост зі списку
        /// <see cref="FirstHourWorld.Positions"/>. Без вибору за навичкою: View-шар
        /// (§4.2 CompanionSummary) свідомо не несе скілів назовні (R17-суміжне
        /// рішення показу) — політика "найкращий скіл на пост", як у
        /// <c>Steward.Staff</c>, тут неможлива без читання internal-стану, якого
        /// бот, за умовою пакету ("лише публічні команди"), не має.
        ///
        /// НЕ виключає протагоніста/Максима/Мирославу (§3.0 — "у полі"): і
        /// <c>Steward.Staff</c>, і сам ігровий контракт не забороняють ставити
        /// їх на пост — <c>ExpeditionParty.Depart</c> сам знімає з поста, кого
        /// відправляє (seamsForD1: "unassign+away+vacated+save"), тож конфлікту
        /// з <see cref="MaybeDepartExpedition"/> немає. Без цього рядок §6.1 №2
        /// ("assign.made") ніколи не спрацював би: троє "польових" — єдині
        /// Idle-напарники без поста на добу 1 (на пост першої будівлі, обраної
        /// після прологу, стає її іменний — Захар, Овсій або Гафія, Поправка
        /// №12.7; жоден генератор населення не заводить НОВИХ іменних
        /// Companion — R1/Поправка №5, каст фіксований).
        ///
        /// Поправка №12.7 (старт без будівель): з <paramref name="city"/>
        /// закриті пости (їхня будівля ще не стоїть) пропускаються — інакше
        /// бот «ставив» вільних на склад/ринок без будівлі, наказ відмовляв
        /// (SlotLocked), і ці люди не діставались відкритим постам (фермам).
        /// </summary>
        public static IReadOnlyDictionary<string, string> DefaultAssignments(RosterView roster, CityView city = null)
        {
            var result = new Dictionary<string, string>();
            if (roster?.Companions == null) return result;

            var occupied = new HashSet<string>();
            foreach (var c in roster.Companions)
                if (!string.IsNullOrEmpty(c.AssignedSlotId)) occupied.Add(c.AssignedSlotId);

            var open = city?.OpenPosts != null ? new HashSet<string>(city.OpenPosts) : null;

            foreach (var slotId in FirstHourWorld.Positions)
            {
                if (occupied.Contains(slotId)) continue;
                if (open != null && !open.Contains(slotId)) continue;

                foreach (var c in roster.Companions)
                {
                    if (c.Status != CompanionStatus.Idle) continue;
                    if (!string.IsNullOrEmpty(c.AssignedSlotId)) continue;
                    if (result.ContainsKey(c.Id)) continue;
                    // Поправка №12.7: Захар, Дід Овсій і Гафія чекають СВОГО
                    // поста (раду/склад/лазарет) — він відкриється будівлею, а
                    // не займають чужий. Інакше старт без будівель розкидав би
                    // фахівців по фермах, і збудований склад отримував би
                    // випадкового, а не комірника.
                    string ownPost = OwnPostOf(c.Id);
                    if (ownPost != null && ownPost != slotId) continue;

                    result[c.Id] = slotId;
                    occupied.Add(slotId);
                    break;
                }
            }

            return result;
        }

        /// <summary>
        /// Розстановка журнального гравця (<c>MechanicsJournalCompletionTests</c>
        /// і Unity-тур <c>-autoplay-journal</c> — ОДНА функція, щоб обидва
        /// ходили тими самими командами, Статут UI-14). Це навмисно недбалий
        /// господар: ферми лишає порожніми (голод штовхає Напругу до зміни
        /// смуги й природного бунту — інакше «добра економіка» з облавами
        /// тримає Спокій усі 40 діб), а протагоніста, коли він удома, ставить
        /// на розвідпост (постовий досвід — підвищення рівня).
        ///
        /// До Поправки №12.7 так виходило саме собою: склад, лазарет і рада
        /// стояли від старту зайнятими, і за замовчуванням протагоніст
        /// потрапляв на розвідпост, а ферми — нікому. Зі стартом без будівель
        /// розстановка за замовчуванням нагодувала громаду — і три записи
        /// журналу стали недосяжні. Поведінку названо явно.
        ///
        /// <paramref name="starveFarms"/> — ферми порожні, доки журнал не
        /// побачив зміну смуги Напруги і природний бунт; потім господар
        /// годує громаду, і вона може вирости до нового тіра (запис
        /// population_tier). Рішення, коли перестати морити, приймає водій
        /// журнального туру з <c>GetMechanicsJournal</c>.
        /// </summary>
        public static IReadOnlyDictionary<string, string> JournalAssignments(RosterView roster, CityView city, bool starveFarms = true)
        {
            var result = new Dictionary<string, string>();
            if (roster?.Companions == null) return result;

            bool scoutingFree = true;
            foreach (var c in roster.Companions)
                if (c.AssignedSlotId == "scouting_post") scoutingFree = false;
            bool scoutingOpen = city?.OpenPosts == null || System.Linq.Enumerable.Contains(city.OpenPosts, "scouting_post");

            string protagonistPost = null;
            foreach (var c in roster.Companions)
                if (c.Id == GameSession.ProtagonistId && c.Status == CompanionStatus.Idle &&
                    string.IsNullOrEmpty(c.AssignedSlotId) && scoutingFree && scoutingOpen)
                {
                    result[c.Id] = "scouting_post";
                    protagonistPost = "scouting_post";
                }

            foreach (var kv in DefaultAssignments(roster, city))
            {
                if (starveFarms && kv.Value == "settlement_farms") continue;
                if (kv.Value == protagonistPost || result.ContainsKey(kv.Key)) continue;
                result[kv.Key] = kv.Value;
            }
            return result;
        }

        /// <summary>
        /// Черга будівель журнального гравця — одна для Unity-туру
        /// <c>-autoplay-journal</c> і <c>MechanicsJournalCompletionTests</c>
        /// (Статут UI-14). Таверна — одразу за Складом і Залою ради: журнальний
        /// гравець морить ферми (<see cref="JournalAssignments"/>), люди щодня
        /// йдуть, а тір 2 (Таверна І людність не менше порога) досяжний, лише
        /// поки людей ще досить. 29.09.2026 Таверна стояла за Майстернею: ядро
        /// встигало рівно на порозі (150 людей на 27-у добу), Unity-тур — ніколи
        /// (43 з 44, без population_tier).
        ///
        /// Поправка №12.9: перша будівля журнального гравця — Лазарет (див.
        /// <c>ChooseJournalSceneOptionIndex</c>), Зала ради серед вибору
        /// прологу вже нема — і черга будівництва відкриває раду ЩОЙНО є на
        /// що будувати. Склад — перший (щоденне золото), Зала — другою (Указ/
        /// Дипломатія/Інвестиція/Спорядження журналу все ще потребують саме
        /// її), далі Таверна/Майстерня як і раніше.
        /// </summary>
        public static readonly string[] JournalBuildPriority =
        {
            Base.DefaultBuildings.Storehouse, Base.DefaultBuildings.CouncilHall,
            Base.DefaultBuildings.Tavern, Base.DefaultBuildings.Workshop,
            Base.DefaultBuildings.Infirmary, Base.DefaultBuildings.Temple,
            Base.DefaultBuildings.Market, Base.DefaultBuildings.Fortifications
        };

        /// <summary>З цієї доби журнальний гравець відкладає гроші на Таверну і Майстерню (<see cref="JournalCouncilPlan"/>); доби 1–5 (вузол 1, криза і фінал доби 5) — без змін.</summary>
        public const int JournalSavingFromDay = 6;

        /// <summary>
        /// Ранкова рада журнального гравця: ЩО замовити, у порядку виконання.
        /// Виконує кожен водій сам, своїми командами (Unity-тур — тими самими,
        /// що кнопки вкладки «Рада», охоронець <c>AutoplayDriverGuardTests</c>).
        /// Одна стройка за ранок — перша незбудована з <see cref="JournalBuildPriority"/>;
        /// облава і переселенці — щойно готові; решта дій ради — за календарем
        /// (підготовка до загрози і спорядження вилазки — кожна третя доба,
        /// указ — четверта, дипломатія — п'ята). Відмінність від <c>BotRunner</c>:
        /// з доби <see cref="JournalSavingFromDay"/>, поки наступна в черзі
        /// Таверна чи Майстерня не по кишені, календарні дії ради (15–25 золота
        /// кожна) пропускаються — вони з'їдали весь дохід складу (+6 на добу).
        /// </summary>
        public static List<JournalCouncilOrder> JournalCouncilPlan(CityView city, EconomyView economy, int day)
        {
            var plan = new List<JournalCouncilOrder>();

            string next = null;
            foreach (var id in JournalBuildPriority)
                if (!IsBuiltOrBuilding(city, id)) { next = id; break; }
            if (next != null) plan.Add(new JournalCouncilOrder(JournalCouncilOrderKind.Build, next));

            if (city != null && city.RaidReady) plan.Add(new JournalCouncilOrder(JournalCouncilOrderKind.Raid));
            if (city != null && city.SettlersReady) plan.Add(new JournalCouncilOrder(JournalCouncilOrderKind.Settlers));

            bool saving = day >= JournalSavingFromDay &&
                          (next == Base.DefaultBuildings.Tavern || next == Base.DefaultBuildings.Workshop) &&
                          !CanAfford(next, economy);
            if (saving) return plan;

            if (day % 3 == 0)
            {
                plan.Add(new JournalCouncilOrder(JournalCouncilOrderKind.PrepareThreat));
                plan.Add(new JournalCouncilOrder(JournalCouncilOrderKind.OutfitExpedition, "outskirts"));
            }
            if (day % 4 == 0) plan.Add(new JournalCouncilOrder(JournalCouncilOrderKind.Decree, "tuhar_boyars"));
            if (day % 5 == 0) plan.Add(new JournalCouncilOrder(JournalCouncilOrderKind.Diplomacy, "tuhar_boyars"));
            return plan;
        }

        /// <summary>Виконує одну дію <see cref="JournalCouncilPlan"/> публічною командою сесії — для тестів ядра; Unity-тур кличе ті самі команди сам.</summary>
        public static void ExecuteJournalCouncilOrder(GameSession session, JournalCouncilOrder order)
        {
            switch (order.Kind)
            {
                case JournalCouncilOrderKind.Build: session.OrderBuilding(order.Arg); break;
                case JournalCouncilOrderKind.Raid: session.OrderRaid(); break;
                case JournalCouncilOrderKind.Settlers: session.OrderSettlers(); break;
                case JournalCouncilOrderKind.PrepareThreat: session.OrderPrepareThreat(); break;
                case JournalCouncilOrderKind.OutfitExpedition: session.OrderOutfitExpedition(order.Arg); break;
                case JournalCouncilOrderKind.Decree: session.OrderDecree(order.Arg); break;
                case JournalCouncilOrderKind.Diplomacy: session.OrderDiplomacy(order.Arg); break;
            }
        }

        /// <summary>Будівля стоїть або будується.</summary>
        public static bool IsBuiltOrBuilding(CityView city, string id)
        {
            if (city?.Built != null)
                foreach (var b in city.Built) if (b.Id == id) return true;
            if (city?.InProgress != null)
                foreach (var b in city.InProgress) if (b.Id == id) return true;
            return false;
        }

        /// <summary>Чи вистачає гаманця на повну ціну будівлі (без знижок — оцінка з запасом).</summary>
        private static bool CanAfford(string buildingId, EconomyView economy)
        {
            var def = Base.DefaultBuildings.Get(buildingId);
            if (def == null || economy == null) return false;
            return economy.Gold >= def.GoldCost && economy.BuildComponent >= def.BuildComponentCost;
        }

        /// <summary>
        /// Пост, для якого іменний фахівець стартового касту «свій».
        /// Поправка №12.9: НЕ виводиться з <c>FirstBuildingKeeperOf</c> +
        /// <c>OpensSlotId</c> — Захарове ремесло (Сторожа) поста не
        /// відкриває взагалі (він на council_seat, віче, з першого ранку,
        /// незалежно від вибору першої будівлі), тож той вивід дав би
        /// null і для Захара, і сплутав би «ремесло, яке озвучує вибір» із
        /// «пост, який фахівець тримає». Окрема мапа, як і просив власник.
        /// Null — у напарника свого поста немає (Максим/Мирослава/
        /// протагоніст — у полі, §3.0).
        /// </summary>
        private static string OwnPostOf(string companionId)
        {
            switch (companionId)
            {
                case "zakhar": return "council_seat";
                case "keeper": return "storehouse_dock";
                case "healer": return "infirmary_bed";
                // Поправка №12.10 (пул прибульців): ті самі правила —
                // фахівець чекає СВОГО поста, не займає чужий.
                case "goban": return "workshop_bench";
                case "sindbad": return "settlement_market";
                default: return null;
            }
        }

        /// <summary>Сентинел-QuestId синтетичного офера події данжу (§3.4, кімната 3 "Прихований попіл") — щоб ChooseQuestOption міг відрізнити його від справжнього квесту.</summary>
        public const string DungeonEventQuestId = "dungeon.event";

        /// <summary>
        /// Синтетичний PendingOfferView для точок рішення, які GameSession
        /// резолвить напряму за <c>IncidentPath</c> без проміжного офера
        /// (криза доби 5, фінал, бойова кімната данжу). Options лишається
        /// порожнім — політики цього пакету обирають шлях за власним
        /// "характером" (Quiet/Bloody за замовчуванням), не за конкретними
        /// кандидатами/порогами синтетичного офера.
        /// </summary>
        public static PendingOfferView SyntheticOffer(string kind, string topicId, bool isCrisis = false)
        {
            return new PendingOfferView
            {
                Kind = kind,
                TopicId = topicId,
                IsCrisis = isCrisis,
                Options = new List<DecisionOptionView>()
            };
        }

        /// <summary>Синтетичний QuestOfferView для події данжу (Type=="Event") — Options.TextKey несе ключ варіанту, ChooseQuestOption повертає індекс.</summary>
        public static QuestOfferView SyntheticDungeonEventOffer(DungeonRoomView room)
        {
            var options = new List<DecisionOptionView>();
            if (room?.EventOptionKeys != null)
                foreach (var key in room.EventOptionKeys)
                    options.Add(new DecisionOptionView { TextKey = key });

            return new QuestOfferView
            {
                Kind = "Event",
                TopicId = room?.Id,
                QuestId = DungeonEventQuestId,
                Options = options
            };
        }

        /// <summary>Індекс у межах [0, count) — синтетичний офер може мати менше варіантів, ніж політика "хоче".</summary>
        public static int ClampIndex(int index, int count)
        {
            if (count <= 0) return 0;
            if (index < 0) return 0;
            if (index >= count) return count - 1;
            return index;
        }

        // ---- сценовий вибір (§4.10, Поправка №7.8): "характер" політики на Choice-кроці ----

        /// <summary>Індекс першого варіанту з Form=="Intimidate" — -1, якщо такого немає.</summary>
        private static int FirstByForm(SceneStepView step, string form)
        {
            var options = step?.Options;
            if (options == null) return -1;
            for (int i = 0; i < options.Count; i++)
                if (string.Equals(options[i]?.Form, form, StringComparison.Ordinal)) return i;
            return -1;
        }

        /// <summary>
        /// BloodyPolicy (§4.10): найагресивніший варіант — перевірка
        /// Залякування (звинуватити/погрожувати), а нема такої — останній
        /// варіант (у сценах цієї збірки саме він, як правило, лишає
        /// найгостріший наслідок — "відпустити" зрадницю, "тримати перевал").
        /// </summary>
        public static int ChooseSceneAggressive(SceneStepView step)
        {
            int count = step?.Options?.Count ?? 0;
            int byIntimidate = FirstByForm(step, "Intimidate");
            if (byIntimidate >= 0) return byIntimidate;
            return ClampIndex(count - 1, count);
        }

        /// <summary>
        /// PacifistPolicy (§4.10): найм'якший варіант — перевірка Переконання
        /// (умовити/попросити), а нема такої — перший варіант БЕЗ перевірки
        /// Залякування (у сценах цієї збірки це або "довіритись", або
        /// "відмовити словом", ніколи не силове рішення).
        /// </summary>
        public static int ChooseScenePersuasive(SceneStepView step)
        {
            var options = step?.Options;
            int count = options?.Count ?? 0;
            int byPersuade = FirstByForm(step, "Persuade");
            if (byPersuade >= 0) return byPersuade;

            if (options != null)
                for (int i = 0; i < options.Count; i++)
                    if (!string.Equals(options[i]?.Form, "Intimidate", StringComparison.Ordinal)) return i;

            return ClampIndex(0, count);
        }

        /// <summary>Решта політик (Steward/PatrolAlways/DelveGreedy, §4.10): перший виборний варіант — той самий "обережний за замовчуванням" норов, що й StewardPolicy.ChooseIncidentPath.</summary>
        public static int ChooseSceneDefault(SceneStepView step) => ClampIndex(0, step?.Options?.Count ?? 0);

        // ---- геометрія покрокового бою (§4.10: "хоча б одна політика грає бій ходами") ----

        public static int Chebyshev(GridPosView a, GridPosView b)
        {
            int dx = Math.Abs(a.X - b.X);
            int dy = Math.Abs(a.Y - b.Y);
            return dx > dy ? dx : dy;
        }

        /// <summary>
        /// Хто зараз ходить: пряме зіставлення за <see cref="BattleView.CurrentUnitId"/>
        /// (§4.2.1, фікс-ревью D2-блокера). Раніше тут стояла евристика "чия
        /// клітинка входить у ReachableTiles" — хибна, бо
        /// <c>Pathfinder.Reachable</c> навмисно НЕ включає стартовий тайл у
        /// видачу (див. doc-коментар класу): жоден юніт "не входив" у власні
        /// прохідні тайли, і FindCurrent завжди повертав null, тож BotRunner
        /// ніколи не доходив до CombatMove/Attack/EnterOverwatch, лише спамив
        /// CombatEndTurn. Null, якщо активного юніта немає (бій завершився).
        /// </summary>
        public static BattleUnitView FindCurrent(BattleView battle)
        {
            if (battle?.Units == null || string.IsNullOrEmpty(battle.CurrentUnitId)) return null;
            foreach (var u in battle.Units)
                if (string.Equals(u.Id, battle.CurrentUnitId, StringComparison.Ordinal)) return u;
            return null;
        }

        /// <summary>Найближчий живий юніт іншої сторони, ніж <paramref name="from"/> ("FromDefector" рахується як ворог гравця).</summary>
        public static BattleUnitView FindNearestOpposite(BattleView battle, BattleUnitView from)
        {
            if (battle?.Units == null || from == null) return null;
            bool fromIsPlayer = string.Equals(from.Side, "Player", StringComparison.Ordinal);

            BattleUnitView best = null;
            int bestDist = int.MaxValue;
            foreach (var u in battle.Units)
            {
                if (u == from || u.IsDowned) continue;
                bool uIsPlayer = string.Equals(u.Side, "Player", StringComparison.Ordinal);
                if (uIsPlayer == fromIsPlayer) continue; // своя сторона

                int d = Chebyshev(from.Pos, u.Pos);
                if (d < bestDist) { bestDist = d; best = u; }
            }
            return best;
        }

        /// <summary>Досяжний тайл, що найбільше скорочує дистанцію до цілі (анти-осциляція — той самий прийом, що й CombatAi.TryStepToward).</summary>
        public static GridPosView? StepToward(BattleView battle, BattleUnitView from, GridPosView goal)
        {
            if (battle?.ReachableTiles == null || from == null) return null;

            int curDist = Chebyshev(from.Pos, goal);
            GridPosView? best = null;
            int bestDist = curDist;

            foreach (var tile in battle.ReachableTiles)
            {
                if (tile.X == from.Pos.X && tile.Y == from.Pos.Y) continue; // не топтатись на місці
                int d = Chebyshev(tile, goal);
                if (d < bestDist) { bestDist = d; best = tile; }
            }
            return best;
        }
    }

    /// <summary>Дія ранкової ради журнального гравця (<see cref="BotSupport.JournalCouncilPlan"/>).</summary>
    public enum JournalCouncilOrderKind { Build, Raid, Settlers, PrepareThreat, OutfitExpedition, Decree, Diplomacy }

    /// <summary>Одна дія ради: вид і аргумент (будівля, точка вилазки чи фракція).</summary>
    public readonly struct JournalCouncilOrder
    {
        public readonly JournalCouncilOrderKind Kind;
        public readonly string Arg;

        public JournalCouncilOrder(JournalCouncilOrderKind kind, string arg = null)
        {
            Kind = kind;
            Arg = arg;
        }
    }
}
