using System;
using System.Collections.Generic;

namespace Game.Core.Combat
{
    /// <summary>
    /// Боевой ИИ: достойный, ЧИТАЕМЫЙ соперник — не оптимальный. Utility-скоринг:
    /// цель по ожидаемому урону × шансу (+добивание, +Метка), позиция по роли
    /// (клинч-роли сближаются, стрелки держат оптимал и укрытие), способности из
    /// общего пула по простым приоритетам (рывок вне контакта → свежий статус →
    /// усиленный залп). Side-агностичен: тем же кодом ходят враги, босс-перебежчик
    /// и авто-союзники (автобой). Детерминирован при данном CombatState — вся
    /// случайность идёт через внедрённый IDiceRoller, а не через сам ИИ.
    /// </summary>
    public static class CombatAi
    {
        // Пороги поведения (осознанно НЕ в BalanceConfig: это характер ИИ, не баланс игры).
        private const int MinHitToShoot = 25;        // ниже — сначала ищем позицию
        private const int GoodHitForAbility = 50;    // усиленный залп только при уверенном шансе
        private const double CoverHalfValue = 1.5;   // ценность укрытия в очках позиции
        private const double CoverFullValue = 3.0;
        private const double NoLosPenalty = 4.0;     // прятаться ОТ боя ИИ не должен
        private const double MoveApValue = 0.25;     // цена потраченного на движение AP

        /// <summary>Ведёт ход ТЕКУЩЕГО юнита целиком (до EndTurn). Зови, когда ходит ИИ.</summary>
        public static void TakeTurn(CombatState cs)
        {
            if (cs == null || cs.Outcome != CombatOutcome.Ongoing) return;
            var unit = cs.Current;
            if (unit == null || !unit.IsActive) { cs.EndTurn(); return; }

            int guard = 16; // страховка от зацикливания
            while (cs.Outcome == CombatOutcome.Ongoing && cs.Current == unit && unit.IsActive && guard-- > 0)
            {
                if (!TryAct(cs, unit)) break;
            }
            if (cs.Outcome == CombatOutcome.Ongoing && cs.Current == unit) cs.EndTurn();
        }

        /// <summary>
        /// Прогоняет бой ИИ-против-ИИ до исхода (автобой — обе стороны играет
        /// ИИ). turnBudget — внешняя страховка сверх внутреннего предела раундов
        /// CombatState (Balance.Combat.RoundCap): по умолчанию (null) считается
        /// от реального размера боя (RoundCap × число юнитов + запас), а не
        /// фиксированной константой — иначе достаточно большой ростер (по этой
        /// же причине хватало и не такого уж большого: раунд у TurnSystem
        /// считается один на ПОЛНЫЙ проход очереди инициативы, т.е. ~ЧислоЮнитов
        /// вызовов TakeTurn на раунд) исчерпывал бы старую страховку (400) РАНЬШЕ,
        /// чем сработает RoundCap внутри CombatState. Независимо от бюджета —
        /// своего или переданного вызывающим — завершаемость гарантирована
        /// БЕЗУСЛОВНО: если по выходу из цикла бой всё ещё Ongoing,
        /// CombatState.ForceDraw закрывает его сам. AutoResolve никогда не
        /// возвращает управление с Outcome == Ongoing.
        /// </summary>
        public static void AutoResolve(CombatState cs, int? turnBudget = null)
        {
            if (cs == null) return;

            int budget = turnBudget ?? (cs.Balance.Combat.RoundCap * Math.Max(1, cs.Units.Count) + cs.Units.Count + 4);
            while (cs.Outcome == CombatOutcome.Ongoing && budget-- > 0)
                TakeTurn(cs);

            cs.ForceDraw("исчерпан внешний бюджет ходов автобоя (AutoResolve)");
        }

