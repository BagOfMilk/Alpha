using UnityEngine;

namespace Game.Gameplay.UI
{
    /// <summary>
    /// Налаштування «Камера в розмові» (розбір BG3, 08.10.2026: крупні плани в розмові вимикаються в налаштуваннях):
    /// «Крупно» — камера на того, хто говорить (<c>DialogueStage</c>), «Як у селі» — камера лишається на загальному
    /// плані, вікно діалогу те саме. Перемикач у меню паузи, зберігається між запусками; типово — крупно.
    /// </summary>
    public static class DialogueCameraSetting
    {
        private const string Key = "dialogue_closeups";

        public static bool CloseUps
        {
            get => PlayerPrefs.GetInt(Key, 1) != 0;
            set
            {
                PlayerPrefs.SetInt(Key, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }
    }
}
