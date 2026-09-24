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
        /// Idle-напарники без поста на добу 1 (Захар/Овсій/Гафія вже на трьох
        /// постах зі старту, і жоден генератор населення не заводить НОВИХ
        /// іменних Companion — R1/Поправка №5, каст фіксований).
        /// </summary>
        public static IReadOnlyDictionary<string, string> DefaultAssignments(RosterView roster)
        {
            var result = new Dictionary<string, string>();
            if (roster?.Companions == null) return result;

            var occupied = new HashSet<string>();
            foreach (var c in roster.Companions)
                if (!string.IsNullOrEmpty(c.AssignedSlotId)) occupied.Add(c.AssignedSlotId);

            foreach (var slotId in FirstHourWorld.Positions)
            {
                if (occupied.Contains(slotId)) continue;

                foreach (var c in roster.Companions)
                {
                    if (c.Status != CompanionStatus.Idle) continue;
                    if (!string.IsNullOrEmpty(c.AssignedSlotId)) continue;
                    if (result.ContainsKey(c.Id)) continue;

                    result[c.Id] = slotId;
                    occupied.Add(slotId);
                    break;
                }
            }

            return result;
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
}
