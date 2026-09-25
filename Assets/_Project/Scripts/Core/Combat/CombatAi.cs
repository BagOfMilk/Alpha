using System;
using System.Collections.Generic;

namespace Game.Core.Combat
{
    /// <summary>
    /// Бойовий ШІ: гідний, ЧИТАБЕЛЬНИЙ суперник — не оптимальний. Utility-скоринг:
    /// ціль за очікуваним уроном × шансом (+добивання, +Мітка), позиція за роллю
    /// (клінч-ролі зближуються, стрільці тримають оптимал і укриття), здібності зі
    /// спільного пулу за простими пріоритетами (ривок поза контактом → свіжий статус →
    /// посилений залп). Side-агностичний: тим самим кодом ходять вороги, бос-перебіжчик
    /// і авто-союзники (автобій). Детермінований за даного CombatState — уся
    /// випадковість іде через впроваджений IDiceRoller, а не через сам ШІ.
    /// </summary>
    public static class CombatAi
    {
        // Пороги поведінки (свідомо НЕ в BalanceConfig: це характер ШІ, не баланс гри).
        private const int MinHitToShoot = 25;        // нижче — спершу шукаємо позицію
        private const int GoodHitForAbility = 50;    // посилений залп тільки при впевненому шансі
        private const double CoverHalfValue = 1.5;   // цінність укриття в очках позиції
        private const double CoverFullValue = 3.0;
        private const double NoLosPenalty = 4.0;     // ховатись ВІД бою ШІ не повинен
        private const double MoveApValue = 0.25;     // ціна витраченого на рух AP

        /// <summary>Веде хід ПОТОЧНОГО юніта цілком (до EndTurn). Клич, коли ходить ШІ.</summary>
        public static void TakeTurn(CombatState cs)
        {
            if (cs == null || cs.Outcome != CombatOutcome.Ongoing) return;
            var unit = cs.Current;
            if (unit == null || !unit.IsActive) { cs.EndTurn(); return; }

            int guard = 16; // страховка від зациклення
            while (cs.Outcome == CombatOutcome.Ongoing && cs.Current == unit && unit.IsActive && guard-- > 0)
            {
                if (!TryAct(cs, unit)) break;
            }
            if (cs.Outcome == CombatOutcome.Ongoing && cs.Current == unit) cs.EndTurn();
        }

        /// <summary>
        /// Проганяє бій ШІ-проти-ШІ до наслідку (автобій — обидві сторони грає
        /// ШІ). turnBudget — зовнішня страховка понад внутрішню межу раундів
        /// CombatState (Balance.Combat.RoundCap): за замовчуванням (null) рахується
        /// від реального розміру бою (RoundCap × кількість юнітів + запас), а не
        /// фіксованою константою — інакше достатньо великий ростер (з тієї ж
        /// причини вистачало і не такого вже великого: раунд у TurnSystem
        /// рахується один на ПОВНИЙ прохід черги ініціативи, тобто ~КількістьЮнітів
        /// викликів TakeTurn на раунд) вичерпував би стару страховку (400) РАНІШЕ,
        /// ніж спрацює RoundCap усередині CombatState. Незалежно від бюджету —
        /// свого чи переданого викликачем — завершуваність гарантована
        /// БЕЗУМОВНО: якщо на виході з циклу бій усе ще Ongoing,
        /// CombatState.ForceDraw закриває його сам. AutoResolve ніколи не
        /// повертає керування з Outcome == Ongoing.
        /// </summary>
        public static void AutoResolve(CombatState cs, int? turnBudget = null)
        {
            if (cs == null) return;

            int budget = turnBudget ?? (cs.Balance.Combat.RoundCap * Math.Max(1, cs.Units.Count) + cs.Units.Count + 4);
            while (cs.Outcome == CombatOutcome.Ongoing && budget-- > 0)
                TakeTurn(cs);

            cs.ForceDraw("исчерпан внешний бюджет ходов автобоя (AutoResolve)");
        }

