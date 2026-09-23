using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Game.Core.Balance;
using Game.Core.Economy;

namespace Game.Core.Base
{
    public enum BuildOrderResult
    {
        Started = 0,
        UnknownBuilding,
        AlreadyBuilt,
        AlreadyInProgress,
        QuestOnly,
        NotEnoughGold,
        NotEnoughMaterials
    }

    public enum CouncilOrderResult
    {
        Queued = 0,
        NoCouncilHall,
        AlreadyQueued,
        OnCooldown,
        NotEnoughGold,
        NotEnoughFood
    }

    /// <summary>
    /// Городские работы: что построено, что строится, что приказал совет и кто
    /// пришёл извне (Поправка №6).
    ///
    /// Всё, что игрок заказывает, ОПЛАЧИВАЕТСЯ СРАЗУ, а исполняется конвейером
    /// дня — шагом <see cref="CityWorksStep"/>. Причина: изменение Напряжения
    /// или населения в промежутке между сутками не попало бы ни в один отчёт, и
    /// игрок увидел бы сдвиг без причины. Всё, что двигает город, обязано
    /// случиться внутри суток и прозвучать.
    /// </summary>
    public sealed class CityWorks : Loop.IStateBlob
    {
        private sealed class Project
        {
            public string Id;
            public int DaysLeft;
            public int TotalDays;
        }

        private readonly HashSet<string> _built = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<Project> _projects = new List<Project>();

        private bool _raidQueued;
        private int _lastRaidDay = int.MinValue / 2;
        private int _settlersQueued;
        private int _lastSettlersDay = int.MinValue / 2;
        private int _arrivalsQueued;

        public CityWorks(IEnumerable<string> alreadyBuilt = null)
        {
            if (alreadyBuilt != null)
                foreach (var id in alreadyBuilt)
                    if (!string.IsNullOrEmpty(id)) _built.Add(id);
        }

        public bool Has(string buildingId)
        {
            return buildingId != null && _built.Contains(buildingId);
        }

        public bool IsBuilding(string buildingId)
        {
            return FindProject(buildingId) != null;
        }

        public IEnumerable<string> Built
        {
            get { return _built; }
        }

        /// <summary>
        /// Видимая стадия постройки 0..5 (US-7.3): 0 — не начата, 1–4 — леса,
        /// 5 — готова. Стадия — чистая функция от прошедших суток, отдельного
        /// геймплея на стадиях нет.
        /// </summary>
        public int StageOf(string buildingId)
        {
            if (Has(buildingId)) return 5;

            var p = FindProject(buildingId);
            if (p == null) return 0;

            int elapsed = p.TotalDays - p.DaysLeft;
            int stage = 1 + (elapsed * 4) / Math.Max(1, p.TotalDays);
            return stage > 4 ? 4 : stage;
        }

        // ================= заказы игрока =================

        /// <summary>Заложить здание: цена списывается сразу, стройка идёт по суткам.</summary>
        public BuildOrderResult Order(string buildingId, BaseState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));

            var def = DefaultBuildings.Get(buildingId);
            if (def == null) return BuildOrderResult.UnknownBuilding;
            if (Has(buildingId)) return BuildOrderResult.AlreadyBuilt;
            if (IsBuilding(buildingId)) return BuildOrderResult.AlreadyInProgress;
            if (def.QuestOnly) return BuildOrderResult.QuestOnly;

            // Сначала проверяем обе цены, потом списываем: иначе при нехватке
            // материалов золото ушло бы, а стройка не началась.
            if (!state.Resources.CanAfford(ResourceType.Gold, def.GoldCost))
                return BuildOrderResult.NotEnoughGold;
            if (!state.Resources.CanAfford(ResourceType.Materials, def.MaterialsCost))
                return BuildOrderResult.NotEnoughMaterials;

            state.Resources.TrySpend(ResourceType.Gold, def.GoldCost);
            state.Resources.TrySpend(ResourceType.Materials, def.MaterialsCost);

