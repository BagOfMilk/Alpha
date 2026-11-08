using Game.Gameplay.Ring.UI.Menus;
using UnityEngine;

namespace Game.Gameplay.Ring.Stage
{
    // Окремий файл (прогін 08.11.2026): MonoBehaviour мусить жити у файлі зі своєю назвою. У RingMenuAudio.cs Unity не
    // знаходив скрипт, клав у сцену вбудоване посилання (m_Script: {fileID: …} без guid) — і Ring.exe падав на level0
    // («level0 is corrupted… Position out of bounds»).
    /// <summary>
    /// Ассети меню (ТЗ B08): музика трьох варіацій лейтмотиву, десять звуків одного тембру, печатка й канатна рамка.
    /// Прив'язує <c>Editor/RingStageSceneSetup</c> зі <c>Assets/Art/Ring/Audio/Menu</c> і <c>Assets/Art/Ring/Brand</c>.
    /// </summary>
    public sealed class RingMenuAssets : MonoBehaviour
    {
        public AudioClip musicLogin, musicLoading, musicSettings;
        /// <summary>Порядок — як <see cref="MenuSfx"/>: Move, Confirm, Back, Error, Toggle, Slider, Tab, Transition, LoadDone, LoginOk.</summary>
        public AudioClip[] sfx = new AudioClip[10];
        public Texture2D seal, ropeFrame;

        private void Awake()
        {
            RingMenuArt.Seal = seal;
            RingMenuArt.RopeFrame = ropeFrame;
        }
    }
}
