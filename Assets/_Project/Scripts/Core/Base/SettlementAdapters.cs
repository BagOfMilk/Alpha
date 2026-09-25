using System;
using System.Collections.Generic;
using Game.Core.Balance;
using Game.Core.Characters;
using Game.Core.Characters.Scars;
using Game.Core.Checks;
using Game.Core.Stats;
using Game.Core.World;

namespace Game.Core.Base
{
    /// <summary>
    /// Міст між міським шаром і моделлю персонажа.
    ///
    /// Живе САМЕ ТУТ, а не в Game.Core.Checks — свідомо: міський шар знає
    /// лише рядковий SkillKey, а про атрибути, скіли й трейти не знає
    /// нічого. Перебудова моделі змінює цей файл і більше нічий.
    ///
    /// Відповідності «ключ міського шару → скіл» тут більше немає: вона живе
    /// в Skills.KeyId поруч із самим enum. Дві таблиці можуть розійтися, одна — ні.
    /// </summary>
    public sealed class CompanionActorAdapter : ISettlementActor
    {
        private readonly Companion _companion;
        private readonly BalanceConfig _balance;

        public CompanionActorAdapter(Companion companion, bool isProtagonist = false, BalanceConfig balance = null)
        {
            _companion = companion ?? throw new ArgumentNullException(nameof(companion));
            IsProtagonist = isProtagonist;
            _balance = balance;
        }

        public string Id => _companion.Id;
        public bool IsProtagonist { get; }

        // B4-аудит §4.5: Antagonist свідомо виключений із присутності (не «!= Dead») —
        // той, хто пішов в антагоністи, не кандидат ні на перевірку, ні на пост.
        public bool IsPresentInSettlement =>
            _companion.Status != CompanionStatus.OnMission &&
            _companion.Status != CompanionStatus.Dead &&
            _companion.Status != CompanionStatus.Antagonist;

        public string HeldPositionId => _companion.AssignedSlotId;

        /// <summary>
        /// G16 (GDD:98): соц-перевірки додають контекстний атрибут підходу
        /// поверх голого скіла — «Залякати → Воля» (той самий атрибут, що й
        /// опір станам, див. AttributeType.Will), «Переконати →
        /// Кмітливість», «Торгівля → Кмітливість». Утилітарні (Neutral) перевірки
        /// атрибут не додають — вилазка і доповіді з постів від цього не
        /// зсуваються.
        /// </summary>
        public int GetCheckValue(SkillKey skill, ApproachForm approach = ApproachForm.Neutral)
        {
            var s = Resolve(skill);
            if (s == SkillType.None) return 0;

            int value = _companion.Skill(s);
            var attribute = ContextAttributeFor(approach);
            if (attribute != AttributeType.None) value += _companion.Attribute(attribute);
            return value;
        }

        private static AttributeType ContextAttributeFor(ApproachForm approach)
        {
            switch (approach)
            {
                case ApproachForm.Intimidate: return AttributeType.Will;
                case ApproachForm.Persuade: return AttributeType.Wits;
                case ApproachForm.Trade: return AttributeType.Wits;
                default: return AttributeType.None;
            }
        }

        /// <summary>
        /// Усе, що лягає поверх голого скіла: трейти, шрами, перки.
        ///
        /// Рахується як «резолвнуте мінус база», а не сумуванням потрібних
        /// модифікаторів вручну. Це рівно той поділ, при якому
        /// CheckResolver складає GetCheckValue + GetTraitModifier і отримує
        /// резолвнуте значення без подвійного рахунку — за побудовою, а не за
        /// домовленістю.
        /// </summary>
        public int GetTraitModifier(SkillKey skill)
        {
            var s = Resolve(skill);
            if (s == SkillType.None) return 0;
            return _companion.Resolve(_balance).Skill(s) - _companion.Skill(s);
        }

        private static SkillType Resolve(SkillKey skill)
            => skill.IsNone ? SkillType.None : Skills.FromKeyId(skill.Id);

        internal Companion Companion => _companion;
    }

    /// <summary>Погляд міського шару на ростер.</summary>
    public sealed class RosterAdapter : IRosterView, ICasualtySink, Game.Core.Loop.IStateBlob
    {
        private readonly Roster _roster;
        private readonly string _protagonistId;
        private readonly BalanceConfig _balance;
        private readonly List<ISettlementActor> _buffer = new List<ISettlementActor>();

        /// <summary>
        /// Хто йде у вилазку. Тільки для симуляційного харнеса: у самій
        /// грі партію задаватиме екран зборів, якого поки немає.
        /// </summary>
        internal List<Companion> PartyForSim { get; set; }