        // ---- Один осмислений крок ходу. false — юніту більше нема чого робити. ----
        /// <summary>
        /// Публічний (дебаг §6.1 №32, 24.09.2026): раніше приватний, доступний
        /// тільки зсередини <see cref="TakeTurn"/>, яка крутить УВЕСЬ хід юніта
        /// одним викликом і не віддає керування між окремими діями —
        /// зовнішній спостерігач (тест/UI) не може зазирнути в BattleView МІЖ
        /// двома ударами одного ходу, а саме там короткоживучий статус
        /// (накладений, ціль ще жива) видно довше однієї атаки. Публічний TryAct
        /// дає GameSession.CombatAiStepOneAction() ту саму тактику ШІ поштучно.
        /// </summary>
        public static bool TryAct(CombatState cs, CombatUnit unit)
        {
            // 1. Медик рятує даун-союзника: поруч — стабілізація, далеко — йдемо до нього.
            if (unit.Profile.MedicineSkill >= 1)
            {
                var downed = Nearest(cs, unit, sameSide: true, state: UnitLifeState.Downed);
                if (downed != null)
                {
                    if (GridPos.Chebyshev(unit.Pos, downed.Pos) <= 1)
                    {
                        if (cs.Stabilize(downed.Id) == CombatActionResult.Success) return true;
                    }
                    else if (TryStepToward(cs, unit, downed.Pos)) return true;
                }
            }

            // 2. Підлатати тяжко пораненого союзника поруч (Перев'язка тощо).
            var heal = FirstUsableOfKind(unit, AbilityEffectKind.Heal);
            if (heal != null)
            {
                var wounded = MostWoundedAllyInRange(cs, unit, heal.Range);
                if (wounded != null
                    && cs.UseAbility(heal.Id, wounded == unit ? null : wounded.Id) == CombatActionResult.Success)
                    return true;
            }

            // 3. Ціль за скорингом.
            var target = PickTarget(cs, unit);
            if (target == null) return false;

            // 4. Ривок: клінч-юніт поза контактом скорочує дистанцію здібністю.
            if (unit.Weapon != null && unit.Weapon.IsMelee
                && GridPos.Chebyshev(unit.Pos, target.Pos) > unit.Weapon.OptimalRange)
            {
                var lunge = FirstUsableOfKind(unit, AbilityEffectKind.LungeToTarget);
                if (lunge != null && cs.UseAbility(lunge.Id, target.Id) == CombatActionResult.Success)
                    return true;
            }

            // 5. Свіжий статус на ціль: тільки якщо ціль ще не під ним.
            var statusAbility = PickStatusAbility(cs, unit, target);
            if (statusAbility != null && cs.UseAbility(statusAbility.Id, target.Id) == CombatActionResult.Success)
                return true;

            int chance = cs.HitChancePreview(unit, target);
            bool inMelee = unit.Weapon != null && unit.Weapon.IsMelee
                           && GridPos.Chebyshev(unit.Pos, target.Pos) <= unit.Weapon.OptimalRange;

            // 6. Посилений залп (Черга тощо) при впевненому шансі.
            if (chance >= GoodHitForAbility || inMelee)
            {
                var volley = PickVolleyAbility(unit);
                if (volley != null && cs.UseAbility(volley.Id, target.Id) == CombatActionResult.Success)
                    return true;
            }

            // 7. Звичайна атака, якщо шанс прийнятний (або ми вже в клінчі).
            if (unit.Weapon != null && (inMelee || (!unit.Weapon.IsMelee && chance >= MinHitToShoot)))
            {
                bool strike = unit.StrikeMeter >= cs.Balance.Combat.StrikeGuaranteeAt;
                var result = cs.Attack(target.Id, strike);
                if (result == CombatActionResult.Success) return true;
            }

            // 8. Позицію можна покращити? (зближення/оптимал/укриття за роллю)
            if (TryImprovePosition(cs, unit, target)) return true;

            // 9. Стріляти немає по кому, а на постріл AP вистачає — дозор у бік
            //    цілі. Симетрія: гравець, що йде на такого ворога, ризикує так
            //    само, як ворог, що йде на дозор гравця. Ближній бій у дозор не
            //    стає — його справа зближуватись.
            if (unit.Weapon != null && !unit.Weapon.IsMelee && unit.Ap >= unit.Weapon.ApCost
                && cs.Overwatch(target.Pos) == CombatActionResult.Success)
                return true;

            return false; // нічого корисного — кінець ходу
        }