        // ---- Один осмысленный шаг хода. false — юниту больше нечего делать. ----
        private static bool TryAct(CombatState cs, CombatUnit unit)
        {
            // 1. Медик спасает даун-союзника: рядом — стабилизация, далеко — идём к нему.
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

            // 2. Подлатать тяжело раненного союзника рядом (Перевязка и т.п.).
            var heal = FirstUsableOfKind(unit, AbilityEffectKind.Heal);
            if (heal != null)
            {
                var wounded = MostWoundedAllyInRange(cs, unit, heal.Range);
                if (wounded != null
                    && cs.UseAbility(heal.Id, wounded == unit ? null : wounded.Id) == CombatActionResult.Success)
                    return true;
            }

            // 3. Цель по скорингу.
            var target = PickTarget(cs, unit);
            if (target == null) return false;

            // 4. Рывок: клинч-юнит вне контакта сокращает дистанцию способностью.
            if (unit.Weapon != null && unit.Weapon.IsMelee
                && GridPos.Chebyshev(unit.Pos, target.Pos) > unit.Weapon.OptimalRange)
            {
                var lunge = FirstUsableOfKind(unit, AbilityEffectKind.LungeToTarget);
                if (lunge != null && cs.UseAbility(lunge.Id, target.Id) == CombatActionResult.Success)
                    return true;
            }

            // 5. Свежий статус на цель: только если цель ещё не под ним.
            var statusAbility = PickStatusAbility(cs, unit, target);
            if (statusAbility != null && cs.UseAbility(statusAbility.Id, target.Id) == CombatActionResult.Success)
                return true;

            int chance = cs.HitChancePreview(unit, target);
            bool inMelee = unit.Weapon != null && unit.Weapon.IsMelee
                           && GridPos.Chebyshev(unit.Pos, target.Pos) <= unit.Weapon.OptimalRange;

            // 6. Усиленный залп (Очередь и т.п.) при уверенном шансе.
            if (chance >= GoodHitForAbility || inMelee)
            {
                var volley = PickVolleyAbility(unit);
                if (volley != null && cs.UseAbility(volley.Id, target.Id) == CombatActionResult.Success)
                    return true;
            }

            // 7. Обычная атака, если шанс приемлем (или мы уже в клинче).
            if (unit.Weapon != null && (inMelee || (!unit.Weapon.IsMelee && chance >= MinHitToShoot)))
            {
                bool strike = unit.StrikeMeter >= cs.Balance.Combat.StrikeGuaranteeAt;
                var result = cs.Attack(target.Id, strike);
                if (result == CombatActionResult.Success) return true;
            }

            // 8. Позицию можно улучшить? (сближение/оптимал/укрытие по роли)
            if (TryImprovePosition(cs, unit, target)) return true;

            // 9. Стрелять не по кому, а на выстрел AP хватает — дозор в сторону
            //    цели. Симметрия: игрок, идущий на такого врага, рискует так
            //    же, как враг, идущий на дозор игрока. Ближний бой в дозор не
            //    встаёт — его дело сближаться.
            if (unit.Weapon != null && !unit.Weapon.IsMelee && unit.Ap >= unit.Weapon.ApCost
                && cs.Overwatch(target.Pos) == CombatActionResult.Success)
                return true;

            return false; // ничего полезного — конец хода
        }

        // ---- Скоринг цели: ожидаемый урон × шанс + добивание + Метка. ----
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
                    score = -GridPos.Chebyshev(unit.Pos, t.Pos); // безоружный: просто ближайший
                }
                else
                {
                    double expected = AvgDamage(unit.Weapon) * cs.HitChancePreview(unit, t) / 100.0;
                    score = expected
                            + (t.Hp <= expected ? 5.0 : 0.0)                    // шанс снять цель
                            + (t.HasStatus(StatusType.Marked) ? 2.0 : 0.0)      // фокус по Метке
                            - GridPos.Chebyshev(unit.Pos, t.Pos) * 0.05;        // тай-брейк: ближние
                    if (!unit.Weapon.IsMelee && !LineOfSight.HasLine(cs.Map, unit.Pos, t.Pos))
                        score -= 8.0;
                }
                if (score > bestScore) { bestScore = score; best = t; }
            }
            return best;
        }

        // ---- Позиция: роль задаёт, чего юнит хочет от тайла. ----
        private static bool TryImprovePosition(CombatState cs, CombatUnit unit, CombatUnit target)
        {
            double current = TileScore(cs, unit, unit.Pos, target);
            GridPos? bestPos = null;
            double bestScore = current + 0.5; // двигаться только ради ощутимого выигрыша

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
                return -dist * 2.0; // клинч-роли сближаются, укрытия им безразличны

            int optimal = unit.Weapon != null ? unit.Weapon.OptimalRange : 5;
            double score = -Math.Abs(dist - optimal);

            double coverWeight = unit.Profile.Role == EnemyRole.Skirmisher ? 1.5 : 1.0;
            var cover = cs.Map.CoverAgainst(pos, target.Pos);
            if (cover == CoverType.Full) score += CoverFullValue * coverWeight;
            else if (cover == CoverType.Half) score += CoverHalfValue * coverWeight;

            if (!LineOfSight.HasLine(cs.Map, pos, target.Pos)) score -= NoLosPenalty;
            return score;
        }

        // ---- Способности ----
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
            double bestFraction = 0.5; // лечим только тех, кому реально плохо (≤ 50%)
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

        /// <summary>Шаг в достижимый тайл, строго сокращающий дистанцию до цели (анти-осцилляция).</summary>
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
