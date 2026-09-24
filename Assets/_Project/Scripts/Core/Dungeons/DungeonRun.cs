using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Game.Core.Balance;
using Game.Core.Base;
using Game.Core.Checks;
using Game.Core.Economy;
using Game.Core.Loop;

namespace Game.Core.Dungeons
{
    /// <summary>Як прогін данжу завершився. Push/Extract/Abandon/Wipe — усі семантики R4.</summary>
    public enum DungeonOutcome
    {
        InProgress = 0,
        Extracted = 1, // вийшли з видобутком — забанковано
        Wiped = 2,     // відряд не витримав бій — незабанковане втрачено
        Abandoned = 3  // пішли самі, не забираючи незабанкованого (обережний вихід)
    }

    /// <summary>
    /// Прихована шкала «Threat» (інваріант 6):
    /// ЯКІР — скільки шуму відряд уже наробив у цьому данжі;
    /// ПОТРЕБИТЕЛЬ — <see cref="DungeonRun"/> сам піднімає ефективний порог
    /// тихого обходу на кожну полосу вище Calm (глибше = важче прослизнути);
    /// СИГНАЛ — кожен розв'язок кімнати несе <c>ThreatBandChanged</c>
    /// (<see cref="RoomResolution"/>), Core/Dungeons не має доступу до шару
    /// подій/сигналів, тож САМ сигнал шле D1, побачивши цей прапорець (seamsForD1).
    /// Число — internal (Game.Gameplay його не бачить), назовні йде лише полоса.
    /// </summary>
    public enum DungeonThreatBand
    {
        Calm = 0,
        Tense = 1,
        Dangerous = 2,
        Deadly = 3
    }

    /// <summary>
    /// Те, що готовий бій потребує від того, хто його запускає (D1/GameSession).
    /// Жодного типу з Game.Core.Combat — лише дані: id ворогів, ключ арени, хто
    /// в партії. <see cref="DungeonRun"/> лише ПРОСИТЬ бій і чекає
    /// <see cref="DungeonRun.ReportCombat"/> — сам бій веде викликач (R4).
    /// </summary>
    public sealed class BattleRequest
    {
        public readonly string ArenaKey;
        public readonly IReadOnlyList<string> EnemyIds;
        public readonly IReadOnlyList<string> PartyIds;

        public BattleRequest(string arenaKey, IReadOnlyList<string> enemyIds, IReadOnlyList<string> partyIds)
        {
            ArenaKey = arenaKey;
            EnemyIds = enemyIds;
            PartyIds = partyIds;
        }
    }

    /// <summary>
    /// Наслідки одного розв'язку, які Core/Dungeons НЕ застосовує сам — дані для
    /// D1. Страх (FearState) і фракції (FactionRegistry) — інші пакети; тут лише
    /// прапорець і id. Флаги — рядкові id для StoryFlags (Foundation/A1).
    /// </summary>
    public sealed class DungeonConsequence
    {
        public static readonly DungeonConsequence None = new DungeonConsequence(false, null, null);

        public readonly bool CausedFear;
        public readonly IReadOnlyDictionary<string, int> FactionDeltas;
        public readonly IReadOnlyList<string> FlagsToSet;

        public DungeonConsequence(bool causedFear, IReadOnlyDictionary<string, int> factionDeltas,
            IReadOnlyList<string> flagsToSet)
        {
            CausedFear = causedFear;
            FactionDeltas = factionDeltas ?? EmptyDeltas;
            FlagsToSet = flagsToSet ?? EmptyFlags;
        }

        private static readonly Dictionary<string, int> EmptyDeltas = new Dictionary<string, int>();
        private static readonly List<string> EmptyFlags = new List<string>();
    }

    /// <summary>
    /// Підсумок одного розв'язку кімнати — усе, що D1 читає для лога/UI. Лише
    /// id/ключі/числа, жодного тексту гравця (Core віддає ключі — R7).
    /// </summary>
    public sealed class RoomResolution
    {
        public string RoomId;
        public DungeonRoomKind Kind;
        public bool Cleared;
        public bool Bypassed;      // тихий обхід без бою (0 ризику — Поправка №1 для данжу)
        public bool NeedsBattle;   // кроваво на бойовій кімнаті — чекаємо ReportCombat
        public bool Wiped;         // бій пішов не так — прогін завершено
        public OutcomeBand? QuietBand;
        public int GainedMaterials;
        public int GainedGold;
        public IReadOnlyList<string> GrantedItemIds = Array.Empty<string>();
        public string EventOptionId; // лише для Kind == Event
        public DungeonConsequence Consequence = DungeonConsequence.None;
        public bool ThreatBandChanged;
    }