        // ---- Скоринг цілі: очікуваний урон × шанс + добивання + Мітка. ----
        private static CombatUnit PickTarget(CombatState cs, CombatUnit unit)
        {
            CombatUnit best = null;
            double bestScore = double.MinValue;
            foreach (var t in cs.Units)
            {
                if (t.Side == unit.Side || !t.IsActive) continue;

                double score;
                if (unit.Weapon == null)
                {
                    score = -GridPos.Chebyshev(unit.Pos, t.Pos); // беззбройний: просто найближчий
                }
                else
                {
                    double expected = AvgDamage(unit.Weapon) * cs.HitChancePreview(unit, t) / 100.0;
                    score = expected
                            + (t.Hp <= expected ? 5.0 : 0.0)                    // шанс зняти ціль
                            + (t.HasStatus(StatusType.Marked) ? 2.0 : 0.0)      // фокус за Міткою
                            - GridPos.Chebyshev(unit.Pos, t.Pos) * 0.05;        // тай-брейк: ближні
                    if (!unit.Weapon.IsMelee && !LineOfSight.HasLine(cs.Map, unit.Pos, t.Pos))
                        score -= 8.0;
                }
                if (score > bestScore) { bestScore = score; best = t; }
            }
            return best;
        }

        // ---- Позиція: роль задає, чого юніт хоче від тайла. ----
        private static bool TryImprovePosition(CombatState cs, CombatUnit unit, CombatUnit target)
        {
            double current = TileScore(cs, unit, unit.Pos, target);
            GridPos? bestPos = null;
            double bestScore = current + 0.5; // рухатись тільки заради відчутного виграшу

            foreach (var kv in cs.ReachableFor(unit))
            {
                double s = TileScore(cs, unit, kv.Key, target) - kv.Value * MoveApValue;
                if (s > bestScore) { bestScore = s; bestPos = kv.Key; }
            }

            return bestPos.HasValue && cs.Move(bestPos.Value) == CombatActionResult.Success;
        }

        private static double TileScore(CombatState cs, CombatUnit unit, GridPos pos, CombatUnit target)
        {
            int dist = GridPos.Chebyshev(pos, target.Pos);
            bool closeCombat = (unit.Weapon != null && unit.Weapon.IsMelee)
                               || unit.Profile.Role == EnemyRole.Tank
                               || unit.Profile.Role == EnemyRole.Breacher;

            if (closeCombat)
                return -dist * 2.0; // клінч-ролі зближуються, укриття їм байдужі

            int optimal = unit.Weapon != null ? unit.Weapon.OptimalRange : 5;
            double score = -Math.Abs(dist - optimal);

            double coverWeight = unit.Profile.Role == EnemyRole.Skirmisher ? 1.5 : 1.0;
            var cover = cs.Map.CoverAgainst(pos, target.Pos);
            if (cover == CoverType.Full) score += CoverFullValue * coverWeight;
            else if (cover == CoverType.Half) score += CoverHalfValue * coverWeight;

            if (!LineOfSight.HasLine(cs.Map, pos, target.Pos)) score -= NoLosPenalty;
            return score;
        }

