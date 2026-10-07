using System;
using System.Collections.Generic;
using Game.Core.Characters;
using Game.Core.Characters.Creation;

namespace Game.Gameplay.UI
{
    /// <summary>Одна частина модульного набору, яку треба ввімкнути на скелеті, і її колір (#RRGGBB; null — не фарбувати).</summary>
    public sealed class KitPartPlan
    {
        public string Part;
        public string Tint;

        public KitPartPlan(string part, string tint)
        {
            Part = part;
            Tint = tint;
        }
    }

    /// <summary>
    /// ЩО збирати на моделі персонажа (Поправка №19): з якого FBX узяти тіло культури, які частини
    /// набору ввімкнути й якими кольорами, які зони тіла сховати під одягом. Чистий C# без рушія —
    /// збирач сцени (<c>CharacterAssembler</c>) лише застосовує план, а правила перевіряють тести
    /// (<c>CharacterKitPlanTests</c>) — той самий поділ, що VillageView/VillageStage.
    /// </summary>
    public sealed class CharacterKitPlan
    {
        /// <summary>FBX набору речей: <c>kit_m</c> чи <c>kit_f</c>.</summary>
        public string KitId;
        /// <summary>FBX тіла культури: <c>body_&lt;стать&gt;_&lt;культура&gt;</c>.</summary>
        public string BodyId;
        public readonly List<KitPartPlan> Parts = new List<KitPartPlan>();
        /// <summary>Зони тіла, що повністю під одягом (head, torso, upperarms, lowerarms, hands, thighs, calves, feet).</summary>
        public readonly List<string> HiddenZones = new List<string>();
        /// <summary>Ключі акцентів образу: гра вмикає частини, ключ яких дорівнює акценту або починається з «акцент_».</summary>
        public readonly List<string> Accents = new List<string>();

        /// <summary>
        /// Які зони тіла ховає річ (дзеркало <c>COVERS</c> з <c>tools/blender/alpha_wardrobe.py</c>):
        /// тіло-проксі грубіше за справжню поверхню і випирало б крізь тканину.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string[]> Covers = new Dictionary<string, string[]>
        {
            { "shirt", new[] { "torso", "upperarms", "lowerarms" } },
            { "tunic", new[] { "torso", "upperarms" } },
            { "kaftan", new[] { "torso", "upperarms", "lowerarms", "thighs" } },
            { "robe", new[] { "torso", "upperarms", "lowerarms", "thighs", "calves" } },
            { "trousers", new[] { "thighs", "calves" } },
            { "sharovary", new[] { "thighs", "calves" } },
            { "skirt_long", new[] { "thighs", "calves" } },
            { "boots", new[] { "feet" } },
            { "shoes", new[] { "feet" } },
            { "gambeson", new[] { "torso", "upperarms" } },
            { "mail", new[] { "torso", "upperarms", "lowerarms" } },
        };

        private static readonly string[] Helmets = { "helm_spangen", "helm_conical", "helm_kettle", "helm_kabuto" };

        /// <summary>
        /// План з образу й надітого (ключі <c>ItemDefinition.VisualKey</c>, напр. «mail», «wpn_sword»).
        /// Надіте лягає поверх: шолом ховає зачіску (бороду — ні), надіта зброя замінює впізнавану,
        /// броня тулуба йде поверх сорочки.
        /// </summary>
        public static CharacterKitPlan From(Appearance look, IEnumerable<string> equippedVisualKeys)
        {
            if (look == null) throw new ArgumentNullException(nameof(look));
            var equipped = new List<string>();
            if (equippedVisualKeys != null)
                foreach (var k in equippedVisualKeys)
                    if (!string.IsNullOrEmpty(k) && !equipped.Contains(k)) equipped.Add(k);

            bool female = look.Gender == Gender.Female;
            string culture = KitParts.IsKnownCulture(look.Culture) ? look.Culture : KitParts.DefaultCulture;
            var plan = new CharacterKitPlan
            {
                KitId = female ? "kit_f" : "kit_m",
                BodyId = "body_" + (female ? "f" : "m") + "_" + culture
            };

            bool helmet = false;
            bool weapon = false;
            foreach (var k in equipped)
            {
                if (Array.IndexOf(Helmets, k) >= 0) helmet = true;
                if (k.StartsWith("wpn_", StringComparison.Ordinal)) weapon = true;
            }

            foreach (var piece in look.Outfit)
                if (!string.IsNullOrEmpty(piece.Part)) plan.Add(piece.Part, piece.Color);
            if (!string.IsNullOrEmpty(look.Hair) && !helmet) plan.Add(look.Hair, look.HairColor);
            if (!female && !string.IsNullOrEmpty(look.FacialHair)) plan.Add(look.FacialHair, look.HairColor);
            if (!string.IsNullOrEmpty(look.SignatureWeapon) && !weapon) plan.Add(look.SignatureWeapon, null);
            foreach (var k in equipped) plan.Add(k, null);
            foreach (var a in look.Accents)
                if (!string.IsNullOrEmpty(a) && !plan.Accents.Contains(a)) plan.Accents.Add(a);

            foreach (var p in plan.Parts)
            {
                string[] zones;
                if (Covers.TryGetValue(p.Part, out zones))
                    foreach (var z in zones)
                        if (!plan.HiddenZones.Contains(z)) plan.HiddenZones.Add(z);
            }
            return plan;
        }

        private void Add(string part, string tint)
        {
            for (int i = 0; i < Parts.Count; i++)
                if (Parts[i].Part == part)
                {
                    if (tint != null) Parts[i].Tint = tint;
                    return;
                }
            Parts.Add(new KitPartPlan(part, tint));
        }

        /// <summary>Чи вмикати частину набору з ключем <paramref name="partKey"/> за цим планом.</summary>
        public bool Wants(string partKey, out string tint)
        {
            tint = null;
            if (string.IsNullOrEmpty(partKey)) return false;
            for (int i = 0; i < Parts.Count; i++)
                if (Parts[i].Part == partKey)
                {
                    tint = Parts[i].Tint;
                    return true;
                }
            for (int i = 0; i < Accents.Count; i++)
                if (MatchesAccent(partKey, Accents[i])) return true;
            return false;
        }

        /// <summary>Частина належить акценту: ключ дорівнює акценту або починається з «акцент_» (sash → sash_tails).</summary>
        public static bool MatchesAccent(string partKey, string accent)
            => partKey == accent || partKey.StartsWith(accent + "_", StringComparison.Ordinal);

        /// <summary>Зону тіла <c>body_&lt;зона&gt;</c> показувати? (очі й голова — завжди).</summary>
        public bool ShowsBodyZone(string zone) => !HiddenZones.Contains(zone);

        /// <summary>Стабільний підпис плану — збирач перебудовує модель лише коли він змінився.</summary>
        public string Signature()
        {
            var parts = new List<string>();
            foreach (var p in Parts) parts.Add(p.Part + "@" + (p.Tint ?? ""));
            return KitId + "|" + BodyId + "|" + string.Join(",", parts.ToArray()) + "|" + string.Join(",", Accents.ToArray());
        }

        /// <summary>#RRGGBB → (r, g, b) у 0..1; некоректний рядок — білий (не фарбувати).</summary>
        public static void ParseColor(string hex, out float r, out float g, out float b)
        {
            r = g = b = 1f;
            if (!Appearance.IsColor(hex)) return;
            r = Convert.ToInt32(hex.Substring(1, 2), 16) / 255f;
            g = Convert.ToInt32(hex.Substring(3, 2), 16) / 255f;
            b = Convert.ToInt32(hex.Substring(5, 2), 16) / 255f;
        }
    }
}
