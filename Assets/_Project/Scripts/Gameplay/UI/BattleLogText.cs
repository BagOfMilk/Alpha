using System;
using System.Collections.Generic;
using System.Globalization;
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
                // Бій v2 (docs/COMBAT_V2.md §7.3): причина атаки — ціна в ОД і
                // напрямлене укриття цілі — суфіксом до рядка attack.*.
                // Порожній рядок, доки Core не пише ці args (§AddCombatV2HudKeys.
                // combat.log.suffix.*) — жодного сліду плейсхолдера в тексті.
                "ap_suffix", ApSuffix(Arg(a, "ap"), female),
                "cover_suffix", CoverSuffix(Arg(a, "cover"), female),
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
            string resolved;
            if (key != null && UkrainianText.Has(key, female)) resolved = UkrainianText.Get(key, female);
            else if (unit != null && !string.IsNullOrEmpty(unit.DisplayNameKey) && UkrainianText.Has(unit.DisplayNameKey, female))
                resolved = UkrainianText.Get(unit.DisplayNameKey, female);
            else if (unit != null && !string.IsNullOrEmpty(unit.DisplayNameKey) && string.Equals(unit.Side, "Player", StringComparison.Ordinal))
                resolved = unit.DisplayNameKey;
            else
                return UkrainianText.MissingMarker(key ?? unitId);

            return WithOrdinal(resolved, unit);
        }

        /// <summary>
        /// Бій v2 (docs/COMBAT_V2.md §7.1, аудит HUD п.2): два вороги з тим
        /// самим DisplayNameKey («Розвідник орди» двічі) нерозрізнювані в
        /// журналі й черзі ходу — <c>BattleUnitView.Ordinal</c> (0 — ім'я
        /// унікальне в бою; 1, 2, … — порядковий серед юнітів з тим самим
        /// іменем) додає римський номер, той самий принцип, що вже показує
        /// підпис на арені (<see cref="IBattleHudData.ResolveDisplayName"/>).
        /// </summary>
        private static string WithOrdinal(string name, BattleUnitView unit)
        {
            if (unit == null || unit.Ordinal <= 0) return name;
            return name + " " + RomanNumeral(unit.Ordinal);
        }

        /// <summary>Римська цифра 1..3999 — досить для будь-якої кількості дублікатів одного ворога в одному бою.</summary>
        public static string RomanNumeral(int n)
        {
            if (n <= 0) return n.ToString(CultureInfo.InvariantCulture);
            var values = new[] { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
            var symbols = new[] { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < values.Length && n > 0; i++)
                while (n >= values[i]) { sb.Append(symbols[i]); n -= values[i]; }
            return sb.ToString();
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
            bool female = false;
            switch (kind)
            {
                case BattleLogKind.Miss:
                    return new BattleFloatingSpec { UnitId = Arg(a, "targetId"), Text = UkrainianText.Get("ui.battle.float.miss", false), Kind = kind };
                case BattleLogKind.Graze:
                    return new BattleFloatingSpec { UnitId = Arg(a, "targetId"), Text = UkrainianText.Get("ui.battle.float.graze", false), Kind = kind };
                case BattleLogKind.Hit:
                    return new BattleFloatingSpec { UnitId = Arg(a, "targetId"), Text = UkrainianText.Get("ui.battle.float.hit", false), Kind = kind };
                case BattleLogKind.Crit:
                    return new BattleFloatingSpec { UnitId = Arg(a, "targetId"), Text = UkrainianText.Get("ui.battle.float.crit", false), Kind = kind, Big = true };
                case BattleLogKind.Damage:
                    return new BattleFloatingSpec { UnitId = Arg(a, "unitId"), Text = UkrainianText.Format("ui.battle.float.damage", false, "amount", Arg(a, "damage")), Kind = kind };
                case BattleLogKind.Heal:
                    return new BattleFloatingSpec { UnitId = Arg(a, "unitId"), Text = UkrainianText.Format("ui.battle.float.heal", false, "amount", Arg(a, "amount")), Kind = kind };
                case BattleLogKind.Status:
                    // Лише накладення нового стану — «спадає»/«знято» без напису
                    // (докучливо мигтить над юнітом щоразу, коли DoT просто цокає).
                    if (entry.Key != "combat.log.status.applied") return null;
                    return new BattleFloatingSpec
                    {
                        UnitId = Arg(a, "unitId"),
                        Text = UkrainianText.Format("ui.battle.float.status", female, "status", Content("combat.status.", Arg(a, "status"), female)),
                        Kind = kind
                    };
                case BattleLogKind.Ability:
                    return new BattleFloatingSpec
                    {
                        UnitId = Arg(a, "unitId"),
                        Text = UkrainianText.Format("ui.battle.float.ability", female, "ability", Content(null, Arg(a, "abilityId"), female)),
                        Kind = kind
                    };
                case BattleLogKind.Overwatch:
                    // «Дозор!» лише коли юніт ЗАЙМАЄ дозор або СТРІЛЯЄ з нього —
                    // зняття/втрата дозору (expired/lost.*) вже мають свій рядок
                    // журналу, другий спливаючий напис на те саме був би шумом.
                    if (entry.Key != "combat.log.overwatch.set" && entry.Key != "combat.log.overwatch.fired") return null;
                    return new BattleFloatingSpec { UnitId = Arg(a, "unitId"), Text = UkrainianText.Get("ui.battle.float.overwatch", false), Kind = kind };
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

        /// <summary>Бій v2 (§7.3): " · ціна N ОД" поруч із рядком атаки — порожньо, доки args["ap"] відсутній.</summary>
        private static string ApSuffix(string rawAp, bool female)
        {
            if (string.IsNullOrEmpty(rawAp)) return string.Empty;
            return UkrainianText.Format("combat.log.suffix.ap", female, "ap", rawAp);
        }

        /// <summary>Бій v2 (§7.3): ", укриття цілі: половинне/повне" — порожньо, доки args["cover"] відсутній або ціль без укриття ("None").</summary>
        private static string CoverSuffix(string rawCover, bool female)
        {
            if (string.IsNullOrEmpty(rawCover) || string.Equals(rawCover, "None", StringComparison.OrdinalIgnoreCase))
                return string.Empty;
            string label = UkrainianText.Get("combat.cover.label." + rawCover.ToLowerInvariant(), female);
            return UkrainianText.Format("combat.log.suffix.cover", female, "cover", label);
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
