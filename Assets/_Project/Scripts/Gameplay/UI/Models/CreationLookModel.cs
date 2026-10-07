using System;
using System.Collections.Generic;
using Game.Core.Characters;
using Game.Core.Characters.Creation;

namespace Game.Gameplay.UI
{
    /// <summary>Що гравець перемикає на екрані створення (кожен рядок — «◀ значення ▶»).</summary>
    public enum CreationLookField
    {
        Culture = 0,
        Hair = 1,
        HairColor = 2,
        FacialHair = 3,
        Outfit = 4,
        OutfitColor = 5
    }

    /// <summary>
    /// Вибір зовнішності героя на екрані створення (Поправка №19.3): культура, зачіска, колір волосся,
    /// борода, стартове вбрання, колір вбрання. Чистий C#: екран (UI Toolkit) лише показує
    /// <see cref="Label"/> і кличе <see cref="Step"/>, а готовий образ іде в ядро
    /// (<c>GameSession.SetProtagonistAppearance</c>, яке ще раз його перевіряє). Варіанти — з
    /// <see cref="AppearanceCatalog"/>; жодної випадковості, вибір лише кнопками.
    /// </summary>
    public sealed class CreationLookModel
    {
        private Gender _gender;
        private int _culture, _hair, _hairColor, _facial, _outfit, _outfitColor = -1;

        public Gender Gender => _gender;

        public CreationLookModel(Appearance start)
        {
            Load(start ?? AppearanceCatalog.DefaultProtagonist(Gender.Male));
        }

        /// <summary>Підхопити образ із сесії (після «Продовжити» чи зміни статі в ядрі).</summary>
        public void Load(Appearance a)
        {
            _gender = a.Gender;
            _culture = Math.Max(0, Array.IndexOf(KitParts.Cultures, a.Culture));
            _hair = Math.Max(0, IndexOf(AppearanceCatalog.HairOptions(_gender), a.Hair));
            _hairColor = Math.Max(0, IndexOf(AppearanceCatalog.HairColorOptions, a.HairColor));
            _facial = FacialOptions().IndexOf(a.FacialHair ?? "");
            if (_facial < 0) _facial = 0;
            _outfit = 0;
            var outfits = AppearanceCatalog.StarterOutfits(KitParts.Cultures[_culture], _gender);
            for (int i = 0; i < outfits.Count; i++)
                if (SameOutfitParts(outfits[i], a)) { _outfit = i; break; }
            _outfitColor = -1;
            if (a.Outfit.Count > 0)
            {
                int idx = IndexOf(AppearanceCatalog.FabricOptions, a.Outfit[0].Color);
                var baseColor = outfits[_outfit].Outfit.Count > 0 ? outfits[_outfit].Outfit[0].Color : null;
                if (idx >= 0 && a.Outfit[0].Color != baseColor) _outfitColor = idx;
            }
        }

        /// <summary>Нова стать — інший набір: зачіска й борода скидаються на першу доступну.</summary>
        public void SetGender(Gender g)
        {
            if (g == _gender) return;
            _gender = g;
            _hair = Math.Min(_hair, AppearanceCatalog.HairOptions(g).Count - 1);
            _facial = 0;
        }

        public int Count(CreationLookField f)
        {
            switch (f)
            {
                case CreationLookField.Culture: return KitParts.Cultures.Length;
                case CreationLookField.Hair: return AppearanceCatalog.HairOptions(_gender).Count;
                case CreationLookField.HairColor: return AppearanceCatalog.HairColorOptions.Count;
                case CreationLookField.FacialHair: return FacialOptions().Count;
                case CreationLookField.Outfit: return AppearanceCatalog.StarterOutfits(KitParts.Cultures[_culture], _gender).Count;
                default: return AppearanceCatalog.FabricOptions.Count + 1; // + «як у вбранні»
            }
        }

        public int Index(CreationLookField f)
        {
            switch (f)
            {
                case CreationLookField.Culture: return _culture;
                case CreationLookField.Hair: return _hair;
                case CreationLookField.HairColor: return _hairColor;
                case CreationLookField.FacialHair: return _facial;
                case CreationLookField.Outfit: return _outfit;
                default: return _outfitColor + 1;
            }
        }

        /// <summary>Чи показувати рядок (борода — лише в чоловічому наборі).</summary>
        public bool Visible(CreationLookField f) => f != CreationLookField.FacialHair || _gender == Gender.Male;

        /// <summary>Крок уперед (+1) чи назад (−1) по колу.</summary>
        public void Step(CreationLookField f, int delta)
        {
            int n = Count(f);
            if (n <= 0) return;
            int i = ((Index(f) + delta) % n + n) % n;
            switch (f)
            {
                case CreationLookField.Culture: _culture = i; _outfit = Math.Min(_outfit, Count(CreationLookField.Outfit) - 1); break;
                case CreationLookField.Hair: _hair = i; break;
                case CreationLookField.HairColor: _hairColor = i; break;
                case CreationLookField.FacialHair: _facial = i; break;
                case CreationLookField.Outfit: _outfit = i; break;
                default: _outfitColor = i - 1; break;
            }
        }

        /// <summary>Значення рядка: ключ тексту (культура) або колір (#RRGGBB) чи номер варіанта.</summary>
        public string ValueKey(CreationLookField f)
        {
            switch (f)
            {
                case CreationLookField.Culture: return "culture." + KitParts.Cultures[_culture];
                case CreationLookField.FacialHair:
                    var fh = FacialOptions()[_facial];
                    return fh.Length == 0 ? "ui.creation.facial.none" : "ui.creation.facial." + fh;
                case CreationLookField.Hair:
                    return AppearanceCatalog.HairOptions(_gender)[_hair].Length == 0 ? "ui.creation.hair.none" : null;
                default: return null;
            }
        }

        /// <summary>Колір рядка для зразка (волосся, вбрання); null — рядок без зразка.</summary>
        public string Swatch(CreationLookField f)
        {
            if (f == CreationLookField.HairColor) return AppearanceCatalog.HairColorOptions[_hairColor];
            if (f == CreationLookField.OutfitColor) return Build().Outfit.Count > 0 ? Build().Outfit[0].Color : null;
            return null;
        }

        /// <summary>Готовий образ для ядра.</summary>
        public Appearance Build()
        {
            string culture = KitParts.Cultures[_culture];
            var a = AppearanceCatalog.StarterOutfits(culture, _gender)[_outfit].Clone();
            a.Culture = culture;
            a.Gender = _gender;
            a.Hair = AppearanceCatalog.HairOptions(_gender)[_hair];
            a.HairColor = AppearanceCatalog.HairColorOptions[_hairColor];
            a.FacialHair = _gender == Gender.Male ? FacialOptions()[_facial] : "";
            if (_outfitColor >= 0 && a.Outfit.Count > 0)
                a.Outfit[0] = new OutfitPiece(a.Outfit[0].Part, AppearanceCatalog.FabricOptions[_outfitColor]);
            return a;
        }

        private List<string> FacialOptions()
        {
            var list = new List<string> { "" };
            if (_gender == Gender.Male) list.AddRange(KitParts.FacialHair);
            return list;
        }

        private static int IndexOf(IReadOnlyList<string> list, string value)
        {
            for (int i = 0; i < list.Count; i++)
                if (list[i] == value) return i;
            return -1;
        }

        private static bool SameOutfitParts(Appearance a, Appearance b)
        {
            if (a.Outfit.Count != b.Outfit.Count) return false;
            for (int i = 0; i < a.Outfit.Count; i++)
                if (a.Outfit[i].Part != b.Outfit[i].Part) return false;
            return true;
        }
    }
}
