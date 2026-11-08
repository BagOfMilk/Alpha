using UnityEngine;

namespace Game.Gameplay.Ring.UI.Menus
{
    /// <summary>
    /// Що меню (вхід, налаштування, завантаження) просить у хоста гри (<c>RingGameBootstrap</c>). Окремо від
    /// <see cref="ICareerHost"/>: інші екрани кар'єри цього не потребують.
    /// </summary>
    public interface IMenuHost
    {
        bool HasSave { get; }

        /// <summary>Підсумок сейву для пластини «Продовжити» («Сезон 1 · Тиждень 3»); null — не прочитався.</summary>
        string SaveSummary { get; }

        /// <summary>Перевірити сейв без зміни гри (стан «перевірка» входу). false — ключ тексту помилки в <paramref name="errorKey"/>.</summary>
        bool CheckSave(out string errorKey);

        /// <summary>Успіх входу: перехід (розріз) → екран завантаження → гра.</summary>
        void StartGame(bool continueSave);

        void Quit();

        RingSettingsRuntime Settings { get; }

        RingMenuInput MenuInput { get; }

        /// <summary>Налаштування застосовано: хост перечитує клавіші, масштаб UI.</summary>
        void SettingsApplied();

        /// <summary>Версія збірки для підвалу.</summary>
        string Version { get; }
    }

    /// <summary>Бренд-зображення меню (печатка, канатна рамка) — ставить Unity-компонент сцени <c>RingMenuAssets</c>.</summary>
    public static class RingMenuArt
    {
        public static Texture2D Seal;
        public static Texture2D RopeFrame;
    }
}