        // ---- Здібності ----
        private static AbilityDefinition PickStatusAbility(CombatState cs, CombatUnit unit, CombatUnit target)
        {
            foreach (var a in unit.Abilities)
            {
                if (a.Targeting != AbilityTarget.Enemy) continue;
                if (unit.CooldownRemaining(a.Id) > 0 || unit.Ap < a.ApCost) continue;

                foreach (var fx in a.Effects)
                {
                    if (fx.Kind != AbilityEffectKind.ApplyStatus || fx.Status == StatusType.None) continue;
                    if (target.HasStatus(fx.Status)) continue;
                    if (!InAbilityRange(cs, unit, target, a)) continue;
                    return a;
                }
            }
            return null;
        }

        private static AbilityDefinition PickVolleyAbility(CombatUnit unit)
        {
            foreach (var a in unit.Abilities)
            {
                if (a.Targeting != AbilityTarget.Enemy) continue;
                if (unit.CooldownRemaining(a.Id) > 0 || unit.Ap < a.ApCost) continue;

                int weaponHits = 0;
                bool other = false;
                foreach (var fx in a.Effects)
                {
                    if (fx.Kind == AbilityEffectKind.WeaponAttack) weaponHits++;
                    else if (fx.Kind != AbilityEffectKind.RemoveStatus) other = true;
                }
                if (weaponHits >= 2 && !other) return a;
            }
            return null;
        }

        private static bool InAbilityRange(CombatState cs, CombatUnit unit, CombatUnit target, AbilityDefinition a)
        {
            if (GridPos.Chebyshev(unit.Pos, target.Pos) > a.Range) return false;
            if (a.RequiresLineOfSight && !LineOfSight.HasLine(cs.Map, unit.Pos, target.Pos)) return false;
            return true;
        }

        private static AbilityDefinition FirstUsableOfKind(CombatUnit u, AbilityEffectKind kind)
        {
            foreach (var a in u.Abilities)
            {
                if (u.CooldownRemaining(a.Id) > 0 || u.Ap < a.ApCost) continue;
                foreach (var fx in a.Effects)
                    if (fx.Kind == kind) return a;
            }
            return null;
        }

        private static CombatUnit MostWoundedAllyInRange(CombatState cs, CombatUnit from, int range)
        {
            CombatUnit best = null;
            double bestFraction = 0.5; // лікуємо тільки тих, кому реально погано (≤ 50%)
            foreach (var u in cs.Units)
            {
                if (u.Side != from.Side || !u.IsActive) continue;
                if (GridPos.Chebyshev(from.Pos, u.Pos) > range) continue;
                double fraction = u.Profile.MaxHp > 0 ? (double)u.Hp / u.Profile.MaxHp : 1.0;
                if (fraction <= bestFraction) { bestFraction = fraction; best = u; }
            }
            return best;
        }

        private static CombatUnit Nearest(CombatState cs, CombatUnit from, bool sameSide, UnitLifeState state)
        {
            CombatUnit best = null;
            int bestDist = int.MaxValue;
            foreach (var u in cs.Units)
            {
                if (u == from || u.LifeState != state) continue;
                if (sameSide != (u.Side == from.Side)) continue;
                int d = GridPos.Chebyshev(from.Pos, u.Pos);
                if (d < bestDist) { bestDist = d; best = u; }
            }
            return best;
        }

        /// <summary>Крок у досяжний тайл, що строго скорочує дистанцію до цілі (анти-осциляція).</summary>
        private static bool TryStepToward(CombatState cs, CombatUnit u, GridPos goal)
        {
            int curDist = GridPos.Chebyshev(u.Pos, goal);
            GridPos? best = null;
            int bestDist = curDist;
            int bestCost = int.MaxValue;
            foreach (var kv in cs.ReachableFor(u))
            {
                int d = GridPos.Chebyshev(kv.Key, goal);
                if (d < bestDist || (d == bestDist && d < curDist && kv.Value < bestCost))
                {
                    bestDist = d;
                    bestCost = kv.Value;
                    best = kv.Key;
                }
            }
            return best.HasValue && cs.Move(best.Value) == CombatActionResult.Success;
        }

        private static double AvgDamage(WeaponDefinition w)
            => w == null ? 0 : (w.DamageMin + w.DamageMax) / 2.0;
    }
}
