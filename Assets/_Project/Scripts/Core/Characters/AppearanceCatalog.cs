using System.Collections.Generic;
using Game.Core.Characters.Creation;

namespace Game.Core.Characters
{
    /// <summary>
    /// Образи іменних персонажів і варіанти для створення героя (Поправка №19.1, №19.3).
    /// Власник 07.10.2026: «якщо це персонажі з історією, то намагайся передати їх кольори,
    /// символи, квірки у зовнішності». Кожен образ спирається на першоджерело картки
    /// (<see cref="OpeningCast"/>); що з першоджерела, а що — художнє доповнення асистента,
    /// написано в коментарі. Кольори й деталі — ПРОПОЗИЦІЯ асистента до огляду власником.
    /// Детерміновано: жодного random; безіменні отримують образ хешем від id.
    /// </summary>
    public static class AppearanceCatalog
    {
        // Палітра тканин епохи (натуральні барвники): біле полотно, вохра, марена, індиго, вайда,
        // крушина, горіх, шафран, чорнило-сажа. Однакові назви по всьому каталогу.
        public const string Linen = "#ece6d6", LinenWarm = "#e2d6bd", Undyed = "#cbbfa5";
        public const string Madder = "#8c1f17", Crimson = "#6e1414", Ochre = "#b5852f", Saffron = "#c9a24a";
        public const string Indigo = "#2b3a66", Woad = "#3d5a80", ForestGreen = "#2f4a2a", Moss = "#5c6b3a";
        public const string Walnut = "#5a4030", Sheepskin = "#6b4a2e", Soot = "#22201e", Charcoal = "#33302c";
        public const string BootLeather = "#4a3020", RedLeather = "#7a2e1d";

        public const string HairBlack = "#16110d", HairDarkBrown = "#2e1f14", HairBrown = "#5a3d22",
            HairAuburn = "#8a3b1c", HairBlond = "#b08d57", HairGrey = "#9d968c", HairWhite = "#d8d4cc";