            _projects.Add(new Project { Id = def.Id, DaysLeft = Math.Max(1, def.Days), TotalDays = Math.Max(1, def.Days) });
            return BuildOrderResult.Started;
        }

        /// <summary>
        /// Облава: разовое снижение Напряжения драйвером CouncilRaid. Требует
        /// Зал совета, стоит золота, имеет откат.
        /// </summary>
        public CouncilOrderResult OrderRaid(BaseState state, int today, BalanceConfig balance)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (balance == null) throw new ArgumentNullException(nameof(balance));

            if (!Has(DefaultBuildings.CouncilHall)) return CouncilOrderResult.NoCouncilHall;
            if (_raidQueued) return CouncilOrderResult.AlreadyQueued;
            if (!RaidReady(today, balance)) return CouncilOrderResult.OnCooldown;
            if (!state.Resources.TrySpend(ResourceType.Gold, balance.City.RaidGoldCost))
                return CouncilOrderResult.NotEnoughGold;

            _raidQueued = true;
            return CouncilOrderResult.Queued;
        }

        public bool RaidReady(int today, BalanceConfig balance)
        {
            return today - _lastRaidDay >= balance.Tension.RaidCooldownDays;
        }

        /// <summary>
        /// Приём переселенцев — «рішення в місті» из слов владельца. Платится
        /// едой: новых ртов надо кормить, и это честная цена роста.
        /// </summary>
        public CouncilOrderResult OrderSettlers(BaseState state, int today, BalanceConfig balance)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (balance == null) throw new ArgumentNullException(nameof(balance));

            if (!Has(DefaultBuildings.CouncilHall)) return CouncilOrderResult.NoCouncilHall;
            if (_settlersQueued > 0) return CouncilOrderResult.AlreadyQueued;
            if (today - _lastSettlersDay < balance.City.SettlersCooldownDays) return CouncilOrderResult.OnCooldown;
            if (!state.Resources.TrySpend(ResourceType.Food, balance.City.SettlersFoodCost))
                return CouncilOrderResult.NotEnoughFood;

            _settlersQueued = balance.City.SettlersPerOrder;
            return CouncilOrderResult.Queued;
        }

        /// <summary>Люди, найденные вылазкой: придут в город ближайшими сутками.</summary>
        public void QueueArrivals(int people)
        {
            if (people > 0) _arrivalsQueued += people;
        }

        // ================= исполнение внутри суток =================

        internal List<string> AdvanceConstruction()
        {
            var done = new List<string>();
            for (int i = _projects.Count - 1; i >= 0; i--)
            {
                var p = _projects[i];
                p.DaysLeft--;
                if (p.DaysLeft > 0) continue;

                _built.Add(p.Id);
                done.Add(p.Id);
                _projects.RemoveAt(i);
            }
            // Детерминированный порядок сообщений: по идентификатору, а не по
            // тому, в каком порядке их заложили.
            done.Sort(StringComparer.Ordinal);
            return done;
        }

        internal bool TakeRaid(int today)
        {
            if (!_raidQueued) return false;
            _raidQueued = false;
            _lastRaidDay = today;
            return true;
        }

        internal int TakeSettlers(int today)
        {
            int n = _settlersQueued;
            _settlersQueued = 0;
            if (n > 0) _lastSettlersDay = today;
            return n;
        }

        internal int TakeArrivals()
        {
            int n = _arrivalsQueued;
            _arrivalsQueued = 0;
            return n;
        }

        /// <summary>
        /// Замки постов по зданиям: пост без своего здания закрыт, человек на
        /// него не встанет. Посты без здания (поля, разведпост) не трогаем.
        /// </summary>
        public void ApplyToSlots(BaseState state)
        {
            if (state == null) return;

            foreach (var def in DefaultBuildings.All())
            {
                if (string.IsNullOrEmpty(def.OpensSlotId)) continue;
                var slot = state.GetSlot(def.OpensSlotId);
                if (slot != null) slot.Unlocked = Has(def.Id);
            }
        }

        private Project FindProject(string id)
        {
            for (int i = 0; i < _projects.Count; i++)
                if (_projects[i].Id == id) return _projects[i];
            return null;
        }

        // ================= слепок =================

        // Формат без «;» и «=» — внешний слепок режет по ним.
        // b:<id>,<id>|p:<id>:<left>:<total>,...|r:<queued>:<lastDay>|s:<n>|a:<n>
        public string CaptureState()
        {
            var sb = new StringBuilder();

            var built = new List<string>(_built);
            built.Sort(StringComparer.Ordinal);
            sb.Append("b:").Append(string.Join(",", built.ToArray()));

            sb.Append("|p:");
            for (int i = 0; i < _projects.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var p = _projects[i];
                sb.Append(p.Id).Append(':').Append(p.DaysLeft.ToString(CultureInfo.InvariantCulture))
                  .Append(':').Append(p.TotalDays.ToString(CultureInfo.InvariantCulture));
            }

            sb.Append("|r:").Append(_raidQueued ? 1 : 0).Append(':')
              .Append(_lastRaidDay.ToString(CultureInfo.InvariantCulture));
            sb.Append("|s:").Append(_settlersQueued.ToString(CultureInfo.InvariantCulture))
              .Append(':').Append(_lastSettlersDay.ToString(CultureInfo.InvariantCulture));
            sb.Append("|a:").Append(_arrivalsQueued.ToString(CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        public void RestoreState(string blob)
        {
            _built.Clear();
            _projects.Clear();
            _raidQueued = false;
            _lastRaidDay = int.MinValue / 2;
            _settlersQueued = 0;
            _lastSettlersDay = int.MinValue / 2;
            _arrivalsQueued = 0;

            if (string.IsNullOrEmpty(blob)) return;

            foreach (var part in blob.Split('|'))
            {
                if (part.Length < 2 || part[1] != ':') continue;
                string body = part.Substring(2);

                switch (part[0])
                {
                    case 'b':
                        foreach (var id in body.Split(','))
                            if (id.Length > 0) _built.Add(id);
                        break;
                    case 'p':
                        foreach (var item in body.Split(','))
                        {
                            var f = item.Split(':');
                            if (f.Length < 3) continue;
                            _projects.Add(new Project { Id = f[0], DaysLeft = ParseInt(f[1]), TotalDays = ParseInt(f[2]) });
                        }
                        break;
                    case 'r':
                        var r = body.Split(':');
                        if (r.Length >= 2)
                        {
                            _raidQueued = ParseInt(r[0]) != 0;
                            _lastRaidDay = ParseInt(r[1]);
                        }
                        break;
                    case 's':
                        var sp = body.Split(':');
                        _settlersQueued = ParseInt(sp[0]);
                        if (sp.Length > 1) _lastSettlersDay = ParseInt(sp[1]);
                        break;
                    case 'a': _arrivalsQueued = ParseInt(body); break;
                }
            }
        }

        private static int ParseInt(string s)
        {
            int v;
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : 0;
        }
    }
}
