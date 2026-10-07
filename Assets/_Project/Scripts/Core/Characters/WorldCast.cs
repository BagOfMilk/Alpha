using System.Collections.Generic;
using Game.Core.Characters.Creation;

namespace Game.Core.Characters
{
    /// <summary>Іменний персонаж світу: картка першоджерела + його образ (Поправка №20).</summary>
    public sealed class CastEntry
    {
        public CharacterCard Card;
        public Appearance Look;
    }

    /// <summary>
    /// Світ попаданців — 91 іменний понад каст відкриття (<see cref="OpeningCast"/>), разом 100
    /// (Поправка №20, власник 07.10.2026: «додай ще 100 персонажів з різгих культур, 20 з них українці
    /// з різних епох», «Усі персонажі мають бути в вільному доступі», «НІЯКИХ РОСІЯН», «Щоб усього було
    /// 100 персонажів»). Українських тут 14 (разом з кастом відкриття — 20 зі 100). Кожне
    /// першоджерело — у суспільному надбанні: міф, фольклор, епос, класика з автором, що помер понад
    /// 70 років тому, або хроніки й записи про історичну постать (образ береться з переказів, не з
    /// пізніших захищених версій — Поправка №2). Російських першоджерел немає (№20, охоронець —
    /// <see cref="CastingRules"/>). Хто з'являється, коли і як приєднується — справа наративної
    /// майстерні (M1.15); тут лише хто вони і як виглядають. Образи — пропозиція асистента.
    /// </summary>
    public static class WorldCast
    {
        private static List<CastEntry> _all;

        public static IReadOnlyList<CastEntry> All()
        {
            if (_all == null) _all = Build();
            return _all;
        }

        public static IEnumerable<CharacterCard> Cards()
        {
            foreach (var e in All()) yield return e.Card;
        }

        public static CastEntry Find(string id)
        {
            foreach (var e in All())
                if (e.Card.Id == id) return e;
            return null;
        }

        // ---- короткі будівники ----

        private const SourceTier Lit = SourceTier.Literary, Folk = SourceTier.Folklore;
        private const CompanionClass Br = CompanionClass.Brawler, Sh = CompanionClass.Shooter,
            He = CompanionClass.Healer, Cr = CompanionClass.Crafter;
        private const Gender M = Gender.Male, F = Gender.Female;

        private static CastEntry E(string id, string name, SourceTier tier, SourceCulture culture, string note,
            string source, string core, CompanionClass cls, bool enemy, Appearance look)
        {
            var card = new CharacterCard(id, name, tier, source, core)
            { Culture = culture, CultureNote = note, Class = cls, IsEnemy = enemy };
            return new CastEntry { Card = card, Look = look };
        }

        private static CastEntry UA(string id, string name, SourceTier tier, string source, string core, CompanionClass cls, Appearance look)
            => E(id, name, tier, SourceCulture.Ukrainian, null, source, core, cls, false, look);

        private static CastEntry W(string id, string name, SourceTier tier, string note, string source, string core,
            CompanionClass cls, Appearance look, bool enemy = false)
            => E(id, name, tier, SourceCulture.Other, note, source, core, cls, enemy, look);

        /// <summary>Образ: тіло культури набору, стать, зачіска, колір, борода, впізнавана зброя.</summary>
        private static Appearance L(string body, Gender g, string hair, string hairColor, string facial = "", string weapon = "")
            => new Appearance { Culture = body, Gender = g, Hair = hair, HairColor = hairColor, FacialHair = facial, SignatureWeapon = weapon };

        // Палітра — та сама, що в AppearanceCatalog (натуральні барвники епохи).
        private const string Linen = AppearanceCatalog.Linen, LinenW = AppearanceCatalog.LinenWarm, Undyed = AppearanceCatalog.Undyed,
            Madder = AppearanceCatalog.Madder, Crimson = AppearanceCatalog.Crimson, Ochre = AppearanceCatalog.Ochre,
            Saffron = AppearanceCatalog.Saffron, Indigo = AppearanceCatalog.Indigo, Woad = AppearanceCatalog.Woad,
            Forest = AppearanceCatalog.ForestGreen, Moss = AppearanceCatalog.Moss, Walnut = AppearanceCatalog.Walnut,
            Sheep = AppearanceCatalog.Sheepskin, Soot = AppearanceCatalog.Soot, Char = AppearanceCatalog.Charcoal,
            Boot = AppearanceCatalog.BootLeather, RedBoot = AppearanceCatalog.RedLeather;
        private const string Purple = "#4b2a4a", Teal = "#2f5a5a", Rust = "#8a4a20", Gold = "#b8923a", Sky = "#6a8fb0",
            Rose = "#a0505a", Olive = "#6b6a35", Snow = "#f2efe8", Plum = "#5a2840", Sand = "#c8b088";
        private const string Black = AppearanceCatalog.HairBlack, DBrown = AppearanceCatalog.HairDarkBrown,
            Brown = AppearanceCatalog.HairBrown, Auburn = AppearanceCatalog.HairAuburn, Blond = AppearanceCatalog.HairBlond,
            Grey = AppearanceCatalog.HairGrey, White = AppearanceCatalog.HairWhite;

