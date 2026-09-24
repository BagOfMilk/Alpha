using System;
using System.Collections.Generic;
using Game.Core.Characters.Creation;
using Game.Core.Session.Views;
using Game.Gameplay.Text;

namespace Game.Gameplay.UI
{
    /// <summary>
    /// Журнал бою словами (R7): <see cref="BattleView.Log"/> несе ключі
    /// <c>combat.log.*</c> з аргументами — id юнітів, числа, токени стану/типу
    /// шкоди/здібності, — а тут вони стають українськими рядками через
    /// <see cref="UkrainianText"/>. Раніше BattleView.Log ніс внутрішній трейс
    /// ядра (російський текст із сирими іменами enum), і фолбек-екран бою
    /// показував його гравцеві як є.
    ///
    /// Чистий C#, спільний для IMGUI-фолбеку (<see cref="BattleScreen"/>) і HUD
    /// арени (<c>BattleArenaController</c>, який підставляє власне
    /// розв'язання імен): лінтується і вкритий headless-тестами
    /// (<c>BattleLogTextTests</c>).
    ///
    /// Плейсхолдери таблиці: <c>{unit}</c>/<c>{target}</c> — імена,
    /// <c>{ability}</c>/<c>{status}</c>/<c>{damageType}</c> — перекладені
    /// токени, <c>{chance}</c> — показане число за правилом попадання
    /// (R1: відсоток чи поріг); решта аргументів (числа) — як є.
    /// </summary>
    public static class BattleLogText
    {
        /// <param name="unitName">Ім'я юніта за його id (як у <see cref="BattleUnitView.Id"/>).</param>
        /// <param name="unitIsFemale">Рід підмета рядка — обирає варіант <c>.m</c>/<c>.f</c>, коли він є.</param>
        public static string Line(BattleLogLineView entry, bool hitRulePercent,
                                  Func<string, string> unitName, Func<string, bool> unitIsFemale)
        {
            if (entry == null || string.IsNullOrEmpty(entry.Key)) return string.Empty;
            var a = entry.Args;
            string unitId = Arg(a, "unitId");
            bool female = !string.IsNullOrEmpty(unitId) && unitIsFemale != null && unitIsFemale(unitId);

            var pairs = new List<string>
            {
                "unit", Name(unitName, unitId),
                "target", Name(unitName, Arg(a, "targetId")),
                "ability", Content(null, Arg(a, "abilityId"), female),
                "status", Content("combat.status.", Arg(a, "status"), female),
                "damageType", Content("combat.damage_type.", Arg(a, "damageType"), female),
                "chance", Chance(Arg(a, "chance"), hitRulePercent, female),
            };
            if (a != null)
                foreach (var kv in a) { pairs.Add(kv.Key); pairs.Add(kv.Value); }

            return UkrainianText.Format(entry.Key, female, pairs.ToArray());
        }

        /// <summary>
        /// Останні <paramref name="max"/> рядків журналу, найновіший — першим
        /// (той самий порядок, що в стрічці подій хабу). Імена — за
        /// <see cref="UnitName"/>.
        /// </summary>
        public static List<string> RecentLines(BattleView view, int max, Gender protagonistGender)
        {
            var lines = new List<string>();
            if (view?.Log == null) return lines;

            Func<string, string> name = id => UnitName(view, id, protagonistGender);
            Func<string, bool> female = id => UnitIsFemale(id, protagonistGender);
            for (int i = view.Log.Count - 1; i >= 0 && lines.Count < max; i--)
            {
                string line = Line(view.Log[i], view.IsHitRulePercent, name, female);
                if (!string.IsNullOrEmpty(line)) lines.Add(line);
            }
            return lines;
        }

        /// <summary>
        /// Ім'я юніта бою за id: <c>u_&lt;id&gt;</c> і <c>defector_&lt;id&gt;</c> —
        /// <c>char.&lt;id&gt;</c>, ворог — <c>enemy.&lt;DisplayNameKey&gt;</c>. Юніт
        /// гравця без ключа (тренувальний бій: <c>trainee_1</c>) — його
        /// DisplayNameKey як є: там уже готовий український текст. Те саме
        /// правило, що в <c>BattleArenaController.ResolveDisplayNameInternal</c>.
        /// </summary>
        public static string UnitName(BattleView view, string unitId, Gender protagonistGender)
        {
            if (string.IsNullOrEmpty(unitId)) return UkrainianText.MissingMarker(null);

            var unit = FindUnit(view, unitId);
            bool female = UnitIsFemale(unitId, protagonistGender);
            string key = NameKey(unitId, unit);
            if (key != null && UkrainianText.Has(key, female)) return UkrainianText.Get(key, female);

            if (unit != null && !string.IsNullOrEmpty(unit.DisplayNameKey))
            {
                if (UkrainianText.Has(unit.DisplayNameKey, female)) return UkrainianText.Get(unit.DisplayNameKey, female);
                if (string.Equals(unit.Side, "Player", StringComparison.Ordinal)) return unit.DisplayNameKey;
            }
            return UkrainianText.MissingMarker(key ?? unitId);
        }

        /// <summary>Рід юніта бою: протагоніст — рід, обраний гравцем, решта — за фіксованим кастом (<see cref="ScreenText.SubjectGender"/>).</summary>
        public static bool UnitIsFemale(string unitId, Gender protagonistGender)
            => ScreenText.SubjectGender(BareId(unitId), protagonistGender) == Gender.Female;

        // ===================== допоміжне =====================

        private static string BareId(string unitId)
        {
            if (string.IsNullOrEmpty(unitId)) return unitId;
            if (unitId.StartsWith("u_", StringComparison.Ordinal)) return unitId.Substring(2);
            if (unitId.StartsWith("defector_", StringComparison.Ordinal)) return unitId.Substring(9);
            return unitId;
        }

        private static string NameKey(string unitId, BattleUnitView unit)
        {
            if (unitId.StartsWith("u_", StringComparison.Ordinal) || unitId.StartsWith("defector_", StringComparison.Ordinal))
                return "char." + BareId(unitId);
            if (unit != null && !string.Equals(unit.Side, "Player", StringComparison.Ordinal) &&
                !string.IsNullOrEmpty(unit.DisplayNameKey))
                return "enemy." + unit.DisplayNameKey;
            return null;
        }

        private static string Name(Func<string, string> unitName, string unitId)
        {
            if (string.IsNullOrEmpty(unitId)) return string.Empty;
            return unitName != null ? unitName(unitId) : UkrainianText.MissingMarker(unitId);
        }

        /// <summary>Перекладений токен: <c>prefix + id</c> (для здібності id — уже повний ключ). Невідомий — видима заглушка, не сирий токен.</summary>
        private static string Content(string prefix, string id, bool female)
        {
            if (string.IsNullOrEmpty(id)) return string.Empty;
            return UkrainianText.Get((prefix ?? string.Empty) + id, female);
        }

        private static string Chance(string raw, bool hitRulePercent, bool female)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;
            return UkrainianText.Format(hitRulePercent ? "combat.log.chance.percent" : "combat.log.chance.threshold",
                female, "value", raw);
        }

        private static BattleUnitView FindUnit(BattleView view, string unitId)
        {
            if (view?.Units == null) return null;
            foreach (var u in view.Units)
                if (string.Equals(u.Id, unitId, StringComparison.Ordinal)) return u;
            return null;
        }

        private static string Arg(IReadOnlyDictionary<string, string> args, string name)
        {
            if (args == null) return null;
            string v;
            return args.TryGetValue(name, out v) ? v : null;
        }
    }
}