        /// <summary>Образ іменного персонажа за id картки; null — немає авторського образу.</summary>
        public static Appearance Named(string id)
        {
            switch (id)
            {
                // «Захар Беркут» (І. Франко, 1883) — тухольські громадяни, гірська Карпатія XIII ст.
                case "maksym":
                    // Син старійшини, мисливець і воїн громади; у повісті потрапляє в полон до монголів
                    // і бранцем ходить у путах — звідси сліди кайданів. Прізвище «Беркут» — пір'їна
                    // беркута (художнє). Біла сорочка з червоно-чорною вишивкою, червоний пояс, кептар,
                    // гірська сокирка-топірець (художнє: зброя карпатських горян).
                    return new Appearance { Culture = "ukrainian", Gender = Gender.Male, Hair = "hair_short03", HairColor = HairBrown,
                            FacialHair = "moustache", SignatureWeapon = "wpn_axe" }
                        .With("shirt", Linen).With("trousers", LinenWarm).With("vest", Sheepskin).With("boots", BootLeather)
                        .Accent("sash", "embroidery_red_black", "berkut_feather", "chain_scars");
                case "myroslava":
                    // Донька боярина Тугара, вправна лучниця; іде від батька до громади. Боярське походження —
                    // золота вишивка й червоні сап'янці; мисливський зелений одяг (художнє); коса з
                    // червоною стрічкою — дівоча (художнє).
                    return new Appearance { Culture = "ukrainian", Gender = Gender.Female, Hair = "hair_elvs_double_mh_braid",
                            HairColor = HairDarkBrown, SignatureWeapon = "wpn_bow" }
                        .With("shirt", Linen).With("tunic", ForestGreen).With("skirt_long", Charcoal).With("boots", RedLeather)
                        .Accent("embroidery_gold", "braid_ribbon_red", "quiver");
                case "zakhar":
                    // Старійшина Тухлі, голос громади, знахар; довга біла сорочка, сива борода й волосся,
                    // патериця старійшини (художнє — атрибут влади громади), беркутове перо роду.
                    return new Appearance { Culture = "ukrainian", Gender = Gender.Male, Hair = "hair_long01", HairColor = HairWhite,
                            FacialHair = "beard_full" }
                        .With("robe", Linen).With("vest", Sheepskin).With("shoes", BootLeather)
                        .Accent("sash", "staff", "berkut_feather", "herb_pouch");
                case "tuhar":
                    // Боярин Тугар Вовк — чужак, що хоче правити по-новому і йде на змову з ордою.
                    // Прізвище «Вовк» — вовче хутро на комірі (художнє); багатий малиновий жупан, золотий
                    // пояс і перстень-печатка боярина (художнє), шабля.
                    return new Appearance { Culture = "ukrainian", Gender = Gender.Male, Hair = "hair_short02", HairColor = HairDarkBrown,
                            FacialHair = "moustache", SignatureWeapon = "wpn_sabre" }
                        .With("kaftan", Crimson).With("trousers", Soot).With("boots", Soot)
                        .Accent("wolf_fur_collar", "boyar_belt", "gold_signet");
                // Фольклорний ярус — українська народна традиція.
                case "keeper":
                    // Дід Овсій, комірник: рахує кожен мішок уголос і нікому не вірить на слово — шнур
                    // з вузликами-рахівницею (художнє); старий пастковик — лук, овчинна шапка, кожушок.
                    return new Appearance { Culture = "ukrainian", Gender = Gender.Male, Hair = "", HairColor = HairGrey,
                            FacialHair = "beard_short", SignatureWeapon = "wpn_bow" }
                        .With("shirt", LinenWarm).With("vest", Sheepskin).With("trousers", Walnut).With("boots", BootLeather)
                        .Accent("tally_cord", "sheepskin_hat", "sash");
                case "healer":
                    // Знахарка Гафія: лікує всіх, кажучи правду в очі. Темна хустка заміжньої, трави за
                    // поясом (художнє).
                    return new Appearance { Culture = "ukrainian", Gender = Gender.Female, Hair = "hair_rehmanpolanski_hair_bun_brown",
                            HairColor = HairGrey }
                        .With("shirt", Linen).With("skirt_long", Charcoal).With("vest", Walnut).With("shoes", BootLeather)
                        .Accent("headscarf", "herb_pouch", "embroidery_red_black");
                // Пул прибульців (№12.10).
                case "goban":
                    // Гобан Саор (ірландський фольклор, записи XIX ст.) — майстер-будівничий, що перехитрює
                    // королів. Руде волосся й борода, зелений вовняний плащ із вузлуватою застібкою,
                    // фартух теслі, сокира майстра (художнє, у дусі переказів про будівничого).
                    return new Appearance { Culture = "nordic", Gender = Gender.Male, Hair = "hair_short04", HairColor = HairAuburn,
                            FacialHair = "beard_full", SignatureWeapon = "wpn_axe" }
                        .With("tunic", Moss).With("trousers", Walnut).With("cloak", ForestGreen).With("boots", BootLeather)
                        .Accent("cloak_brooch_knot", "carpenter_apron");
                case "sindbad":
                    // Синдбад-мореплавець («Тисяча й одна ніч») — купець і оповідач, сім разів розорявся
                    // і багатів. Тюрбан, шафранова мантія, індигові шаровари, шовковий пояс, золота
                    // сережка моряка, гаманець купця, вигнута шабля (художнє).
                    return new Appearance { Culture = "middle_eastern", Gender = Gender.Male, Hair = "hair_short01", HairColor = HairBlack,
                            FacialHair = "beard_short", SignatureWeapon = "wpn_sabre" }
                        .With("robe", Saffron).With("sharovary", Indigo).With("turban", Linen).With("shoes", RedLeather)
                        .Accent("sash", "gold_earring", "merchant_purse");
                case "horde_commander":
                    // Командир орди. За №8.2 — Сумангуру Канте (першоджерело ще звіряється, §5.7): у
                    // переказах — цар-коваль Сосо і чаклун. Темно-індигова мантія, залізні обручі на руках
                    // коваля, обереги-гри-гри, залізна булава (художнє, у дусі переказу).
                    return new Appearance { Culture = "west_african", Gender = Gender.Male, Hair = "hair_short04", HairColor = HairBlack,
                            FacialHair = "beard_short", SignatureWeapon = "wpn_mace" }
                        .With("robe", Indigo).With("sharovary", Soot).With("boots", Soot)
                        .Accent("iron_armrings", "gris_gris_amulets");
                default:
                    return null;
            }
        }

        // ---- Безіменні (селяни, вороги без картки): детермінований образ від id ----

