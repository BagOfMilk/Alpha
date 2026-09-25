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

        // ===================== Бій v2 (docs/COMBAT_V2.md §6, §7.4) =====================
        // Заготовки контракту: презентер (частина «3D») будує з них журнал і
        // спливаючі написи; частина «HUD» доводить тексти й кольори. Сигнатури
        // заморожені.

        /// <summary>Сенс рядка логу — для кольору.</summary>
        public static BattleLogKind KindOf(BattleLogLineView entry)
        {
            string key = entry?.Key;
            if (string.IsNullOrEmpty(key)) return BattleLogKind.Neutral;
            if (key == "combat.log.round") return BattleLogKind.Round;
            if (key == "combat.log.move") return BattleLogKind.Move;
            if (key == "combat.log.attack.miss") return BattleLogKind.Miss;
            if (key == "combat.log.attack.graze") return BattleLogKind.Graze;
            if (key == "combat.log.attack.hit") return BattleLogKind.Hit;
            if (key == "combat.log.attack.crit") return BattleLogKind.Crit;
            if (key == "combat.log.damage") return BattleLogKind.Damage;
            if (key == "combat.log.heal") return BattleLogKind.Heal;
            if (key.StartsWith("combat.log.status.", StringComparison.Ordinal)) return BattleLogKind.Status;
            if (key.StartsWith("combat.log.overwatch.", StringComparison.Ordinal)) return BattleLogKind.Overwatch;
            if (key == "combat.log.ability") return BattleLogKind.Ability;
            if (key == "combat.log.downed" || key == "combat.log.bleeding_out") return BattleLogKind.Downed;
            if (key == "combat.log.died") return BattleLogKind.Death;
            if (key == "combat.log.victory") return BattleLogKind.Victory;
            if (key == "combat.log.defeat") return BattleLogKind.Defeat;
            return BattleLogKind.Neutral;
        }

        /// <summary>Готовий рядок журналу з типом.</summary>
        public static BattleLogEntryUi Entry(BattleLogLineView entry, BattleView view, Gender protagonistGender)
        {
            if (entry == null) return null;
            Func<string, string> name = id => UnitName(view, id, protagonistGender);
            Func<string, bool> female = id => UnitIsFemale(id, protagonistGender);
            return new BattleLogEntryUi
            {
                Round = entry.Round,
                Text = Line(entry, view != null && view.IsHitRulePercent, name, female),
                Kind = KindOf(entry)
            };
        }

        /// <summary>Спливаючий напис для рядка логу (над ким і що); null — рядок без напису.</summary>
        public static BattleFloatingSpec Floating(BattleLogLineView entry, BattleView view, Gender protagonistGender)
        {
            if (entry == null || string.IsNullOrEmpty(entry.Key)) return null;
            var a = entry.Args;
            var kind = KindOf(entry);
            switch (kind)
            {
                case BattleLogKind.Miss:
                    return new BattleFloatingSpec { UnitId = Arg(a, "targetId"), Text = UkrainianText.Get("ui.battle.float.miss", false), Kind = kind };
                case BattleLogKind.Graze:
                    return new BattleFloatingSpec { UnitId = Arg(a, "targetId"), Text = UkrainianText.Get("ui.battle.float.graze", false), Kind = kind };
                case BattleLogKind.Crit:
                    return new BattleFloatingSpec { UnitId = Arg(a, "targetId"), Text = UkrainianText.Get("ui.battle.float.crit", false), Kind = kind, Big = true };
                case BattleLogKind.Damage:
                    return new BattleFloatingSpec { UnitId = Arg(a, "unitId"), Text = UkrainianText.Format("ui.battle.float.damage", false, "amount", Arg(a, "damage")), Kind = kind };
                case BattleLogKind.Downed:
                    return entry.Key == "combat.log.downed"
                        ? new BattleFloatingSpec { UnitId = Arg(a, "unitId"), Text = UkrainianText.Get("ui.battle.float.downed", false), Kind = kind, Big = true }
                        : null;
                case BattleLogKind.Death:
                    return new BattleFloatingSpec { UnitId = Arg(a, "unitId"), Text = UkrainianText.Get("ui.battle.float.died", false), Kind = kind, Big = true };
                default:
                    return null;
            }
        }

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