        private static List<CastEntry> Build()
        {
            var list = new List<CastEntry>
            {
                // =================== Україна: 14 різних епох ===================
                // Київська Русь
                UA("kyrylo_kozhumiaka", "Кирило Кожум'яка", Folk,
                    "київський переказ про змієборця-кожум'яку («Повість минулих літ», XII ст.; народні записи XIX ст.)",
                    "силач-ремісник: б'ється не за славу, а щоб місто жило", Br,
                    L("ukrainian", M, "hair_short01", Brown, "moustache", "wpn_club").With("shirt", LinenW).With("trousers", Walnut).With("boots", Boot)
                        .Accent("carpenter_apron", "sash")),
                UA("olha_kyivska", "Княгиня Ольга", Lit,
                    "«Повість минулих літ» (XII ст.)",
                    "мудра й безжальна володарка: помста — тоді, коли її найменше чекають", He,
                    L("ukrainian", F, "hair_elvs_reverse_french_braid_bun", DBrown).With("robe", Crimson).With("shoes", RedBoot)
                        .Accent("headscarf", "embroidery_gold", "gold_earring")),
                // Народна казка й легенда
                UA("kotyhoroshko", "Котигорошко", Folk,
                    "українська народна казка «Котигорошко» (записи XIX ст.)",
                    "наймолодший і найсильніший: кидає булаву за хмари і визволяє своїх", Br,
                    L("ukrainian", M, "hair_cortu_short_messy_hair", Blond, "", "wpn_mace").With("shirt", Linen).With("sharovary", Woad).With("shoes", Boot)
                        .Accent("sash", "embroidery_red_black")),
                UA("marko_prokliatyi", "Марко Проклятий", Folk,
                    "українська легенда про Марка Проклятого (записи XIX ст.)",
                    "вічний мандрівник із прокляттям: шукає спокути, а знаходить лише нові гріхи", Br,
                    L("ukrainian", M, "hair_long01", Black, "beard_short", "wpn_sabre").With("kaftan", Soot).With("trousers", Char).With("cloak", Char).With("boots", Soot)
                        .Accent("scar_cheek", "iron_armrings")),
                // Козацька доба
                UA("ivan_sirko", "Іван Сірко", Folk,
                    "козацький отаман XVII ст.: народні перекази й думи",
                    "непереможний отаман-характерник: власну руку після смерті лишив вести військо", Br,
                    L("ukrainian", M, "", Grey, "moustache", "wpn_sabre").With("kaftan", Woad).With("sharovary", Crimson).With("boots", RedBoot)
                        .Accent("sash", "gold_signet")),
                UA("baida", "Байда", Folk,
                    "народна «Пісня про Байду» (XVI ст.; прототип — Дмитро Вишневецький)",
                    "висить на гаку, а не зрікається своїх — і ще встигає влучити з лука", Sh,
                    L("ukrainian", M, "hair_short04", DBrown, "moustache", "wpn_bow").With("kaftan", Madder).With("sharovary", Indigo).With("boots", Boot)
                        .Accent("sash", "quiver", "chain_scars")),
                UA("marusia_bohuslavka", "Маруся Богуславка", Folk,
                    "дума «Маруся Богуславка» (XVI–XVII ст.)",
                    "бранка в чужому палаці, що відчиняє в'язницю для сімохсот козаків", He,
                    L("ukrainian", F, "hair_long01", DBrown, "", "wpn_dagger").With("robe", Teal).With("sharovary", Gold).With("shoes", RedBoot)
                        .Accent("gold_earring", "kerchief", "merchant_purse")),
                UA("marusia_churai", "Маруся Чурай", Folk,
                    "напівлегендарна співачка XVII ст.: народні пісні, що їй приписують, і перекази",
                    "пісня гостріша за шаблю: співає правду, навіть коли за неї судять", He,
                    L("ukrainian", F, "hair_braid01", Auburn).With("shirt", Linen).With("skirt_long", Madder).With("vest", Forest).With("boots", RedBoot)
                        .Accent("embroidery_red_black", "braid_ribbon_red")),
                // XVIII–XIX ст.
                UA("oleksa_dovbush", "Олекса Довбуш", Folk,
                    "ватажок опришків XVIII ст.: гуцульські перекази й пісні",
                    "карпатський месник: бере в багатих і роздає бідним, від кулі береже лише зрада", Sh,
                    L("ukrainian", M, "hair_long01", Black, "moustache", "wpn_musket").With("shirt", Linen).With("trousers", Madder).With("vest", Sheep).With("boots", Boot)
                        .Accent("embroidery_red_black", "sash", "sheepskin_hat")),
                UA("ustym_karmaliuk", "Устим Кармалюк", Folk,
                    "повстанець XIX ст.: народні пісні й перекази (записи XIX ст.)",
                    "чотири рази тікав з каторги — кайдани лишили сліди, але не зламали", Br,
                    L("ukrainian", M, "hair_short02", Black, "moustache", "wpn_club").With("kaftan", Char).With("trousers", Walnut).With("cloak", Soot).With("boots", Boot)
                        .Accent("chain_scars", "kerchief")),
                UA("enei", "Еней", Lit,
                    "Іван Котляревський, «Енеїда» (1798)",
                    "моторний парубок і козак: де бенкет — там і бійка, де бійка — там і він", Br,
                    L("ukrainian", M, "hair_short03", Brown, "moustache", "wpn_sabre").With("kaftan", Crimson).With("sharovary", Woad).With("boots", RedBoot)
                        .Accent("sash", "boyar_belt")),
                UA("natalka_poltavka", "Наталка Полтавка", Lit,
                    "Іван Котляревський, «Наталка Полтавка» (1819)",
                    "вірна й уперта: чекатиме свого, хоч би хто сватав", Cr,
                    L("ukrainian", F, "hair_elvs_double_mh_braid", Blond).With("shirt", Linen).With("skirt_long", Woad).With("vest", Crimson).With("boots", RedBoot)
                        .Accent("embroidery_red_black", "braid_ribbon_red", "kerchief")),
                UA("mykola_dzheria", "Микола Джеря", Lit,
                    "Іван Нечуй-Левицький, «Микола Джеря» (1878)",
                    "кріпак, що втік і не скорився: працює за трьох, кланятися не вміє", Cr,
                    L("ukrainian", M, "hair_short04", Brown, "beard_short", "wpn_axe").With("shirt", LinenW).With("trousers", Undyed).With("boots", Boot)
                        .Accent("sheepskin_hat", "tally_cord")),
                // Початок XX ст.
                UA("mavka", "Мавка", Lit,
                    "Леся Українка, «Лісова пісня» (1911)",
                    "лісова душа, що вибрала людське серце — і не шкодує", He,
                    L("ukrainian", F, "hair_o4saken_long01", Moss_Hair).With("robe", Forest).With("shoes", Walnut)
                        .Accent("herb_pouch", "braid_ribbon_red")),

                // =================== Британські острови й Північ ===================
                W("robin_hood", "Робін Гуд", Folk, "англійська", "англійські балади XV ст. («A Gest of Robyn Hode»)",
                    "найкращий лучник Шервуду: закон для нього — справедливість, а не шериф", Sh,
                    L("nordic", M, "hair_short02", Auburn, "beard_short", "wpn_bow").With("tunic", Forest).With("trousers", Moss).With("boots", Boot)
                        .Accent("quiver")),
                W("maid_marian", "Діва Меріан", Folk, "англійська", "англійські балади й травневі ігри (XV–XVI ст.)",
                    "шляхетна, що обрала ліс: стріляє не гірше за Робіна", Sh,
                    L("nordic", F, "hair_long01", Auburn, "", "wpn_bow").With("tunic", Moss).With("skirt_long", Walnut).With("boots", Boot)
                        .Accent("quiver", "cloak_brooch_knot")),
                W("king_arthur", "Король Артур", Lit, "британська", "Томас Мелорі, «Смерть Артура» (1485)",
                    "король, що вірить у Круглий стіл більше, ніж у власний трон", Br,
                    L("nordic", M, "hair_short04", Brown, "beard_full", "wpn_sword").With("tunic", Woad).With("trousers", Char).With("cloak", Crimson).With("boots", Boot)
                        .Accent("gold_signet", "cloak_brooch_knot")),
                W("merlin", "Мерлін", Lit, "британська", "Гальфрид Монмутський (XII ст.); Томас Мелорі (1485)",
                    "старий порадник, що бачить наперед — і все одно не може зупинити долю", He,
                    L("nordic", M, "hair_long01", White, "beard_full").With("robe", Indigo).With("shoes", Boot)
                        .Accent("staff", "herb_pouch")),
                W("morgan_le_fay", "Моргана", Lit, "британська", "Томас Мелорі, «Смерть Артура» (1485)",
                    "чаклунка й сестра короля: її ворожнеча — родинна, а тому найлютіша", He,
                    L("nordic", F, "hair_o4saken_long01", Black).With("robe", Purple).With("shoes", Soot)
                        .Accent("gris_gris_amulets", "gold_earring"), enemy: true),
                W("mordred", "Мордред", Lit, "британська", "Томас Мелорі, «Смерть Артура» (1485)",
                    "син-зрадник, що чекав, поки король відвернеться", Br,
                    L("nordic", M, "hair_short01", Black, "beard_short", "wpn_sword").With("tunic", Soot).With("trousers", Soot).With("cloak", Plum).With("boots", Soot)
                        .Accent("scar_cheek", "iron_armrings"), enemy: true),
                W("cu_chulainn", "Кухулін", Folk, "ірландська", "ірландський Ольстерський цикл («Викрадення бика з Куальнге»)",
                    "юний пес Ольстера: б'ється сам проти війська і в люті не впізнає своїх", Br,
                    L("nordic", M, "hair_cortu_short_messy_hair", DBrown, "", "wpn_spear").With("tunic", Crimson).With("trousers", Undyed).With("cloak", Woad).With("shoes", Boot)
                        .Accent("cloak_brooch_knot", "iron_armrings")),
                W("scathach", "Скатах", Folk, "шотландська/ірландська", "ірландський Ольстерський цикл («Сватання до Емер»)",
                    "наставниця героїв на острові тіней: учить битися так, щоб учні її пережили", Br,
                    L("nordic", F, "hair_ponytail01", Grey, "", "wpn_spear").With("tunic", Char).With("trousers", Walnut).With("boots", Soot)
                        .Accent("iron_armrings", "scar_cheek")),
                W("beowulf", "Беовульф", Lit, "давньоанглійська", "поема «Беовульф» (VIII–XI ст.)",
                    "герой, що йде на чудовисько голими руками, бо меч його не візьме", Br,
                    L("nordic", M, "hair_long01", Blond, "beard_full", "wpn_sword").With("tunic", Ochre).With("trousers", Walnut).With("boots", Boot)
                        .Accent("gold_signet", "wolf_fur_collar")),
                W("sigurd", "Сігурд", Lit, "скандинавська", "«Сага про Вольсунгів» (XIII ст.)",
                    "змієборець, що зрозумів мову птахів — і почув у ній свою загибель", Br,
                    L("nordic", M, "hair_culturalibre_hair_01", Blond, "beard_short", "wpn_sword").With("tunic", Rust).With("trousers", Char).With("cloak", Undyed).With("boots", Boot)
                        .Accent("gold_earring")),
                W("brynhild", "Брюнгільда", Lit, "скандинавська", "«Сага про Вольсунгів» (XIII ст.); «Старша Едда»",
                    "діва-воїтелька, що не прощає обману навіть коханому", Br,
                    L("nordic", F, "hair_elvs_french_braid_variation", Blond, "", "wpn_spear").With("tunic", Sky).With("skirt_long", Char).With("boots", Boot)
                        .Accent("cloak_brooch_knot", "iron_armrings")),
                W("vainamoinen", "Вяйнямьойнен", Lit, "фінська", "Еліас Ленрот, «Калевала» (1835/1849)",
                    "старий співець, чия пісня сильніша за меч і будує човни з нічого", He,
                    L("nordic", M, "hair_long01", White, "beard_full").With("robe", Undyed).With("shoes", Boot)
                        .Accent("staff", "tally_cord")),
                W("louhi", "Лоухі", Lit, "фінська", "Еліас Ленрот, «Калевала» (1835/1849)",
                    "господиня темної Похйоли: торгується, чаклує і ховає сонце", He,
                    L("nordic", F, "hair_elvs_unkempt_french_braid", Grey).With("robe", Soot).With("cloak", Char).With("shoes", Soot)
                        .Accent("wolf_fur_collar", "gris_gris_amulets"), enemy: true),
                W("lacplesis", "Лачплесіс", Lit, "латиська", "Андрейс Пумпурс, «Лачплесіс» (1888)",
                    "ведмежий син, що захищає свою землю від чужих лицарів", Br,
                    L("nordic", M, "hair_long01", Brown, "beard_short", "wpn_axe").With("tunic", Undyed).With("trousers", Walnut).With("boots", Boot)
                        .Accent("wolf_fur_collar", "sash")),
                W("boudica", "Боудіка", Lit, "бритська (кельтська)", "Тацит, «Аннали» (II ст.); Діон Кассій",
                    "королева, що підняла племена проти легіонів за кривду дочкам", Br,
                    L("nordic", F, "hair_long01", Auburn, "", "wpn_spear").With("tunic", Madder).With("cloak", Forest).With("shoes", Boot)
                        .Accent("cloak_brooch_knot", "gold_earring")),
                W("jan_zizka", "Ян Жижка", Lit, "чеська", "гуситські хроніки XV ст.",
                    "одноокий полководець, а згодом сліпий — і жодної програної битви", Br,
                    L("nordic", M, "", Grey, "moustache", "wpn_mace").With("tunic", Char).With("trousers", Walnut).With("boots", Soot)
                        .Accent("eyepatch", "iron_armrings")),
                W("zawisza_czarny", "Завиша Чорний", Lit, "польська", "хроніки Яна Длугоша (XV ст.)",
                    "лицар, на слово якого покладалися, як на присягу", Br,
                    L("nordic", M, "hair_short03", Black, "moustache", "wpn_sword").With("tunic", Soot).With("trousers", Soot).With("cloak", Soot).With("boots", Soot)
                        .Accent("boyar_belt")),

                // =================== Середземномор'я й Західна Європа ===================
                W("joan_of_arc", "Жанна д'Арк", Lit, "французька", "протоколи суду 1431 р. і реабілітації 1456 р.",
                    "селянська дівчина, що повела військо, бо чула голоси", Br,
                    L("mediterranean", F, "hair_bob01", Brown, "", "wpn_sword").With("tunic", Snow).With("trousers", Char).With("boots", Boot)
                        .Accent("cloak_brooch_knot")),
                W("roland", "Роланд", Lit, "французька", "«Пісня про Роланда» (XI ст.)",
                    "надто гордий, щоб засурмити по допомогу вчасно", Br,
                    L("mediterranean", M, "hair_short03", Auburn, "", "wpn_sword").With("tunic", Woad).With("trousers", Undyed).With("boots", Boot)
                        .Accent("sash", "gold_signet")),
                W("ganelon", "Ганелон", Lit, "французька", "«Пісня про Роланда» (XI ст.)",
                    "вельможа, що продав ар'єргард ворогу із заздрощів", Br,
                    L("mediterranean", M, "hair_short01", Grey, "beard_short", "wpn_sword").With("kaftan", Plum).With("trousers", Soot).With("boots", Soot)
                        .Accent("gold_signet", "boyar_belt"), enemy: true),
                W("el_cid", "Сід Кампеадор", Lit, "іспанська", "«Пісня про мого Сіда» (XII–XIII ст.)",
                    "вигнанець, що власною вірністю повернув собі честь", Br,
                    L("mediterranean", M, "hair_short02", DBrown, "beard_full", "wpn_sword").With("tunic", Ochre).With("trousers", Char).With("cloak", Crimson).With("boots", Boot)
                        .Accent("cloak_brooch_knot")),
                W("don_quixote", "Дон Кіхот", Lit, "іспанська", "Мігель де Сервантес, «Дон Кіхот» (1605–1615)",
                    "лицар печального образу: бачить велетнів там, де всі бачать млини", Br,
                    L("mediterranean", M, "", Grey, "moustache", "wpn_spear").With("tunic", Undyed).With("trousers", Walnut).With("boots", Boot)
                        .Accent("kerchief")),
                W("sancho_panza", "Санчо Панса", Lit, "іспанська", "Мігель де Сервантес, «Дон Кіхот» (1605–1615)",
                    "простий здоровий глузд при божевільному лицарі — і вірніший за нього", Cr,
                    L("mediterranean", M, "hair_short01", Brown, "beard_short", "wpn_club").With("shirt", LinenW).With("trousers", Ochre).With("shoes", Boot)
                        .Accent("merchant_purse", "sash")),
                W("bradamante", "Брадаманта", Lit, "італійська", "Лудовіко Аріосто, «Несамовитий Роланд» (1516)",
                    "лицарка в білому обладунку, що перемагає всіх, окрім власного серця", Br,
                    L("mediterranean", F, "hair_ponytail01", Blond, "", "wpn_spear").With("tunic", Snow).With("trousers", Sky).With("boots", Boot)
                        .Accent("cloak_brooch_knot")),
                W("odysseus", "Одіссей", Lit, "давньогрецька", "Гомер, «Одіссея»",
                    "хитромудрий: десять років дороги додому і жодного разу не забув куди", Sh,
                    L("mediterranean", M, "hair_short04", DBrown, "beard_full", "wpn_bow").With("tunic", Ochre).With("cloak", Walnut).With("shoes", Boot)
                        .Accent("gold_signet")),
                W("penelope", "Пенелопа", Lit, "давньогрецька", "Гомер, «Одіссея»",
                    "тче вдень і розпускає вночі: її терпіння — теж зброя", Cr,
                    L("mediterranean", F, "hair_elvs_reverse_french_braid_bun", Black).With("robe", Plum).With("shoes", Boot)
                        .Accent("gold_earring", "tally_cord")),
                W("atalanta", "Аталанта", Lit, "давньогрецька", "Аполлодор, «Бібліотека»; Овідій, «Метаморфози»",
                    "мисливиця, яку не наздогнати і не перестріляти", Sh,
                    L("mediterranean", F, "hair_ponytail01", Auburn, "", "wpn_bow").With("tunic", Moss).With("shoes", Boot)
                        .Accent("quiver", "wolf_fur_collar")),
                W("spartacus", "Спартак", Lit, "фракійська", "Плутарх, «Красс»; Аппіан (II ст.)",
                    "гладіатор, що повів рабів проти Риму — і не схотів утікати сам", Br,
                    L("mediterranean", M, "hair_short01", DBrown, "beard_short", "wpn_sword").With("tunic", Undyed).With("shoes", Boot)
                        .Accent("chain_scars", "iron_armrings")),
                W("dartagnan", "д'Артаньян", Lit, "французька", "Александр Дюма, «Три мушкетери» (1844)",
                    "гасконець із гарячою головою і вірною шпагою", Br,
                    L("mediterranean", M, "hair_long01", Black, "moustache", "wpn_sword").With("tunic", Sky).With("trousers", Char).With("cloak", Woad).With("boots", Boot)
                        .Accent("cloak_brooch_knot")),
                W("cyrano", "Сірано де Бержерак", Lit, "французька", "Едмон Ростан, «Сірано де Бержерак» (1897)",
                    "поет-забіяка: б'ється рядком і шпагою водночас", Br,
                    L("mediterranean", M, "hair_long01", Brown, "moustache", "wpn_sword").With("tunic", Rust).With("trousers", Soot).With("cloak", Soot).With("boots", Soot)
                        .Accent("scar_cheek")),
                W("dracula", "Дракула", Lit, "румунська (образ)", "Брем Стокер, «Дракула» (1897)",
                    "старий граф, що живе чужим життям і чекає гостей", Br,
                    L("mediterranean", M, "hair_short03", Black, "moustache", "wpn_sword").With("kaftan", Soot).With("trousers", Soot).With("cloak", Crimson).With("boots", Soot)
                        .Accent("gold_signet", "wolf_fur_collar"), enemy: true),

                // =================== Кавказ, Близький Схід, Іран, тюркський світ ===================
                W("david_of_sasun", "Давид Сасунський", Folk, "вірменська", "вірменський епос «Сасна Црер» (записи XIX ст.)",
                    "богатир, що б'є чужого царя, аби не платити данину", Br,
                    L("middle_eastern", M, "hair_short02", Black, "beard_full", "wpn_sword").With("tunic", Madder).With("trousers", Char).With("boots", Boot)
                        .Accent("sash", "gold_signet")),
                W("tariel", "Таріель", Lit, "грузинська", "Шота Руставелі, «Витязь у тигровій шкурі» (XII ст.)",
                    "витязь у тигровій шкурі: шукає кохану по всьому світу", Br,
                    L("middle_eastern", M, "hair_long01", Black, "moustache", "wpn_sabre").With("kaftan", Ochre).With("trousers", Soot).With("boots", RedBoot)
                        .Accent("wolf_fur_collar", "boyar_belt")),
                W("nestan_darejan", "Нестан-Дареджан", Lit, "грузинська", "Шота Руставелі, «Витязь у тигровій шкурі» (XII ст.)",
                    "царівна в полоні, що й з полону веде свою гру", He,
                    L("middle_eastern", F, "hair_o4saken_long01", Black).With("robe", Rose).With("shoes", RedBoot)
                        .Accent("embroidery_gold", "gold_earring")),
                W("rostam", "Рустам", Lit, "перська", "Фірдоусі, «Шахнаме» (1010)",
                    "найсильніший з героїв Ірану, якому доля судила вбити власного сина", Br,
                    L("middle_eastern", M, "hair_short04", DBrown, "beard_full", "wpn_mace").With("tunic", Teal).With("trousers", Char).With("boots", Boot)
                        .Accent("wolf_fur_collar", "gold_signet")),
                W("gordafarid", "Гордафарід", Lit, "перська", "Фірдоусі, «Шахнаме» (1010)",
                    "діва-воїн, що сама вийшла проти Сухраба і перехитрила його", Sh,
                    L("middle_eastern", F, "hair_elvs_french_braid_variation", Black, "", "wpn_bow").With("tunic", Woad).With("trousers", Char).With("boots", Boot)
                        .Accent("quiver", "iron_armrings")),
                W("zahhak", "Заххак", Lit, "перська", "Фірдоусі, «Шахнаме» (1010)",
                    "цар-тиран зі зміями на плечах, що годує їх своїми підданими", Br,
                    L("middle_eastern", M, "hair_long01", Black, "beard_full", "wpn_mace").With("robe", Soot).With("turban", Crimson).With("shoes", Soot)
                        .Accent("gris_gris_amulets", "gold_signet"), enemy: true),
                W("scheherazade", "Шахерезада", Lit, "арабська/перська", "«Тисяча й одна ніч» (фр. переклад А. Галлана, 1704–1717)",
                    "оповідачка, що тисячу ночей рятує життя історіями", He,
                    L("middle_eastern", F, "hair_o4saken_long01", Black).With("robe", Plum).With("sharovary", Gold).With("shoes", RedBoot)
                        .Accent("gold_earring", "embroidery_gold")),
                W("ali_baba", "Алі-Баба", Lit, "арабська", "«Тисяча й одна ніч» (А. Галлан, 1704–1717)",
                    "бідний дроворуб, що підслухав два слова — і став багатим і обережним", Cr,
                    L("middle_eastern", M, "hair_short01", Black, "beard_short", "wpn_axe").With("tunic", Undyed).With("sharovary", Walnut).With("turban", Undyed).With("shoes", Boot)
                        .Accent("merchant_purse")),
                W("morgiana", "Марджана", Lit, "арабська", "«Тисяча й одна ніч» (А. Галлан, 1704–1717)",
                    "служниця, що розгадала розбійників і танцем із кинджалом урятувала дім", Br,
                    L("middle_eastern", F, "hair_braid01", Black, "", "wpn_dagger").With("robe", Saffron).With("sharovary", Crimson).With("shoes", RedBoot)
                        .Accent("gold_earring", "sash")),
                W("aladdin", "Аладдін", Lit, "арабська", "«Тисяча й одна ніч» (А. Галлан, 1704–1717)",
                    "ледар з вулиці, що з лампою в руках вчиться бути гідним дива", Cr,
                    L("middle_eastern", M, "hair_cortu_short_messy_hair", Black, "", "wpn_dagger").With("tunic", Ochre).With("sharovary", Teal).With("shoes", RedBoot)
                        .Accent("sash", "merchant_purse")),
                W("antarah", "Антара ібн Шаддад", Lit, "арабська", "поезія Антари (VI ст.) і народна «Сіра про Антару»",
                    "син невільниці, що мечем і віршем виборов собі ім'я", Br,
                    L("west_african", M, "hair_short04", Black, "beard_short", "wpn_sabre").With("robe", Undyed).With("turban", Ochre).With("boots", Boot)
                        .Accent("sash", "gold_earring")),
                W("nasreddin", "Насреддін", Folk, "тюркська/перська", "анекдоти про Насреддіна (традиція з XIII ст.)",
                    "мудрець на віслюку: жартом каже те, за що інших карають", Cr,
                    L("middle_eastern", M, "hair_short01", Grey, "beard_full").With("robe", Sand).With("turban", Linen).With("shoes", Boot)
                        .Accent("staff", "merchant_purse")),
                W("koroglu", "Кероглу", Folk, "тюркська (азербайджанська/турецька)", "епос «Кероглу» (усна традиція ашугів, записи XIX ст.)",
                    "син осліпленого конюха, що став ватажком вільних вершників", Br,
                    L("middle_eastern", M, "hair_short03", DBrown, "moustache", "wpn_sabre").With("kaftan", Teal).With("sharovary", Soot).With("boots", Boot)
                        .Accent("sash", "boyar_belt")),
                W("gilgamesh", "Гільгамеш", Lit, "месопотамська", "«Епос про Гільгамеша» (аккадські таблички)",
                    "цар, що шукав безсмертя, а знайшов дружбу й мудрість", Br,
                    L("middle_eastern", M, "hair_long01", Black, "beard_full", "wpn_axe").With("tunic", Gold).With("shoes", Boot)
                        .Accent("gold_signet", "iron_armrings")),
                W("enkidu", "Енкіду", Lit, "месопотамська", "«Епос про Гільгамеша» (аккадські таблички)",
                    "дикий чоловік зі степу, що став другом царя", Br,
                    L("middle_eastern", M, "hair_long01", DBrown, "beard_full", "wpn_club").With("tunic", Walnut).With("shoes", Walnut)
                        .Accent("wolf_fur_collar")),

                // =================== Південна Азія ===================
                W("arjuna", "Арджуна", Lit, "індійська", "«Махабхарата»",
                    "найкращий лучник, що сумнівається перед битвою — і все одно стріляє", Sh,
                    L("south_asian", M, "hair_ponytail01", Black, "moustache", "wpn_bow").With("robe", Saffron).With("sharovary", Linen).With("shoes", RedBoot)
                        .Accent("quiver", "gold_earring")),
                W("bhima", "Бхіма", Lit, "індійська", "«Махабхарата»",
                    "найсильніший з братів: булава, апетит і чесна лють", Br,
                    L("south_asian", M, "hair_short04", Black, "moustache", "wpn_mace").With("vest", Crimson).With("sharovary", Ochre).With("shoes", Boot)
                        .Accent("iron_armrings", "gold_earring")),
                W("draupadi", "Драупаді", Lit, "індійська", "«Махабхарата»",
                    "цариця, що не пробачила образи — і змусила світ це пам'ятати", He,
                    L("south_asian", F, "hair_o4saken_long01", Black).With("robe", Crimson).With("shoes", RedBoot)
                        .Accent("embroidery_gold", "gold_earring")),
                W("duryodhana", "Дурйодгана", Lit, "індійська", "«Махабхарата»",
                    "заздрісний царевич, що поставив царство на гру в кості", Br,
                    L("south_asian", M, "hair_short02", Black, "moustache", "wpn_mace").With("kaftan", Gold).With("sharovary", Soot).With("shoes", RedBoot)
                        .Accent("gold_signet", "boyar_belt"), enemy: true),
                W("lakshmibai", "Лакшмібаї, рані Джгансі", Lit, "індійська", "сучасні їй хроніки й народні балади (1857–1858)",
                    "рані, що з дитиною за спиною повела вершників у бій", Br,
                    L("south_asian", F, "hair_ponytail01", Black, "", "wpn_sabre").With("tunic", Plum).With("sharovary", Linen).With("boots", Boot)
                        .Accent("sash", "gold_earring")),

                // =================== Східна й Південно-Східна Азія ===================
                W("hua_mulan", "Хуа Мулань", Lit, "китайська", "«Балада про Мулань» (V–VI ст.)",
                    "пішла на війну замість батька й дванадцять років лишалась нерозпізнаною", Br,
                    L("east_asian", F, "hair_rehmanpolanski_hair_bun_brown", Black, "", "wpn_sword").With("kaftan", Crimson).With("trousers", Char).With("boots", Soot)
                        .Accent("iron_armrings")),
                W("sun_wukong", "Сунь Укун", Lit, "китайська", "У Чен'ень, «Подорож на Захід» (1592)",
                    "цар мавп: непокірний, кмітливий і з посохом, що росте за бажанням", Br,
                    L("east_asian", M, "hair_cortu_short_messy_hair", Auburn, "", "wpn_club").With("tunic", Gold).With("trousers", Crimson).With("boots", Soot)
                        .Accent("staff", "iron_armrings")),
                W("guan_yu", "Гуань Юй", Lit, "китайська", "Ло Гуаньчжун, «Троецарство» (XIV ст.)",
                    "червонолиций воїн із довгою бородою: вірність — понад усе", Br,
                    L("east_asian", M, "hair_long01", Black, "beard_full", "wpn_spear").With("kaftan", Forest).With("trousers", Char).With("boots", Soot)
                        .Accent("boyar_belt")),
                W("zhuge_liang", "Чжуге Лян", Lit, "китайська", "Ло Гуаньчжун, «Троецарство» (XIV ст.)",
                    "стратег у простому вбранні: виграє битви, не вставаючи з воза", He,
                    L("east_asian", M, "hair_ponytail01", Black, "beard_short").With("robe", Snow).With("shoes", Soot)
                        .Accent("berkut_feather", "staff")),
                W("cao_cao", "Цао Цао", Lit, "китайська", "Ло Гуаньчжун, «Троецарство» (XIV ст.)",
                    "хитрий канцлер: «хай краще я зраджу світ, ніж світ — мене»", Br,
                    L("east_asian", M, "hair_short02", Black, "beard_short", "wpn_sword").With("kaftan", Soot).With("trousers", Soot).With("cloak", Crimson).With("boots", Soot)
                        .Accent("gold_signet"), enemy: true),
                W("tomoe_gozen", "Томое Ґодзен", Lit, "японська", "«Повість про дім Тайра» (XIV ст.)",
                    "лучниця й вершниця, вартна тисячі воїнів", Sh,
                    L("east_asian", F, "hair_o4saken_long01", Black, "", "wpn_bow").With("kaftan", Indigo).With("trousers", Char).With("boots", Soot)
                        .Accent("quiver")),
                W("benkei", "Бенкей", Lit, "японська", "«Ґікейкі» (XIV–XV ст.)",
                    "чернець-велетень, що стояв на мосту до останнього подиху", Br,
                    L("east_asian", M, "", Black, "beard_short", "wpn_spear").With("robe", Soot).With("shoes", Soot)
                        .Accent("headscarf", "gris_gris_amulets")),
                W("yoshitsune", "Мінамото-но Йошіцуне", Lit, "японська", "«Повість про дім Тайра»; «Ґікейкі» (XIV–XV ст.)",
                    "блискучий молодий полководець, переслідуваний власним братом", Br,
                    L("east_asian", M, "hair_ponytail01", Black, "", "wpn_katana").With("kaftan", Teal).With("trousers", Snow).With("boots", Soot)
                        .Accent("sash")),
                W("momotaro", "Момотаро", Folk, "японська", "японська народна казка (записи періоду Едо)",
                    "хлопчик із персика, що зібрав загін і пішов на острів демонів", Br,
                    L("east_asian", M, "hair_short03", Black, "", "wpn_katana").With("tunic", Rose).With("trousers", Undyed).With("shoes", Soot)
                        .Accent("kerchief", "merchant_purse")),
                W("hong_gildong", "Хон Кільтон", Lit, "корейська", "«Повість про Хон Кільтона» (XVII ст.)",
                    "син служниці, якого не визнали, — став розбійником-справедливцем", Cr,
                    L("east_asian", M, "hair_ponytail01", Black, "", "wpn_sword").With("robe", Sky).With("shoes", Soot)
                        .Accent("sash")),
                W("trung_trac", "Чинг Чак", Lit, "в'єтнамська", "в'єтнамські хроніки й храмові перекази (сестри Чинг, I ст.)",
                    "сестра-повстанниця на бойовому слоні, що вигнала намісника", Br,
                    L("east_asian", F, "hair_elvs_reverse_french_braid_bun", Black, "", "wpn_sword").With("robe", Gold).With("shoes", Soot)
                        .Accent("iron_armrings", "embroidery_gold")),
                W("hang_tuah", "Ханг Туах", Lit, "малайська", "«Хікаят Ханг Туах» (XVII–XVIII ст.)",
                    "адмірал, вірний султанові навіть тоді, коли той несправедливий", Br,
                    L("east_asian", M, "hair_short02", Black, "moustache", "wpn_dagger").With("tunic", Gold).With("sharovary", Forest).With("shoes", Boot)
                        .Accent("sash", "kerchief")),

                // =================== Африка ===================
                W("sundiata", "Сундіата Кейта", Folk, "мандінка", "«Епос про Сундіату» (усна традиція гріотів)",
                    "кульгавий у дитинстві, він підвівся — і заснував імперію Малі", Br,
                    L("west_african", M, "hair_short01", Black, "beard_short", "wpn_bow").With("robe", Snow).With("shoes", Boot)
                        .Accent("gris_gris_amulets", "quiver")),
                W("sogolon", "Соголон", Folk, "мандінка", "«Епос про Сундіату» (усна традиція гріотів)",
                    "мати-«буйволиця»: негарна для двору, мудра для сина", He,
                    L("west_african", F, "hair_afro01", Black).With("robe", Indigo).With("shoes", Boot)
                        .Accent("headscarf", "gris_gris_amulets")),
                W("nzinga", "Нзінга Мбанде", Lit, "мбунду", "португальські й капуцинські хроніки XVII ст.",
                    "королева-дипломатка: сиділа на спині слуги, бо стільця їй не дали", Br,
                    L("west_african", F, "hair_bob02", Black, "", "wpn_axe").With("robe", Crimson).With("shoes", RedBoot)
                        .Accent("gold_earring", "iron_armrings")),
                W("makeda", "Македа, цариця Савська", Lit, "ефіопська", "«Кебра Негаст» (XIV ст.)",
                    "цариця, що прийшла випробувати мудреця загадками", He,
                    L("west_african", F, "hair_braid01", Black).With("robe", Saffron).With("shoes", RedBoot)
                        .Accent("embroidery_gold", "gold_earring")),
                W("mwindo", "Мвіндо", Folk, "ньянга", "«Епос про Мвіндо» (усна традиція ньянга)",
                    "народжений, що вже говорив, — і з чарівним віялом пройшов підземний світ", Br,
                    L("west_african", M, "hair_short04", Black, "", "wpn_club").With("tunic", Ochre).With("shoes", Walnut)
                        .Accent("gris_gris_amulets", "herb_pouch")),
                W("amina_zazzau", "Аміна з Заззау", Lit, "хауса", "«Кано хроніка» (записи XIX ст.)",
                    "цариця-воїн, що оточила міста глиняними мурами", Br,
                    L("west_african", F, "hair_afro01", Black, "", "wpn_spear").With("robe", Teal).With("shoes", Boot)
                        .Accent("iron_armrings", "sash")),
                W("yaa_asantewaa", "Яа Асантева", Lit, "ашанті", "сучасні їй свідчення й хроніки (1900)",
                    "королева-мати, що закликала до бою, коли чоловіки вагались", He,
                    L("west_african", F, "hair_bob01", Grey, "", "wpn_musket").With("robe", Gold).With("shoes", Boot)
                        .Accent("gris_gris_amulets", "gold_earring")),

                // =================== Америки й Океанія ===================
                W("hiawatha", "Гаявата", Lit, "ірокезька (образ)", "Генрі Лонгфелло, «Пісня про Гаявату» (1855)",
                    "миротворець, що вчив племена жити разом", He,
                    L("latin", M, "hair_long01", Black, "", "wpn_bow").With("tunic", Walnut).With("trousers", Walnut).With("shoes", Walnut)
                        .Accent("berkut_feather", "quiver")),
                W("hunahpu", "Хунахпу", Lit, "майя (кіче)", "«Пополь-Вух» (запис XVI ст.)",
                    "близнюк-герой, що переграв володарів підземного світу", Sh,
                    L("latin", M, "hair_ponytail01", Black, "", "wpn_spear").With("tunic", Snow).With("shoes", Walnut)
                        .Accent("berkut_feather", "gris_gris_amulets")),
                W("ollantay", "Ольянтай", Lit, "кечуа", "драма «Ольянтай» (запис XVIII ст.)",
                    "полководець, що повстав проти інки через кохання до його доньки", Br,
                    L("latin", M, "hair_short03", Black, "", "wpn_mace").With("tunic", Crimson).With("cloak", Gold).With("shoes", Walnut)
                        .Accent("gold_earring", "sash")),
                W("lautaro", "Лаутаро", Lit, "мапуче", "Алонсо де Ерсілья, «Араукана» (1569)",
                    "юний вождь, що вивчив ворога зсередини і переміг його", Br,
                    L("latin", M, "hair_long01", Black, "", "wpn_spear").With("tunic", Woad).With("shoes", Walnut)
                        .Accent("kerchief", "iron_armrings")),
                W("anacaona", "Анакаона", Lit, "таїно", "хроніки Бартоломе де лас Касаса (XVI ст.)",
                    "касика-поетеса, що до останнього вела переговори за свій народ", He,
                    L("latin", F, "hair_long01", Black).With("robe", Snow).With("shoes", Walnut)
                        .Accent("gris_gris_amulets", "gold_earring")),
                W("juana_azurduy", "Хуана Асурдуй", Lit, "болівійська", "сучасні їй військові звіти й мемуари (1810-ті)",
                    "командирка партизан, що вела бій навіть вагітною", Br,
                    L("latin", F, "hair_elvs_french_braid_variation", Black, "", "wpn_sabre").With("tunic", Woad).With("trousers", Char).With("boots", Boot)
                        .Accent("sash")),
                W("maui", "Мауї", Folk, "полінезійська", "полінезійські міфи (записи XIX ст.)",
                    "хитрун, що виловив острови з моря і сповільнив сонце", Cr,
                    L("latin", M, "hair_culturalibre_hair_02", Black, "", "wpn_club").With("tunic", Ochre).With("shoes", Walnut)
                        .Accent("gris_gris_amulets", "tally_cord")),
            };
            return list;
        }

        // Зелене волосся Мавки — частина образу (лісова душа), не з палітри людей.
        private const string Moss_Hair = "#4a5a2c";
    }
}