        private static readonly string[] Fabrics = { Linen, LinenWarm, Undyed, Madder, Ochre, Indigo, Woad, ForestGreen, Moss, Walnut, Saffron };
        private static readonly string[] HairColors = { HairBlack, HairDarkBrown, HairBrown, HairAuburn, HairBlond, HairGrey };
        private static readonly string[] MaleHair = { "hair_short01", "hair_short02", "hair_short03", "hair_short04", "hair_cortu_short_messy_hair", "hair_long01", "hair_ponytail01", "" };
        private static readonly string[] FemaleHair = { "hair_braid01", "hair_elvs_french_braid_variation", "hair_elvs_reverse_french_braid_bun", "hair_rehmanpolanski_hair_bun_brown", "hair_long01", "hair_ponytail01", "hair_afro01", "hair_elvs_unkempt_french_braid" };
        private static readonly string[] Tops = { "shirt", "tunic", "kaftan", "robe" };

        /// <summary>
        /// Образ безіменного: культура, зачіска, кольори — стабільний хеш від id (FNV-1a, без random,
        /// інваріант 1). Той самий id — той самий вигляд у кожній партії.
        /// </summary>
        public static Appearance ForUnnamed(string id, Gender gender)
        {
            uint h = Hash(id ?? "");
            var a = new Appearance
            {
                Culture = KitParts.Cultures[(int)(h % (uint)KitParts.Cultures.Length)],
                Gender = gender,
                Hair = gender == Gender.Male ? MaleHair[(int)((h >> 3) % (uint)MaleHair.Length)] : FemaleHair[(int)((h >> 3) % (uint)FemaleHair.Length)],
                HairColor = HairColors[(int)((h >> 7) % (uint)HairColors.Length)],
                FacialHair = gender == Gender.Male ? KitParts.FacialHair[(int)((h >> 11) % 4u) % 3] : ""
            };
            if (gender == Gender.Male && ((h >> 11) % 4u) == 3u) a.FacialHair = "";
            string top = Tops[(int)((h >> 13) % (uint)Tops.Length)];
            a.With(top, Fabrics[(int)((h >> 17) % (uint)Fabrics.Length)]);
            if (top != "robe")
            {
                bool skirt = gender == Gender.Female && ((h >> 19) & 1u) == 0u;
                a.With(skirt ? "skirt_long" : "trousers", Fabrics[(int)((h >> 21) % (uint)Fabrics.Length)]);
            }
            a.With(((h >> 25) & 1u) == 0u ? "boots" : "shoes", BootLeather);
            return a;
        }

        private static uint Hash(string s)
        {
            uint h = 2166136261u;
            foreach (char ch in s) { h ^= ch; h *= 16777619u; }
            return h;
        }

        // ---- Створення героя (№19.3): що гравець може обрати ----

        /// <summary>Зачіски на вибір за статтю (бритоголовий — порожній рядок).</summary>
        public static IReadOnlyList<string> HairOptions(Gender g) => g == Gender.Male ? MaleHairOptions : FemaleHairOptions;

        private static readonly string[] MaleHairOptions =
        {
            "", "hair_short01", "hair_short02", "hair_short03", "hair_short04", "hair_cortu_short_messy_hair",
            "hair_long01", "hair_o4saken_long01", "hair_ponytail01", "hair_afro01", "hair_culturalibre_hair_01", "hair_culturalibre_hair_02"
        };

        private static readonly string[] FemaleHairOptions =
        {
            "hair_braid01", "hair_elvs_double_mh_braid", "hair_elvs_french_braid_variation", "hair_elvs_reverse_french_braid_bun",
            "hair_elvs_unkempt_french_braid", "hair_rehmanpolanski_hair_bun_brown", "hair_long01", "hair_o4saken_long01",
            "hair_ponytail01", "hair_afro01", "hair_bob01", "hair_faydaen_hair_1", "hair_culturalibre_hair_05", "hair_culturalibre_hair_06"
        };

        public static IReadOnlyList<string> HairColorOptions => HairColors;
        public static IReadOnlyList<string> FabricOptions => Fabrics;

