using System.Collections.Generic;
using Game.Core.Ring.Career;

namespace Game.Core.Ring
{
    /// <summary>
    /// Суть ґіміків 5–8 у правилах (TRPG_DESIGN §5.2, черга П5, пункт 4; умови ПМ 12.11.2026): «Ветеран — Більше жетонів реверсу,
    /// читає суперника», «Хардкорник — Підлога, предмети, терпіти біль», «Новачок — Швидко росте, мало прийомів», «Силач — Підняття,
    /// кидки, повільний». Основа атрибутів, вільні бали й стартові прийоми — у career_gimmicks.json (Силач «повільний» — спритність
    /// −1 в основі, Новачок «мало прийомів» — без ґімік-прийомів на старті); тут — те, що змінює матч і кар'єру. Поправки матчу йдуть
    /// тим самим збирачем, що перки й пізні ролі («один ефект — одна система»), у всіх трьох місцях: шоу кар'єри, матч ворожнечі
    /// (<see cref="FeudState.BuildMatchSetup"/>) і моди NPC-суперника. Числа — ПЛЕЙСХОЛДЕР (на приймання власнику).
    /// </summary>
    public static class GimmickRules
    {
        /// <summary>
        /// Ветеран: «більше жетонів реверсу» — це перк «Ветеран», вбудований від старту (умова ПМ). Купити його вдруге не можна, тож
        /// ґімік, перк і Легенда не складаються понад те, що доступне будь-кому.
        /// </summary>
        private static readonly string[] VeteranPerks = { PerkCatalog.Veteran };
        private static readonly string[] NoPerks = new string[0];

        /// <summary>Хардкорник терпить біль: пороги ушкодження в матчі зсунуті (як у перка «Залізна щелепа»).</summary>
        public const int HardcoreDamageShift = 1;
        /// <summary>Хардкорник: травма зони в кар'єрі — з накопиченого ушкодження, більшого на стільки.</summary>
        public const int HardcoreInjuryExtra = 1;
        /// <summary>Новачок швидко росте: очок розвитку за кожен зіграний матч.</summary>
        public const int RookieDevPerMatch = 1;
        /// <summary>Силач «важкий з першого кидка»: імпульс на старті матчу.</summary>
        public const int StrongmanStartMomentum = 1;

        /// <summary>Перки, вбудовані в ґімік від створення (не куплені: не коштують очок і не роблять суперників сильнішими).</summary>
        public static IReadOnlyList<string> BuiltInPerks(Gimmick g) { return g == Gimmick.Veteran ? VeteranPerks : NoPerks; }

        /// <summary>Чи перк вбудований у ґімік (не куплений).</summary>
        public static bool IsBuiltInPerk(Gimmick g, string perkId)
        {
            foreach (var p in BuiltInPerks(g)) if (p == perkId) return true;
            return false;
        }

        /// <summary>
        /// Поправки матчу від ґіміка — поверх перків і пізньої ролі. Вбудований перк сюди не входить: у гравця він уже серед перків
        /// (<see cref="Development.ToModifiers"/>), у NPC — через <see cref="ForRival"/>.
        /// </summary>
        public static void ApplyTo(MatchModifiers m, Gimmick g)
        {
            if (m == null) return;
            switch (g)
            {
                case Gimmick.Veteran: m.ReadsRival = true; break;   // показ наміру суперника — рушій бою (лише показ, на кидки не впливає)
                case Gimmick.Hardcore: m.DamageThresholdShift += HardcoreDamageShift; break;
                case Gimmick.Strongman: m.StartMomentumBonus += StrongmanStartMomentum; break;
            }
        }

        /// <summary>Поправки NPC-суперника від його ґіміка: вбудовані перки (у NPC перків немає) і решта суті ґіміка.</summary>
        public static MatchModifiers ForRival(Gimmick g)
        {
            var m = Development.ToModifiers(BuiltInPerks(g));
            ApplyTo(m, g);
            return m;
        }

        /// <summary>Накопичене ушкодження зони, з якого починається травма (кар'єра).</summary>
        public static int InjuryAtZoneDamage(Gimmick g)
        {
            return CareerRules.InjuryAtZoneDamage + (g == Gimmick.Hardcore ? HardcoreInjuryExtra : 0);
        }

        /// <summary>Додаткові очки розвитку за зіграний матч.</summary>
        public static int DevPerMatch(Gimmick g) { return g == Gimmick.Rookie ? RookieDevPerMatch : 0; }
    }
}
