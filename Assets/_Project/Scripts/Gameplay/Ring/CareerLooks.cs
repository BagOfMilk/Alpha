using System.Collections.Generic;

namespace Game.Gameplay.Ring
{
    /// <summary>
    /// Зовнішність борців кар'єри на 3D-рингу (лише вигляд, без чисел балансу): тіло модульного набору
    /// (<c>body_&lt;стать&gt;_&lt;культура&gt;</c>), колір екіпіровки, зачіска, масштаб. Культури різні — українців близько
    /// п'ятої частини (правило кастингу власника, Поправка №19/№20 Alpha). Колір костюма героя — з палітри створення.
    /// </summary>
    public static class CareerLooks
    {
        public sealed class Look
        {
            public string Body;
            public int Rgb;
            public string Hair;
            public float Scale = 1f;
            public Look(string body, int rgb, string hair, float scale = 1f) { Body = body; Rgb = rgb; Hair = hair; Scale = scale; }
        }

        /// <summary>Вісім кольорів костюма героя (CharacterDraft.CostumeColor): синій, червоний, зелений, золотий, фіолетовий, білий, чорний, помаранчевий.</summary>
        public static readonly int[] Costume = { 0x2F6FD0, 0xC02020, 0x2E9A48, 0xD4A429, 0x7A3FC0, 0xE8E8E8, 0x202024, 0xE06A1A };

        /// <summary>Тіла, з яких герой обирає на екрані створення (стать × культура).</summary>
        public static readonly string[] HeroBodies =
        {
            "body_m_ukrainian", "body_m_nordic", "body_m_mediterranean", "body_m_latin", "body_m_west_african", "body_m_east_asian",
            "body_m_south_asian", "body_m_middle_eastern",
            "body_f_ukrainian", "body_f_nordic", "body_f_mediterranean", "body_f_latin", "body_f_west_african", "body_f_east_asian",
            "body_f_south_asian", "body_f_middle_eastern"
        };

        public static readonly string[] HeroHair = { "hair_short01", "hair_short02", "hair_short03", "hair_short04", "hair_ponytail01", "hair_afro01", "" };

        private static readonly Dictionary<string, Look> Npc = new Dictionary<string, Look>
        {
            { "hammer", new Look("body_m_nordic", 0xB0201A, "hair_short04", 1.1f) },
            { "stalevyi", new Look("body_m_ukrainian", 0x5A6A78, "hair_short03", 1.08f) },
            { "dzhura", new Look("body_m_latin", 0x1FA3A0, "hair_short02", 0.96f) },
            { "vdova", new Look("body_f_east_asian", 0x30202E, "hair_ponytail01", 0.98f) },
            { "baron", new Look("body_m_mediterranean", 0x8E2E8E, "hair_short01", 1.02f) },
            { "komet", new Look("body_f_west_african", 0xD4A429, "hair_afro01", 1.02f) },
            // П5 (11.11.2026): 19 нових — культури різні, українців 4 з 19, росіян немає. Кольори — лише вже наявні (палітра костюма
            // героя й кольори перших шести): нових кольорів не вигадуємо (Статут вигляду).
            { "inti", new Look("body_f_latin", Costume[2], "hair_ponytail01", 1.04f) },
            // Ліга Семи Міст
            { "sirena", new Look("body_f_latin", Costume[0], "hair_ponytail01", 0.96f) },
            { "buivol", new Look("body_m_west_african", Costume[7], "", 1.12f) },
            { "hadiuka", new Look("body_f_ukrainian", Costume[2], "hair_ponytail01", 0.98f) },
            { "husar", new Look("body_m_nordic", Costume[1], "hair_short01", 1.04f) },
            { "skorpion", new Look("body_f_middle_eastern", Costume[3], "hair_ponytail01", 1f) },
            { "ronin", new Look("body_m_east_asian", Costume[6], "hair_short03", 1f) },
            // Національний ефір
            { "maharadzha", new Look("body_m_south_asian", Costume[4], "hair_short01", 1.06f) },
            { "blyskavka", new Look("body_f_west_african", Costume[5], "hair_afro01", 0.98f) },
            { "kharakternyk", new Look("body_m_ukrainian", Costume[1], "hair_short04", 1.04f) },
            { "troianda", new Look("body_f_latin", Costume[6], "hair_ponytail01", 1f) },
            { "toa", new Look("body_m_latin", Costume[7], "hair_afro01", 1.14f) },
            { "valkiriia", new Look("body_f_nordic", 0x5A6A78, "hair_ponytail01", 1.08f) },
            // Корона Світу
            { "khvylia", new Look("body_f_latin", 0x1FA3A0, "hair_ponytail01", 0.96f) },
            { "burevii", new Look("body_m_ukrainian", Costume[0], "hair_short02", 0.98f) },
            { "berehynia", new Look("body_f_ukrainian", Costume[3], "hair_ponytail01", 1.1f) },
            { "korason", new Look("body_m_east_asian", Costume[1], "hair_short01", 1f) },
            { "feniks", new Look("body_f_east_asian", Costume[7], "hair_ponytail01", 1f) },
            { "atlant", new Look("body_m_mediterranean", Costume[6], "", 1.16f) },
        };

        /// <summary>Тіло жіноче (body_f_*): стать героя для дивізіону (P2-6), поки екран створення не передає її в CharacterDraft.</summary>
        public static bool IsFemaleBody(string body)
        {
            return body != null && body.StartsWith("body_f_", System.StringComparison.Ordinal);
        }

        /// <summary>Ключі вигляду, що є в словнику (для охоронця «у кожного NPC ростеру — свій вигляд»).</summary>
        public static IEnumerable<string> NpcIds { get { return Npc.Keys; } }

        public static Look ForNpc(string id)
        {
            Look l;
            if (id != null && Npc.TryGetValue(id, out l)) return l;
            return new Look("body_m_south_asian", 0x606060, "hair_short02");
        }

        public static int CostumeRgb(int index)
        {
            return index >= 0 && index < Costume.Length ? Costume[index] : Costume[0];
        }
    }
}
