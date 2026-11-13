using System;

namespace Game.Gameplay.Ring.Stage
{
    /// <summary>
    /// Люди кар'єри поза рингом (лише вигляд, без чисел балансу; рішення власника 12.11.2026, № 8 і № 10): букер федерації в
    /// закуліссі, гості подій закулісся на місці букера (чи поруч із ним) і менеджерка в кутку героя. Ключі — мовці сцен
    /// закулісся (<c>career_backstage.json</c>, <c>CareerContent.ExtraSpeakers</c>). Кольори — лише ті, що вже є в грі
    /// (палітра костюма героя <see cref="CareerLooks.Costume"/> і кольори постатей): нових кольорів не вигадуємо (Статут вигляду).
    /// Чистий C# — охоронець <c>BackstageCastTests</c> звіряє, що кожен мовець сцени закулісся має постать.
    /// </summary>
    public static class BackstageCast
    {
        /// <summary>
        /// Постать: тіло набору, зачіска (ключ частини набору або порожньо), колір сорочки (0xRRGGBB);
        /// <see cref="Dressed"/> = false — у борцівському (ветеран у лазареті).
        /// </summary>
        public sealed class Person
        {
            public readonly string Speaker, Body, Hair;
            public readonly int Rgb;
            public readonly bool Dressed;

            public Person(string speaker, string body, string hair, int rgb, bool dressed = true)
            {
                Speaker = speaker; Body = body; Hair = hair; Rgb = rgb; Dressed = dressed;
            }

            public bool Female { get { return Body != null && Body.StartsWith("body_f_", StringComparison.Ordinal); } }
        }

        /// <summary>Букер незалежної федерації (рівень 1) — Ліна Ворон; вона ж — якщо кар'єра букера не назвала.</summary>
        public const string DefaultBooker = "booker";

        private static readonly Person[] Bookers =
        {
            // Ліна Ворон (Незалежна): як з лукбуку 04.11.2026 — хвіст, світло-сталева сорочка.
            new Person("booker", "body_f_mediterranean", "hair_ponytail01", 0x8C9EB3),
            // Агата Новак (Ліга Семи Міст): гастролі, автобус, дев'ятий сезон у дорозі — каре, сталева сорочка.
            new Person("booker2", "body_f_nordic", "hair_bob02", 0x5A6A78),
            // Девід Окоро (Національний ефір): телевізійник — коротка стрижка, чорна сорочка.
            new Person("booker3", "body_m_west_african", "hair_short01", CareerLooks.Costume[6]),
            // Хіроко Танака (Корона Світу): світова сцена — каре, біла сорочка.
            new Person("booker4", "body_f_east_asian", "hair_bob01", CareerLooks.Costume[5]),
        };

        /// <summary>Марта Ріос — менеджерка героя: у кутку на великих шоу й у події найму; червона сорочка.</summary>
        public static readonly Person Manager = new Person("manager", "body_f_latin", "hair_long01", CareerLooks.Costume[1]);

        /// <summary>Букер за мовцем (booker, booker2…booker4); невідомий — Ліна.</summary>
        public static Person Booker(string speaker)
        {
            foreach (var p in Bookers) if (p.Speaker == speaker) return p;
            return Bookers[0];
        }

        /// <summary>Чи мовець — букер федерації (booker, booker2…): у сцені він на місці букера.</summary>
        public static bool IsBooker(string speaker)
        {
            return speaker != null && speaker.StartsWith(DefaultBooker, StringComparison.Ordinal);
        }

        /// <summary>Усі букери (огляд і охоронець).</summary>
        public static Person[] AllBookers { get { return (Person[])Bookers.Clone(); } }

        /// <summary>
        /// Гість події закулісся — той, хто говорить, крім букера, героя, оповідача й залу. Подія без гостя (мішаний матч,
        /// поворот) — null: тоді говорить букер федерації.
        /// </summary>
        public static string GuestSpeaker(string sceneId)
        {
            switch (sceneId)
            {
                case "ev_parking": return "baron";
                case "ev_interview": return "host";
                case "ev_contract": case "ev_scout": return "scout";
                case "ev_manager": return "manager";
                case "ev_friend": case "ev_veteran": case "ev_betrayal": return "stalevyi";
                default: return null;
            }
        }

        /// <summary>Подія, де говорять і гість, і букер (зрада напарника): гість стоїть поруч із букером, а не на його місці.</summary>
        public static bool GuestWithBooker(string sceneId) { return sceneId == "ev_betrayal"; }

        /// <summary>Подія на парковці: холодна лампа, що мерехтить.</summary>
        public static bool Parking(string sceneId) { return sceneId == "ev_parking"; }

        /// <summary>Постать гостя за мовцем; невідомий — null.</summary>
        public static Person Guest(string speaker)
        {
            switch (speaker)
            {
                case "baron": { var l = CareerLooks.ForNpc("baron"); return new Person(speaker, l.Body, l.Hair, l.Rgb); }
                case "stalevyi": { var l = CareerLooks.ForNpc("stalevyi"); return new Person(speaker, l.Body, l.Hair, l.Rgb, false); }
                // Ведуча інтерв'ю й агент-скаут — як було в RingStage3D.SetBackstageScene.
                case "host": return new Person(speaker, "body_f_south_asian", "hair_bob02", 0x3B5BA0);
                case "scout": return new Person(speaker, "body_m_east_asian", "hair_short02", CareerLooks.Costume[6]);
                case "manager": return Manager;
                default: return null;
            }
        }
    }
}