    /// <summary>Що винесли при екстракті — банкується в той самий BaseState.Resources.</summary>
    public sealed class DungeonExtractReport
    {
        public int Materials;
        public int Gold;
        public int DepthReached;
        public IReadOnlyList<string> ItemIds = Array.Empty<string>();

        /// <summary>
        /// Фікс мажора ревʼю B2 (інваріант 4): Extract/Abandon теж завершують
        /// прогін і мусять донести зміну полоси Threat, якщо гравець вийшов,
        /// не розв'язавши щойно відкриту Push-ом кімнату (тоді жоден
        /// RoomResolution з цим переходом уже не прийде).
        /// </summary>
        public bool ThreatBandChanged;
    }

    /// <summary>
    /// Push-your-luck прогін данжу (Core/Dungeons, R4). Повністю детермінований:
    /// кімнати й їх порядок — авторський контент (<see cref="DefaultDungeon"/>),
    /// жодного генератора чи кубика. Лут копиться в «незабанковане» і йде в
    /// <see cref="Base.BaseState.Resources"/> ТІЛЬКИ на <see cref="Extract"/>;
    /// <see cref="ReportCombat"/> з Worst-полосою (вайп) незабанковане знищує.
    ///
    /// Бойову кімнату сам НЕ резолвить: кровавий шлях або провалений тихий
    /// обхід переводить прогін у стан «чекає бою» (<see cref="AwaitingBattle"/>,
    /// <see cref="PendingBattle"/>) — бій веде викликач (D1/GameSession),
    /// результат повертається через <see cref="ReportCombat"/>.
    /// </summary>
    public sealed class DungeonRun : IStateBlob
    {
        private readonly IReadOnlyList<DungeonRoomDefinition> _rooms;
        private readonly List<string> _partyIds;
        private readonly BalanceConfig _cfg;
        private readonly List<string> _unbankedItemIds = new List<string>();

        private int _roomIndex = -1;

        /// <summary>
        /// Полоса Threat, яку востаннє побачив виклик, що повернув
        /// <see cref="RoomResolution"/> (інваріант 4 — фікс блокера ревью B2).
        /// Threat міняється лише у <see cref="Push"/> і в <see cref="ResolveEvent"/>
        /// (дельта варіанту), а Push НЕ повертає RoomResolution — тож порівняння
        /// живе тут і застосовується в <see cref="FinalizeResolution"/> при
        /// НАСТУПНОМУ розв'язку кімнати, а не лише всередині ResolveEvent.
        /// </summary>
        private DungeonThreatBand _lastReportedBand = DungeonThreatBand.Calm;

        /// <summary>
        /// Полоса Threat, зафіксована ПЕРЕД входом у поточну кімнату (до
        /// власного <see cref="Push"/>) — фікс БЛОКЕРА ревью B2 (інваріант 8:
        /// «показаний порог = застосований»). <see cref="EffectiveThreshold"/>
        /// штрафує тихий обхід ЦІЄЮ полосою, а не живою <see cref="ThreatBand"/>:
        /// інакше вхідний Push у кожну кімнату (уключно з першою — він
        /// одразу підіймає Threat ще в конструкторі) підіймав би власний порог
        /// кімнати ДО того, як гравець у ній хоч щось зробив, і документований
        /// «Виживання ≥5 / Переконання ≥5» кімнати 1 (§3.4/§7.11) мовчки ставав
        /// би ≥6. Оновлюється лише в <see cref="Push"/>, тож дельта
        /// <see cref="ResolveEvent"/> у ПОТОЧНІЙ кімнаті законно НЕ штрафує
        /// власний тихий чек цієї ж кімнати, але вже враховується для
        /// наступної.
        /// </summary>
        private DungeonThreatBand _roomEntryThreatBand = DungeonThreatBand.Calm;

        public string SiteId { get; }

        /// <summary>Номер поточної кімнати, 1-base. 0 — ще не увійшли (не буває назовні).</summary>
        public int Depth => _roomIndex + 1;

        public int RoomsCleared { get; private set; }
        public DungeonOutcome Outcome { get; private set; } = DungeonOutcome.InProgress;

        /// <summary>Прихована шкала (інваріант 3) — назовні лише <see cref="ThreatBand"/>.</summary>
        internal int Threat { get; private set; }

