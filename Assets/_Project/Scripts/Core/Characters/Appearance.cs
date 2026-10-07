using System;
using System.Collections.Generic;
using System.Text;
using Game.Core.Characters.Creation;

namespace Game.Core.Characters
{
    /// <summary>Одна річ образу: частина модульного набору + колір тканини (#RRGGBB).</summary>
    [Serializable]
    public struct OutfitPiece
    {
        public string Part;
        public string Color;

        public OutfitPiece(string part, string color)
        {
            Part = part;
            Color = color;
        }
    }

    /// <summary>
    /// Зовнішність персонажа (Поправка №19.1, №19.3): культура, стать, зачіска, колір волосся,
    /// борода, одяг з кольорами, акценти (символи й «квірки» з першоджерела) і впізнавана зброя
    /// за замовчуванням. ЛИШЕ вигляд: жодного числа балансу, жодного впливу на стати — гра
    /// збирає з цього модель на спільному скелеті. Ключі частин — <see cref="KitParts"/>.
    /// Надіте спорядження (броня, зброя) перекриває одяг і впізнавану зброю на моделі.
    /// </summary>
    [Serializable]
    public sealed class Appearance
    {
        public string Culture = KitParts.DefaultCulture;
        public Gender Gender = Gender.Male;
        public string Hair = "";
        public string HairColor = "#3b2a1c";
        public string FacialHair = "";
        public List<OutfitPiece> Outfit = new List<OutfitPiece>();
        /// <summary>Символи й квірки (пір'їна беркута, вовче хутро на комірі, сережка, сліди кайданів…).</summary>
        public List<string> Accents = new List<string>();
        /// <summary>Частина набору зброї, яку видно, коли в слоті зброї порожньо (лук мисливця, сокирка).</summary>
        public string SignatureWeapon = "";

        public Appearance Clone()
        {
            return new Appearance
            {
                Culture = Culture, Gender = Gender, Hair = Hair, HairColor = HairColor, FacialHair = FacialHair,
                Outfit = new List<OutfitPiece>(Outfit), Accents = new List<string>(Accents), SignatureWeapon = SignatureWeapon
            };
        }

        public Appearance With(string part, string color)
        {
            Outfit.Add(new OutfitPiece(part, color));
            return this;
        }

        public Appearance Accent(params string[] accents)
        {
            Accents.AddRange(accents);
            return this;
        }

        // ---- Запис (для сейву і передачі в шар показу): k=v через ';' ----
        // c=культура;g=0;h=зачіска;hc=#колір;f=борода;o=частина@#колір,…;a=акцент,…;w=зброя

        public string Encode()
        {
            var sb = new StringBuilder();
            sb.Append("c=").Append(Culture ?? "");
            sb.Append(";g=").Append((int)Gender);
            sb.Append(";h=").Append(Hair ?? "");
            sb.Append(";hc=").Append(HairColor ?? "");
            sb.Append(";f=").Append(FacialHair ?? "");
            sb.Append(";o=");
            for (int i = 0; i < Outfit.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Outfit[i].Part).Append('@').Append(Outfit[i].Color);
            }
            sb.Append(";a=").Append(string.Join(",", Accents.ToArray()));
            sb.Append(";w=").Append(SignatureWeapon ?? "");
            return sb.ToString();
        }

        /// <summary>Невідомі поля ігноруються (вперед-сумісність); порожній рядок — образ за замовчуванням.</summary>
        public static Appearance Decode(string s)
        {
            var a = new Appearance();
            if (string.IsNullOrEmpty(s)) return a;
            foreach (var field in s.Split(';'))
            {
                int eq = field.IndexOf('=');
                if (eq <= 0) continue;
                string k = field.Substring(0, eq), v = field.Substring(eq + 1);
                switch (k)
                {
                    case "c": a.Culture = v; break;
                    case "g": a.Gender = v == "1" ? Gender.Female : Gender.Male; break;
                    case "h": a.Hair = v; break;
                    case "hc": a.HairColor = v; break;
                    case "f": a.FacialHair = v; break;
                    case "o":
                        foreach (var piece in v.Split(','))
                        {
                            int at = piece.IndexOf('@');
                            if (at > 0) a.Outfit.Add(new OutfitPiece(piece.Substring(0, at), piece.Substring(at + 1)));
                        }
                        break;
                    case "a":
                        foreach (var acc in v.Split(','))
                            if (acc.Length > 0) a.Accents.Add(acc);
                        break;
                    case "w": a.SignatureWeapon = v; break;
                }
            }
            return a;
        }

        /// <summary>Колір у форматі #RRGGBB (інше — помилка даних, ловить охоронець каталогу).</summary>
        public static bool IsColor(string c)
        {
            if (c == null || c.Length != 7 || c[0] != '#') return false;
            for (int i = 1; i < 7; i++)
                if (Uri.IsHexDigit(c[i]) == false) return false;
            return true;
        }
    }
}