        /// <summary>
        /// Ростер у зліпок (Поправка №5.6 п. 4): хто живий, у якому стані і на
        /// якому посту. Без цього завантаження повертало людей «як на старті», і
        /// обіцянка «продовження невідрізнене від безперервного» не виконувалась.
        ///
        /// Пишуться лише змінювані поля. Імена, стати й картки приходять із
        /// контенту: дублювати їх у сейві означає одного разу розійтися з ним.
        /// </summary>
        public string CaptureState()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var c in _roster.All)
            {
                if (sb.Length > 0) sb.Append(',');
                sb.Append(c.Id).Append('>')
                  .Append((int)c.Status).Append('>')
                  .Append(c.AssignedSlotId ?? "").Append('>')
                  .Append(c.InjuryPoints.ToString("R", System.Globalization.CultureInfo.InvariantCulture))
                  // B4/R2: Лояльність — п'яте поле, додане адитивно в кінець
                  // запису (§4.8 R13), щоб старі зліпки без нього так само читались
                  // (RestoreState нижче толерантна до довжини < 5).
                  .Append('>').Append(c.Loyalty.ToString(System.Globalization.CultureInfo.InvariantCulture))
                  // Шосте поле — шрами (id через '+'). Раніше вічний трек не
                  // потрапляв у зліпок зовсім: після «Продовжити» шрами зникали
                  // разом зі своїми модифікаторами статів (дебаг 25.09.2026).
                  .Append('>').Append(ScarIds(c));
            }
            return sb.ToString();
        }

        public void RestoreState(string blob)
        {
            if (string.IsNullOrEmpty(blob)) return;
            foreach (var entry in blob.Split(','))
            {
                var f = entry.Split('>');
                if (f.Length < 4) continue;
                var c = _roster.Get(f[0]);
                if (c == null) continue;

                int status;
                if (int.TryParse(f[1], out status)) c.Status = (CompanionStatus)status;
                c.RestoreAssignmentForSave(string.IsNullOrEmpty(f[2]) ? null : f[2]);

                double injury;
                if (double.TryParse(f[3], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out injury))
                    c.InjuryPoints = injury;

                // B4/R2: п'яте поле — не в усіх старих зліпків є, тому
                // не в загальній "f.Length < 4 continue" вище, а окремою толерантною перевіркою.
                if (f.Length >= 5)
                {
                    int loyalty;
                    if (int.TryParse(f[4], System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture, out loyalty))
                        c.RestoreLoyaltyForSave(loyalty);
                }

                // Шрами — шосте поле; старі зліпки без нього лишають трек як є.
                if (f.Length >= 6)
                    c.Scars.RestoreFromSave(ScarsFromIds(f[5]));
            }
        }

        private static string ScarIds(Companion c)
        {
            var ids = new List<string>();
            foreach (var scar in c.Scars.Scars) ids.Add(scar.Id);
            return string.Join("+", ids);
        }

        /// <summary>Визначення шрамів — статичний контент (<see cref="DefaultScars"/>), у зліпку лише id.</summary>
        private static List<ScarDefinition> ScarsFromIds(string field)
        {
            var scars = new List<ScarDefinition>();
            if (string.IsNullOrEmpty(field)) return scars;
            var catalog = DefaultScars.All();
            foreach (var id in field.Split('+'))
                for (int i = 0; i < catalog.Count; i++)
                    if (catalog[i].Id == id) { scars.Add(catalog[i]); break; }
            return scars;
        }
        public RosterAdapter(Roster roster, string protagonistId = null, BalanceConfig balance = null)
        {
            _roster = roster ?? throw new ArgumentNullException(nameof(roster));
            _protagonistId = protagonistId;
            _balance = balance;
        }

        public IReadOnlyList<ISettlementActor> PresentActors
        {
            get
            {
                _buffer.Clear();
                var all = _roster.All;
                for (int i = 0; i < all.Count; i++)
                {
                    var actor = new CompanionActorAdapter(all[i], IsProtagonist(all[i].Id), _balance);
                    if (actor.IsPresentInSettlement) _buffer.Add(actor);
                }
                return _buffer;
            }
        }

        public ISettlementActor Protagonist
        {
            get
            {
                if (string.IsNullOrEmpty(_protagonistId)) return null;
                var c = _roster.Get(_protagonistId);
                return c == null ? null : new CompanionActorAdapter(c, true, _balance);
            }
        }

        /// <summary>
        /// Кого криза взагалі може торкнутися. Протагоніст виключений (US-4.4),
        /// мертві виключені. Список відсортований — вибір жертви зобов'язаний бути
        /// відтворюваним.
        /// </summary>
        public IReadOnlyList<string> KillableActorIds
        {
            get
            {
                var ids = new List<string>();
                var all = _roster.All;
                for (int i = 0; i < all.Count; i++)
                {
                    var c = all[i];
                    if (c.IsDead || IsProtagonist(c.Id)) continue;
                    if (c.Status == CompanionStatus.OnMission) continue;
                    // B4-аудит §4.5: антагоніст (незворотно пішов, див. enum)
                    // не жертва звичайної кризи — інакше Kill/Wound нижче
                    // мовчки затирають його статус ще до фіналу (R8).
                    if (c.Status == CompanionStatus.Antagonist) continue;
                    ids.Add(c.Id);
                }
                ids.Sort(StringComparer.Ordinal);
                return ids;
            }
        }

        public string ActorOnPosition(string positionId)
        {
            if (string.IsNullOrEmpty(positionId)) return null;
            var all = _roster.All;
            for (int i = 0; i < all.Count; i++)
                if (!all[i].IsDead &&
                    string.Equals(all[i].AssignedSlotId, positionId, StringComparison.Ordinal))
                    return all[i].Id;
            return null;
        }

        public void Kill(string actorId)
        {
            var c = _roster.Get(actorId);
            // B4-аудит §4.5: антагоніст незворотний — звичайний Kill його не чіпає.
            if (c == null || IsProtagonist(actorId) || c.Status == CompanionStatus.Antagonist) return;
            c.MarkDead();
        }

        /// <summary>
        /// Єдина точка поранення (R16/R5, закриває G10): інциденти сьогодні не
        /// знають тіра рани і кличуть її без третього аргументу (Light за
        /// замовчуванням — шрам не належиться, рівно та сама поведінка, що була до
        /// цього пакета). Вилазка ранить через <c>ExpeditionRunner.Complete</c> —
        /// друга, окрема точка входу з тим самим ім'ям правила всередині
        /// (<c>DefaultScars.TryGrant</c>), а не дублювальна копія. Бій (коли
        /// з'явиться) зобов'язаний ранити ТІЛЬКИ звідси, вже передаючи справжній тір.
        /// </summary>
        public void Wound(string actorId, double injuryPoints, WoundTier tier = WoundTier.Light)
        {
            WoundReporting(actorId, injuryPoints, tier);
        }

        /// <summary>
        /// Той самий шлях ранення, що і <see cref="Wound"/> (ICasualtySink) —
        /// винесений окремим методом (а не зміною сигнатури інтерфейсного
        /// Wound), щоб не ламати ICasualtySink (World/IncidentResolver) і
        /// PassVanguardOutcome, яким дарований шрам не потрібен. Повертає
        /// дарований <see cref="Characters.Scars.ScarDefinition"/> (або null) —
        /// major-фікс ревью: без цього виклик GameSession не мав звідки
        /// дізнатись, що шрам дарований, і подія "scar.granted" (§2 №23)
        /// не могла піти в DayLog з жодної з трьох реальних точок ранення.
        /// </summary>
        public Characters.Scars.ScarDefinition WoundReporting(string actorId, double injuryPoints, WoundTier tier = WoundTier.Light)
        {
            var c = _roster.Get(actorId);
            // B4-аудит §4.5: антагоніст незворотний — рана не затирає його статус.
            if (c == null || c.IsDead || c.Status == CompanionStatus.Antagonist) return null;
            c.InjuryPoints += injuryPoints;
            if (c.Status != CompanionStatus.OnMission)
                c.Status = CompanionStatus.Injured;
            Characters.Scars.ScarDefinition granted;
            Characters.Scars.DefaultScars.TryGrant(c, tier, out granted);
            return granted;
        }

        private bool IsProtagonist(string id) =>
            !string.IsNullOrEmpty(_protagonistId) &&
            string.Equals(id, _protagonistId, StringComparison.Ordinal);
    }

    /// <summary>
    /// Облік повторних звернень за темами. Третій підхід до однієї теми за тиждень
    /// дорожчий за перший — захист від спаму без жодної випадковості.
    /// </summary>
    public sealed class RepeatTracker : IRepeatTracker, Game.Core.Loop.IStateBlob
    {
        private readonly Dictionary<string, List<int>> _byTopic = new Dictionary<string, List<int>>();

        /// <summary>
        /// Без цього штраф за повтори обнулявся б кожним завантаженням, і сейв
        /// ставав би способом зняти покарання.
        /// </summary>
        public string CaptureState()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var pair in _byTopic)
            {
                if (pair.Value.Count == 0) continue;
                if (sb.Length > 0) sb.Append('~');
                sb.Append(pair.Key).Append('#');
                for (int i = 0; i < pair.Value.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(pair.Value[i]);
                }
            }
            return sb.ToString();
        }

        public void RestoreState(string blob)
        {
            _byTopic.Clear();
            if (string.IsNullOrEmpty(blob)) return;

            foreach (var entry in blob.Split('~'))
            {
                int hash = entry.IndexOf('#');
                if (hash <= 0) continue;

                var days = new List<int>();
                foreach (var d in entry.Substring(hash + 1).Split(','))
                {
                    int v;
                    if (int.TryParse(d, out v)) days.Add(v);
                }
                _byTopic[entry.Substring(0, hash)] = days;
            }
        }

        public int AttemptsInWindow(string topicId, int day, int windowDays)
        {
            if (string.IsNullOrEmpty(topicId) || !_byTopic.TryGetValue(topicId, out var days)) return 0;

            int count = 0;
            for (int i = 0; i < days.Count; i++)
                if (day - days[i] < windowDays) count++;
            return count;
        }

        public void Register(string topicId, int day)
        {
            if (string.IsNullOrEmpty(topicId)) return;
            if (!_byTopic.TryGetValue(topicId, out var days))
            {
                days = new List<int>();
                _byTopic[topicId] = days;
            }
            days.Add(day);
        }
    }
}
