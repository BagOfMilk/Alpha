using UnityEngine;

namespace Game.Gameplay.Characters
{
    /// <summary>
    /// Посилання на FBX модульного набору (Поправка №19): речі статей (<c>kit_m</c>, <c>kit_f</c>) і тіла
    /// культур (<c>body_&lt;стать&gt;_&lt;культура&gt;</c>). Поля заповнює редактор
    /// (<c>Editor/CharacterKitBuilder</c>), гра лише читає.
    /// </summary>
    public sealed class CharacterKitLibrary : MonoBehaviour
    {
        public GameObject KitMale;
        public GameObject KitFemale;
        public GameObject[] Bodies = new GameObject[0];

        public GameObject Kit(string id) => id == "kit_f" ? KitFemale : KitMale;

        public GameObject Body(string id)
        {
            foreach (var b in Bodies)
                if (b != null && b.name == id) return b;
            return null;
        }

        public bool IsComplete => KitMale != null && KitFemale != null && Bodies != null && Bodies.Length > 0;
    }
}
