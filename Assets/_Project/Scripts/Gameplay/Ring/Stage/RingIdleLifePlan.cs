using System;

namespace Game.Gameplay.Ring.Stage
{
    /// <summary>Хто стоїть у петлі очікування: борець (Ring_Stance_Loop) чи рефері (Ref_Idle_Loop).</summary>
    public enum IdleLifeRole { Wrestler = 0, Referee = 1 }

    /// <summary>
    /// Розклад живих вставок у стійку (власник 14.11.2026: «добавь более живие айдл анимации когда просто стояние
    /// происходит, что бы чуть живее выглядело»; ПМ того ж дня — вставка лише у «вільне вікно» режисера). Чиста модель без
    /// рушія: на кожній межі петлі очікування вирішує, чи грати одну вставку і яку. Вставка — кліп рівно на один період
    /// петлі (tools/blender/ring_anims_idle.py), з вузла стійки у вузол стійки, тож шов невидимий.
    /// Правила:
    /// <list type="bullet">
    /// <item>не дозволено режисером (кіно фінішера, камерний план, лежить, больовий, за рингом) — нічого;</item>
    /// <item>до наступного біту менше, ніж вставка + <see cref="Margin"/>, — нічого (вставка не наступає на біт);</item>
    /// <item>між двома вставками — щонайменше одна звичайна петля; після трьох звичайних — вставка напевно (≈ кожні 5–10 с);</item>
    /// <item>той самий жест двічі поспіль — ніколи; вибір — за вагами ґіміка;</item>
    /// <item>детерміновано: хеш (актор, номер петлі), без жодного генератора випадковості — тур і повтор дають той самий рух.</item>
    /// </list>
    /// Читає <c>RingIdleLife</c>; охоронці — <c>RingIdleLifePlanTests</c>.
    /// </summary>
    public static class RingIdleLifePlan
    {
        public const float WrestlerLoop = 2.4f;
        public const float RefereeLoop = 3.2f;
        public const string WrestlerLoopClip = "Ring_Stance_Loop";
        public const string RefereeLoopClip = "Ref_Idle_Loop";

        /// <summary>Вставка закінчується щонайменше за стільки секунд до наступного біту.</summary>
        public const float Margin = 0.3f;

        /// <summary>Жести борця — порядок збігається зі стовпцями <see cref="GimmickWeights"/>.</summary>
        public static readonly string[] WrestlerClips =
        {
            "Stance_Life_Shake", "Stance_Life_Look_L", "Stance_Life_Look_R", "Stance_Life_Bounce", "Stance_Life_Neck",
            "Stance_Life_Wipe", "Stance_Life_Reset", "Stance_Life_ComeOn", "Stance_Life_Fists", "Stance_Life_Breath",
        };

        /// <summary>Жести рефері: погляд з пари не довше ~1 с (умова ПМ).</summary>
        public static readonly string[] RefereeClips = { "Ref_Life_Crouch", "Ref_Life_Cuffs", "Ref_Life_Clap" };

        /// <summary>
        /// Ваги жестів за ґіміком (рядок — <c>Gimmick</c> кар'єри: Monster, Flyer, Technician, Showman, Veteran, Hardcore,
        /// Rookie, Strongman). 0 — жест не пасує характеру (Літун не стискає кулаки, Силач не підстрибує).
        /// Стовпці: Shake, Look_L, Look_R, Bounce, Neck, Wipe, Reset, ComeOn, Fists, Breath.
        /// </summary>
        public static readonly int[][] GimmickWeights =
        {
            new[] { 1, 1, 1, 0, 3, 1, 2, 2, 3, 3 },   // Важковаговик: важкий подих, шия, кулаки
            new[] { 3, 2, 2, 4, 1, 1, 2, 1, 0, 1 },   // Літун: пружний, розім'ятий, дивиться на зал
            new[] { 2, 1, 1, 1, 2, 2, 3, 0, 1, 2 },   // Технік: переставляє стопи, зібраний
            new[] { 1, 4, 4, 2, 1, 1, 1, 4, 1, 1 },   // Шоумен: зал і «ну давай!»
            new[] { 1, 1, 1, 0, 3, 3, 2, 1, 1, 3 },   // Бувалий: піт, шия, подих
            new[] { 2, 1, 1, 1, 3, 2, 1, 3, 3, 1 },   // Хардкорник: заводить себе й суперника
            new[] { 3, 2, 2, 3, 1, 2, 2, 1, 1, 2 },   // Самородок: нервово-пружний
            new[] { 1, 1, 1, 0, 2, 1, 1, 2, 4, 3 },   // Силач: напруга й подих
        };

        public static float LoopLength(IdleLifeRole role) { return role == IdleLifeRole.Referee ? RefereeLoop : WrestlerLoop; }

        public static string LoopClip(IdleLifeRole role) { return role == IdleLifeRole.Referee ? RefereeLoopClip : WrestlerLoopClip; }

        /// <summary>
        /// Що грати на межі петлі номер <paramref name="loop"/> (рахунок петель цього стояння). <paramref name="lastInsertLoop"/> —
        /// номер петлі останньої вставки (на старті — дуже мале число); <paramref name="gimmick"/> — індекс ґіміка або −1;
        /// <paramref name="window"/> — секунд до наступного біту режисера (<see cref="float.PositiveInfinity"/> — чекає гравця).
        /// Повертає ім'я кліпа вставки або null — лишатися в петлі.
        /// </summary>
        public static string Pick(int actorKey, IdleLifeRole role, int gimmick, int loop, int lastInsertLoop, string lastClip,
                                  float window, bool allowed)
        {
            if (!allowed) return null;
            if (!(window >= LoopLength(role) + Margin)) return null;          // NaN теж «ні»
            int gap = loop - lastInsertLoop;
            if (gap < 2) return null;
            uint h = Hash(actorKey, loop);
            int chance = gap >= 4 ? 100 : gap == 3 ? 70 : 45;
            if (h % 100u >= (uint)chance) return null;
            string[] clips = role == IdleLifeRole.Referee ? RefereeClips : WrestlerClips;
            int total = 0;
            for (int i = 0; i < clips.Length; i++)
                if (clips[i] != lastClip) total += Weight(role, gimmick, i);
            if (total <= 0) return null;
            int r = (int)((h / 100u) % (uint)total);
            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i] == lastClip) continue;
                int w = Weight(role, gimmick, i);
                if (r < w) return clips[i];
                r -= w;
            }
            return null;
        }

        public static int Weight(IdleLifeRole role, int gimmick, int clipIndex)
        {
            if (role == IdleLifeRole.Referee) return 1;
            if (gimmick < 0 || gimmick >= GimmickWeights.Length) return 1;
            return GimmickWeights[gimmick][clipIndex];
        }

        /// <summary>Перемішування двох цілих (той самий результат на будь-якій машині й платформі).</summary>
        public static uint Hash(int a, int b)
        {
            unchecked
            {
                uint x = (uint)a * 0x9E3779B1u ^ ((uint)b + 0x7F4A7C15u) * 0x85EBCA77u;
                x ^= x >> 15;
                x *= 0x2C1B3C6Du;
                x ^= x >> 12;
                x *= 0x297A2D39u;
                x ^= x >> 15;
                return x;
            }
        }
    }
}