        public DungeonThreatBand ThreatBand => BandFor(Threat, _cfg.Dungeon);

        public DungeonRoomDefinition CurrentRoom =>
            _roomIndex >= 0 && _roomIndex < _rooms.Count ? _rooms[_roomIndex] : null;

        public bool CurrentCleared { get; private set; }
        public bool AwaitingBattle { get; private set; }
        public BattleRequest PendingBattle { get; private set; }

        public int UnbankedMaterials { get; private set; }
        public int UnbankedGold { get; private set; }
        public IReadOnlyList<string> UnbankedItemIds => _unbankedItemIds;
        public IReadOnlyList<string> PartyIds => _partyIds;

        public bool Active => Outcome == DungeonOutcome.InProgress;

        public DungeonRun(string siteId, IReadOnlyList<DungeonRoomDefinition> rooms,
            IReadOnlyList<string> partyIds, BalanceConfig cfg)
        {
            if (string.IsNullOrEmpty(siteId)) throw new ArgumentNullException(nameof(siteId));
            if (rooms == null || rooms.Count == 0) throw new ArgumentException("Данж без кімнат", nameof(rooms));

            SiteId = siteId;
            _rooms = rooms;
            _partyIds = partyIds != null ? new List<string>(partyIds) : new List<string>();
            _cfg = cfg ?? new BalanceConfig();

            Push(); // одразу входимо в першу кімнату (Depth=1), як у прообразі механіки
        }

        /// <summary>Іти глибше: наступна авторська кімната. Поточна мусить бути пройдена.</summary>
        public DungeonRoomDefinition Push()
        {
            RequireInProgress();
            if (CurrentRoom != null && !CurrentCleared)
                throw new InvalidOperationException("Поточна кімната не пройдена");
            if (_roomIndex + 1 >= _rooms.Count)
                throw new InvalidOperationException("Далі немає кімнат — лишається лише Extract/Abandon");

            // Знімок ДО власного підвищення Threat -- саме він, а не свіжа
            // ThreatBand, штрафує тихий обхід кімнати, у яку зараз заходимо
            // (фікс блокера ревью B2, див. коментар над полем).
            _roomEntryThreatBand = ThreatBand;

            _roomIndex++;
            Threat += _cfg.Dungeon.ThreatPerPush;
            CurrentCleared = false;
            AwaitingBattle = false;
            PendingBattle = null;
            return CurrentRoom;
        }

        /// <summary>
        /// Фікс мажора ревью B2: реальний порог тихого обходу для цього запиту
        /// у поточній кімнаті — той самий, що застосовує <see cref="BestQuietBand"/>
        /// зсередини, з урахуванням штрафу Threat. D1 показує гравцю САМЕ це
        /// число (інваріант 8), а не сирий
        /// <see cref="DungeonRoomDefinition.QuietChecks"/>[i].Threshold, який
        /// без цього методу був єдиним, що бачив назовні світ (seamsForD1).
        /// </summary>
        public int EffectiveQuietThreshold(CheckRequest req) => EffectiveThreshold(req.Threshold);

        /// <summary>
        /// Розв'язок Combat- чи Cache-кімнати шляхом тихо/кроваво (Event —
        /// <see cref="ResolveEvent"/>). Cache не має шляху — гарантований лут.
        /// Combat: кроваво (або провалений тихий обхід) → «чекає бою», без
        /// звернення до жодного бойового типу.
        /// </summary>
        public RoomResolution ResolveRoom(IncidentPath path, IReadOnlyList<ISettlementActor> party)
        {
            RequireInProgress();
            var room = CurrentRoom;
            if (room == null || CurrentCleared)
                throw new InvalidOperationException("Немає що розв'язувати — кімната вже пройдена");
            if (AwaitingBattle)
                throw new InvalidOperationException("Кімната вже чекає бою — розв'язує ReportCombat");
            if (room.Kind == DungeonRoomKind.Event)
                throw new InvalidOperationException("Подію розв'язує вибір гравця — ResolveEvent");

            var res = new RoomResolution { RoomId = room.Id, Kind = room.Kind };

            if (room.Kind == DungeonRoomKind.Cache)
            {
                AddLoot(room.GuaranteedMaterials, room.GuaranteedGold, res);
                GrantNamedItem(room, res);
                MarkCleared(res);
                FinalizeResolution(res);
                return res;
            }

            // room.Kind == Combat
            if (path == IncidentPath.Bloody)
            {
                EnterAwaitingBattle(room, res);
                FinalizeResolution(res);
                return res;
            }

            var band = BestQuietBand(room, party);
            res.QuietBand = band;

            if (band == OutcomeBand.Worst)
            {
                // Не вдалося прослизнути тихо чи вмовити — відряд помічений, бій неминучий.
                EnterAwaitingBattle(room, res);
                FinalizeResolution(res);
                return res;
            }

            res.Bypassed = true; // 0 ризику (Поправка №1 для данжу) — жодної нагороди, жодного бою
            MarkCleared(res);
            FinalizeResolution(res);
            return res;
        }