        /// <summary>Стартове вбрання героя за культурою — три варіанти на культуру (різні силуети).</summary>
        public static IReadOnlyList<Appearance> StarterOutfits(string culture, Gender g)
        {
            var list = new List<Appearance>();
            bool f = g == Gender.Female;
            switch (culture)
            {
                case "ukrainian":
                    list.Add(Outfit(g).With("shirt", Linen).With(f ? "skirt_long" : "sharovary", f ? Charcoal : Madder).With("vest", Sheepskin).With("boots", BootLeather).Accent("sash", "embroidery_red_black"));
                    list.Add(Outfit(g).With("kaftan", Woad).With("trousers", Walnut).With("boots", BootLeather).Accent("sash"));
                    list.Add(Outfit(g).With("shirt", LinenWarm).With("trousers", Undyed).With("cloak", Walnut).With("shoes", BootLeather));
                    break;
                case "west_african":
                    list.Add(Outfit(g).With("robe", Indigo).With("shoes", BootLeather));
                    list.Add(Outfit(g).With("robe", Ochre).With("shoes", BootLeather).Accent("gris_gris_amulets"));
                    list.Add(Outfit(g).With("tunic", Saffron).With("trousers", Undyed).With("shoes", BootLeather));
                    break;
                case "east_asian":
                    list.Add(Outfit(g).With("kaftan", Indigo).With(f ? "skirt_long" : "trousers", Charcoal).With("shoes", Soot));
                    list.Add(Outfit(g).With("robe", Undyed).With("shoes", Soot));
                    list.Add(Outfit(g).With("tunic", Madder).With("trousers", Charcoal).With("boots", Soot));
                    break;
                case "south_asian":
                    list.Add(Outfit(g).With("robe", f ? Madder : Linen).With("sharovary", Linen).With("shoes", RedLeather).Accent("sash"));
                    list.Add(Outfit(g).With("robe", Saffron).With("shoes", RedLeather).With("turban", Saffron));
                    list.Add(Outfit(g).With("tunic", Woad).With("sharovary", Undyed).With("shoes", RedLeather));
                    break;
                case "middle_eastern":
                    list.Add(Outfit(g).With("robe", LinenWarm).With("turban", Linen).With("shoes", RedLeather).Accent("sash"));
                    list.Add(Outfit(g).With("robe", Indigo).With("vest", Crimson).With("shoes", RedLeather));
                    list.Add(Outfit(g).With("tunic", Ochre).With("sharovary", Charcoal).With("boots", BootLeather));
                    break;
                case "latin":
                    list.Add(Outfit(g).With("tunic", Ochre).With("trousers", Walnut).With("cloak", Madder).With("boots", BootLeather));
                    list.Add(Outfit(g).With("shirt", Linen).With(f ? "skirt_long" : "trousers", Woad).With("shoes", BootLeather).Accent("sash"));
                    list.Add(Outfit(g).With("robe", Moss).With("shoes", BootLeather));
                    break;
                case "nordic":
                    list.Add(Outfit(g).With("tunic", Woad).With("trousers", Walnut).With("cloak", Undyed).With("boots", BootLeather).Accent("cloak_brooch_knot"));
                    list.Add(Outfit(g).With("tunic", Madder).With(f ? "skirt_long" : "trousers", Undyed).With("boots", BootLeather));
                    list.Add(Outfit(g).With("shirt", LinenWarm).With("vest", Sheepskin).With("trousers", Charcoal).With("boots", BootLeather));
                    break;
                default: // mediterranean
                    list.Add(Outfit(g).With("tunic", Madder).With("trousers", Walnut).With("shoes", BootLeather));
                    list.Add(Outfit(g).With("tunic", Linen).With("cloak", Crimson).With("shoes", BootLeather));
                    list.Add(Outfit(g).With("robe", Woad).With("shoes", BootLeather).Accent("sash"));
                    break;
            }
            foreach (var a in list) a.Culture = KitParts.IsKnownCulture(culture) ? culture : KitParts.DefaultCulture;
            return list;
        }

        private static Appearance Outfit(Gender g) => new Appearance { Gender = g, Hair = "", HairColor = HairBrown };

        /// <summary>Образ героя за замовчуванням (до вибору гравця): перше вбрання першої культури.</summary>
        public static Appearance DefaultProtagonist(Gender g)
        {
            var a = StarterOutfits(KitParts.DefaultCulture, g)[0].Clone();
            a.Hair = HairOptions(g)[g == Gender.Male ? 2 : 1];
            a.HairColor = HairBrown;
            if (g == Gender.Male) a.FacialHair = "moustache";
            return a;
        }
    }
}
