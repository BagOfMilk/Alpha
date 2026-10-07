using System.Collections.Generic;

namespace Game.Core.Characters
{
    /// <summary>
    /// Ключі частин модульного набору персонажів (Поправка №19; набір генерує
    /// <c>tools/blender/alpha_wardrobe.py</c>, експорт — <c>Assets/Art/Characters/Kit</c>, перелік того, що
    /// справді є у FBX, — <c>kit_manifest.txt</c> поруч). Ядро оперує лише цими рядками: що вмикати
    /// на скелеті, вирішує гра. Охоронець <c>AppearanceTests</c> звіряє каталоги з цим переліком, а
    /// перелік — з маніфестом набору.
    /// </summary>
    public static class KitParts
    {
        public const string DefaultCulture = "ukrainian";

        /// <summary>Культури тіл набору (тіло body_&lt;стать&gt;_&lt;культура&gt;).</summary>
        public static readonly string[] Cultures =
        {
            "ukrainian", "west_african", "east_asian", "south_asian", "middle_eastern", "latin", "nordic", "mediterranean"
        };

        /// <summary>Одяг епох (тканини нейтральні — колір задає образ).</summary>
        public static readonly string[] Clothing =
        {
            "shirt", "tunic", "kaftan", "robe", "vest", "trousers", "sharovary", "skirt_long", "boots", "shoes", "cloak", "turban"
        };

        /// <summary>Броня (її вмикає надітий предмет: <c>ItemDefinition.VisualKey</c>).</summary>
        public static readonly string[] Armor =
        {
            "gambeson", "mail", "cuirass", "bracers", "greaves",
            "helm_spangen", "helm_conical", "helm_kettle", "helm_kabuto",
            "shield_round", "shield_kite", "buckler"
        };

        public static readonly string[] Weapons =
        {
            "wpn_sword", "wpn_sabre", "wpn_katana", "wpn_dagger", "wpn_axe", "wpn_mace", "wpn_spear", "wpn_club", "wpn_bow", "wpn_musket"
        };

        /// <summary>Бороди й вуса — лише чоловічий набір.</summary>
        public static readonly string[] FacialHair = { "beard_full", "beard_short", "moustache" };

        /// <summary>CC0-зачіски MakeHuman у наборі (ключ = "hair_" + назва ассета).</summary>
        public static readonly string[] Hair =
        {
            "hair_afro01", "hair_bob01", "hair_bob02", "hair_braid01", "hair_cortu_short_messy_hair", "hair_cortu_straight_bangs",
            "hair_culturalibre_hair_01", "hair_culturalibre_hair_02", "hair_culturalibre_hair_05", "hair_culturalibre_hair_06",
            "hair_elvs_double_mh_braid", "hair_elvs_french_braid_variation", "hair_elvs_reverse_french_braid_bun",
            "hair_elvs_unkempt_french_braid", "hair_faydaen_hair_1", "hair_littleright_bobcut_hair", "hair_long01",
            "hair_o4saken_long01", "hair_ponytail01", "hair_rehmanpolanski_hair_bun_brown", "hair_short01", "hair_short02",
            "hair_short03", "hair_short04", "hair_sonntag78_blond_with_headband", "hair_sonntag78_junglebook_hair",
            "hair_toigo_blunt_bob", "hair_toigo_blunt_bob_with_bangs", "hair_toigo_curled_under_bob",
            "hair_toigo_curled_under_bob_with_bangs", "hair_toigo_inverted_bob", "hair_toigo_inverted_bob_with_bangs"
        };

        /// <summary>
        /// Акценти — символи й квірки з першоджерел (пір'їна беркута, вовче хутро, сережка, сліди
        /// кайданів, оберіг…). ЗАПЛАНОВАНІ: генеруються наступним проходом набору; поки сітки
        /// немає, гра їх пропускає (образ від цього не ламається).
        /// </summary>
        public static readonly string[] Accents =
        {
            "sash", "berkut_feather", "chain_scars", "embroidery_red_black", "embroidery_gold", "braid_ribbon_red",
            "quiver", "staff", "wolf_fur_collar", "gold_signet", "boyar_belt", "tally_cord", "herb_pouch", "headscarf",
            "cloak_brooch_knot", "carpenter_apron", "gold_earring", "merchant_purse", "gris_gris_amulets", "iron_armrings",
            "kerchief", "sheepskin_hat", "eyepatch", "scar_cheek"
        };

        private static HashSet<string> _built;

        /// <summary>Частини, які вже є в наборі (усе, крім запланованих акцентів).</summary>
        public static bool IsBuilt(string part)
        {
            if (_built == null)
            {
                var s = new HashSet<string>();
                foreach (var list in new[] { Clothing, Armor, Weapons, FacialHair, Hair })
                    foreach (var p in list) s.Add(p);
                _built = s;
            }
            return !string.IsNullOrEmpty(part) && _built.Contains(part);
        }

        public static bool IsKnownCulture(string culture) => System.Array.IndexOf(Cultures, culture) >= 0;

        /// <summary>Будь-яка відома частина — збудована або запланований акцент.</summary>
        public static bool IsKnown(string part) => IsBuilt(part) || System.Array.IndexOf(Accents, part) >= 0;
    }
}