        /// <summary>Результат бою з бойової кімнати. Casualties — лише id, жодних мутацій тут.</summary>
        public RoomResolution ReportCombat(OutcomeBand band, IReadOnlyList<string> casualties)
        {
            RequireInProgress();
            if (!AwaitingBattle || CurrentRoom == null)
                throw new InvalidOperationException("Немає бою, що очікує результату");

            var room = CurrentRoom;
            var res = new RoomResolution { RoomId = room.Id, Kind = room.Kind };

            AwaitingBattle = false;
            PendingBattle = null;

            if (band == OutcomeBand.Worst)
            {
                Outcome = DungeonOutcome.Wiped;
                res.Wiped = true;
                ClearUnbanked();
                FinalizeResolution(res);
                return res;
            }

            AddLoot(room.GuaranteedMaterials, room.GuaranteedGold, res);
            MarkCleared(res);
            FinalizeResolution(res);
            return res;
        }

        /// <summary>Розв'язок кімнати-події (кімната 3, §3.4) вибором гравця.</summary>
        public RoomResolution ResolveEvent(int optionIndex)
        {
            RequireInProgress();
            var room = CurrentRoom;
            if (room == null || CurrentCleared)
                throw new InvalidOperationException("Немає що розв'язувати — кімната вже пройдена");
            if (room.Kind != DungeonRoomKind.Event)
                throw new InvalidOperationException("Це не подія — розв'язує ResolveRoom");
            if (optionIndex < 0 || optionIndex >= room.EventOptions.Count)
                throw new ArgumentOutOfRangeException(nameof(optionIndex), "Немає такого варіанту події");

            var opt = room.EventOptions[optionIndex];
            var res = new RoomResolution { RoomId = room.Id, Kind = room.Kind, EventOptionId = opt.Id };

            AddLoot(opt.MaterialsGain, opt.GoldGain, res);

            if (opt.ThreatDelta != 0)
                Threat += opt.ThreatDelta;

            res.Consequence = new DungeonConsequence(opt.CausesFear, opt.FactionDeltas, opt.FlagsToSet);
            MarkCleared(res);
            FinalizeResolution(res); // ловить і дельту події, і будь-яку ще не здану зміну від Push (див. поле вище)
            return res;
        }

        /// <summary>Банкує незабанковане в той самий кошик поселення. Можна будь-коли, доки прогін не завершено.</summary>
        public DungeonExtractReport Extract(BaseState baseState)
        {
            RequireInProgress();

            var rep = new DungeonExtractReport
            {
                Materials = UnbankedMaterials,
                Gold = UnbankedGold,
                DepthReached = Depth,
                ItemIds = new List<string>(_unbankedItemIds),
                // Фікс мажора ревью B2: якщо гравець зайшов у нову кімнату
                // Push-ом і екстрактнувся, не розв'язавши її, жоден
                // RoomResolution з цим переходом полоси більше не прийде --
                // тож саме тут забираємо його з тим самим лічильником.
                ThreatBandChanged = ConsumeThreatBandChange()
            };

            if (baseState != null)
            {
                baseState.Resources.Add(ResourceType.Materials, UnbankedMaterials);
                baseState.Resources.Add(ResourceType.Gold, UnbankedGold);
            }

            ClearUnbanked();
            Outcome = DungeonOutcome.Extracted;
            return rep;
        }

        /// <summary>Піти без видобутку (обережний вихід, а не вайп) — незабанковане пропадає так само.</summary>
        public DungeonExtractReport Abandon()
        {
            RequireInProgress();
            // Той самий фікс, що й у Extract: обережний вихід так само
            // завершує прогін і не повинен ковтнути ще не здану зміну полоси.
            var rep = new DungeonExtractReport
            {
                DepthReached = Depth,
                ThreatBandChanged = ConsumeThreatBandChange()
            };
            ClearUnbanked();
            Outcome = DungeonOutcome.Abandoned;
            return rep;
        }

        // ---- слепок (IStateBlob) ----
        //
        // НЕ підключено до SettlementSave/GameSession (R13 дозволяє SaveState
        // лише в Morning, а Dungeon — підвішений стан SuspendToken, тож посеред
        // прогону збереження сьогодні недосяжне). Контракт готовий про запас —
        // якщо D1 колись дозволить сейв підвішених станів, тут уже є що читати
        // (seamsForD1). Формат — «ключ:значення» через «|», без «;»/«=»
        // (конвенція BaseState.CaptureState).
        public string CaptureState()
        {
            var sb = new StringBuilder();
            sb.Append("o:").Append(((int)Outcome).ToString(CultureInfo.InvariantCulture));
            sb.Append("|r:").Append(_roomIndex.ToString(CultureInfo.InvariantCulture));
            sb.Append("|c:").Append(CurrentCleared ? '1' : '0');
            sb.Append("|a:").Append(AwaitingBattle ? '1' : '0');
            sb.Append("|t:").Append(Threat.ToString(CultureInfo.InvariantCulture));
            sb.Append("|rc:").Append(RoomsCleared.ToString(CultureInfo.InvariantCulture));
            sb.Append("|um:").Append(UnbankedMaterials.ToString(CultureInfo.InvariantCulture));
            sb.Append("|ug:").Append(UnbankedGold.ToString(CultureInfo.InvariantCulture));
            sb.Append("|it:").Append(string.Join(",", _unbankedItemIds));
            sb.Append("|lb:").Append(((int)_lastReportedBand).ToString(CultureInfo.InvariantCulture));
            // Фікс блокера ревью B2: без цього поля відновлений прогін штрафував
            // би тихий обхід поточної кімнати живою ThreatBand (уже після
            // власного Push) замість зафіксованої "на вході" -- сейв/рестор
            // тихо зсунув би застосований порог.
            sb.Append("|eb:").Append(((int)_roomEntryThreatBand).ToString(CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        public void RestoreState(string blob)
        {
            if (string.IsNullOrEmpty(blob)) return;

            foreach (var part in blob.Split('|'))
            {
                int sep = part.IndexOf(':');
                if (sep < 0) continue;
                string key = part.Substring(0, sep);
                string body = part.Substring(sep + 1);

                switch (key)
                {
                    case "o": Outcome = (DungeonOutcome)ParseInt(body); break;
                    case "r": _roomIndex = ParseInt(body); break;
                    case "c": CurrentCleared = body == "1"; break;
                    case "a":
                        AwaitingBattle = body == "1";
                        PendingBattle = AwaitingBattle && CurrentRoom != null
                            ? new BattleRequest(CurrentRoom.ArenaKey, CurrentRoom.EnemyIds, _partyIds)
                            : null;
                        break;
                    case "t": Threat = ParseInt(body); break;
                    case "rc": RoomsCleared = ParseInt(body); break;
                    case "um": UnbankedMaterials = ParseInt(body); break;
                    case "ug": UnbankedGold = ParseInt(body); break;
                    case "it":
                        _unbankedItemIds.Clear();
                        if (body.Length > 0) _unbankedItemIds.AddRange(body.Split(','));
                        break;
                    case "lb": _lastReportedBand = (DungeonThreatBand)ParseInt(body); break;
                    case "eb": _roomEntryThreatBand = (DungeonThreatBand)ParseInt(body); break;
                }
            }
        }

        // ---- внутрішнє ----

        private void EnterAwaitingBattle(DungeonRoomDefinition room, RoomResolution res)
        {
            AwaitingBattle = true;
            PendingBattle = new BattleRequest(room.ArenaKey, room.EnemyIds, _partyIds);
            res.NeedsBattle = true;
        }

        private void GrantNamedItem(DungeonRoomDefinition room, RoomResolution res)
        {
            if (string.IsNullOrEmpty(room.NamedItemId)) return;
            _unbankedItemIds.Add(room.NamedItemId);
            res.GrantedItemIds = new List<string> { room.NamedItemId };
        }

        /// <summary>
        /// Фікс блокера ревью B2: єдине місце, де смена полоси Threat стає
        /// видимою (інваріант 4). Threat росте у <see cref="Push"/> (щокроку,
        /// уключно з першим входом) і в <see cref="ResolveEvent"/> (дельта
        /// варіанту) — Push сам не повертає RoomResolution, тож зміна чекає тут
        /// до наступного розв'язку кімнати (ResolveRoom/ReportCombat/ResolveEvent)
        /// і саме тоді потрапляє в прапорець, який читає D1 (seamsForD1).
        /// </summary>
        private void FinalizeResolution(RoomResolution res)
        {
            res.ThreatBandChanged = ConsumeThreatBandChange();
        }

        /// <summary>
        /// Спільна точка порівняння живої <see cref="ThreatBand"/> з останньою
        /// повідомленою (фікс мажора ревью B2): раніше нею користувався лише
        /// <see cref="FinalizeResolution"/>, тепер і <see cref="Extract"/> з
        /// <see cref="Abandon"/> — обидва так само завершують прогін і так
        /// само здатні "проковтнути" ще не здану зміну полоси (гравець
        /// Push-нув у нову кімнату й вийшов, не розв'язавши її).
        /// </summary>
        private bool ConsumeThreatBandChange()
        {
            bool changed = ThreatBand != _lastReportedBand;
            _lastReportedBand = ThreatBand;
            return changed;
        }

        private void MarkCleared(RoomResolution res)
        {
            res.Cleared = true;
            CurrentCleared = true;
            RoomsCleared++;
        }

        private void AddLoot(int materials, int gold, RoomResolution res)
        {
            UnbankedMaterials += materials;
            UnbankedGold += gold;
            res.GainedMaterials += materials;
            res.GainedGold += gold;
        }

        private void ClearUnbanked()
        {
            UnbankedMaterials = 0;
            UnbankedGold = 0;
            _unbankedItemIds.Clear();
        }

        private void RequireInProgress()
        {
            if (Outcome != DungeonOutcome.InProgress)
                throw new InvalidOperationException("Прогін данжу завершено");
        }

        /// <summary>
        /// Найкраща полоса серед альтернативних тихих перевірок (OR — відряд бере
        /// ту, у якій сильніший). Той самий поріг, зсунутий Threat: глибше й
        /// шумніше — важче прослизнути (потребитель шкали, інваріант 6).
        /// </summary>
        private OutcomeBand BestQuietBand(DungeonRoomDefinition room, IReadOnlyList<ISettlementActor> party)
        {
            var best = OutcomeBand.Worst;
            for (int i = 0; i < room.QuietChecks.Count; i++)
            {
                var req = room.QuietChecks[i];
                int value = PartyValue(party, req.Skill);
                int margin = value - EffectiveThreshold(req.Threshold);
                var band = CheckResolver.BandFor(margin, req.Approach, _cfg.Checks);
                if (band > best) best = band;
            }
            return best;
        }

        private int EffectiveThreshold(int baseThreshold)
            => baseThreshold + _cfg.Dungeon.ThreatQuietPenaltyStep * (int)_roomEntryThreatBand;

        /// <summary>
        /// Сила відряду на одному навику: лідер (найкраще значення) плюс
        /// половина від решти. Той самий принцип, що й у заглушці вилазки —
        /// підрахунок ЗАВЕДЕНО ОКРЕМО (не викликає Core/Expeditions): B2 не
        /// залежить від паралельного пакета навіть на рівні формули.
        /// </summary>
        private static int PartyValue(IReadOnlyList<ISettlementActor> party, SkillKey skill)
        {
            if (party == null || party.Count == 0 || skill.IsNone) return 0;

            var values = new List<int>(party.Count);
            for (int i = 0; i < party.Count; i++)
            {
                var a = party[i];
                if (a == null) continue;
                values.Add(a.GetCheckValue(skill) + a.GetTraitModifier(skill));
            }
            if (values.Count == 0) return 0;

            values.Sort((x, y) => y.CompareTo(x));
            int total = values[0];
            for (int i = 1; i < values.Count; i++) total += values[i] / 2;
            return total;
        }

        private static DungeonThreatBand BandFor(int threat, DungeonBalance cfg)
        {
            if (threat >= cfg.ThreatDeadlyAt) return DungeonThreatBand.Deadly;
            if (threat >= cfg.ThreatDangerousAt) return DungeonThreatBand.Dangerous;
            if (threat >= cfg.ThreatTenseAt) return DungeonThreatBand.Tense;
            return DungeonThreatBand.Calm;
        }

        private static int ParseInt(string s)
        {
            int v;
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : 0;
        }
    }
}
