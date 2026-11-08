using System.Collections;
using Game.Gameplay.Ring.Models;
using Game.Gameplay.Ring.UI.Menus;
using UnityEngine;

namespace Game.Gameplay.Ring
{
    /// <summary>
    /// Огляд меню (<c>Ring.exe -ring-menu-shots &lt;тека&gt;</c>): бот проходить вхід, налаштування, титри, діалоги, помилку
    /// сейву, вхід у гру й екран завантаження ТИМ САМИМ шляхом вводу, що й гравець (<see cref="IMenuNavigable.OnMenu"/> —
    /// туди ж приходять клавіатура й геймпад), і знімає кадр кожного стану. Сейв — свій (ring_menu_bot.txt), налаштування —
    /// у пам'яті: гравцеві нічого не змінюється. Наведення мишею бот не імітує (події вказівника йдуть лише від миші).
    /// </summary>
    internal static class MenuShots
    {
        private static IEnumerator Wait(float s) { yield return new WaitForSeconds(s); }

        /// <summary>
        /// Знімок і два кадри очікування: Unity знімає екран у кінці кадру, тож дія в тому самому кадрі потрапила б у знімок
        /// (кадр «звук» показував уже «Керування» — огляд 08.11.2026).
        /// </summary>
        private static IEnumerator Snap(RingGameBootstrap h, string name)
        {
            h.Shot(name);
            yield return null;
            yield return null;
        }

        private static void Nav(RingGameBootstrap h, RingAction a, MenuDevice d = MenuDevice.Keyboard)
        {
            var nav = h.CurrentView as IMenuNavigable;
            if (nav != null) nav.OnMenu(a, d);
        }

        /// <summary>Кадри екрана завантаження на 30 %, 70 % і 100 % (показ іде не швидше мінімального часу, тож усі три є).</summary>
        public static IEnumerator LoadingShots(RingGameBootstrap h)
        {
            bool s30 = false, s70 = false;
            float guard = 0f;
            while (h.CurrentLoading != null && guard < 15f)
            {
                float f = h.CurrentLoading.Progress.Shown;
                if (!s30 && f >= 0.3f) { s30 = true; yield return Snap(h, "loading_30"); }
                if (!s70 && f >= 0.7f) { s70 = true; yield return Snap(h, "loading_70"); }
                if (h.CurrentLoading.CanExit) { yield return Snap(h, "loading_100"); yield break; }
                guard += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        public static IEnumerator Run(RingGameBootstrap h)
        {
            // ---- вхід: поява (рух ≤ 2 с) ----
            float guard = 0f;
            while (!(h.CurrentView is TitleMenuView) && guard < 10f) { guard += Time.unscaledDeltaTime; yield return null; }
            yield return Wait(0.85f);
            yield return Snap(h, "title_intro_word");
            yield return Wait(0.5f);
            yield return Snap(h, "title_intro_plates");
            yield return Wait(1.2f);
            yield return Snap(h, "title_idle_continue");
            // ---- фокус по пунктах (клавіатура) ----
            string[] names = { "new", "settings", "credits", "quit" };
            foreach (var n in names)
            {
                Nav(h, RingAction.Down);
                yield return Wait(0.3f);
                yield return Snap(h, "title_focus_" + n);
            }
            Nav(h, RingAction.Down);                      // по колу — знову «Продовжити»
            yield return Wait(0.3f);
            Nav(h, RingAction.Down, MenuDevice.Gamepad);  // підказки підвалу — геймпад
            yield return Wait(0.3f);
            yield return Snap(h, "title_focus_new_pad_hints");
            // ---- налаштування ----
            Nav(h, RingAction.Down);                      // «Налаштування»
            yield return Wait(0.25f);
            Nav(h, RingAction.Confirm);
            yield return Wait(0.06f);
            yield return Snap(h, "settings_opening");
            yield return Wait(0.7f);
            yield return Snap(h, "settings_graphics");
            Nav(h, RingAction.Down); Nav(h, RingAction.Down);
            yield return Wait(0.25f);
            yield return Snap(h, "settings_graphics_focus_quality");
            Nav(h, RingAction.Right);
            yield return Wait(0.25f);
            yield return Snap(h, "settings_graphics_changed");
            Nav(h, RingAction.TabNext);
            yield return Wait(0.5f);
            yield return Snap(h, "settings_sound");
            Nav(h, RingAction.Down);
            Nav(h, RingAction.Right); Nav(h, RingAction.Right);
            yield return Wait(0.3f);
            yield return Snap(h, "settings_sound_music_up");
            Nav(h, RingAction.TabNext);
            yield return Wait(0.5f);
            yield return Snap(h, "settings_controls");
            Nav(h, RingAction.Confirm);                   // «Вгору» — натисни клавішу
            yield return Wait(0.2f);
            yield return Snap(h, "settings_controls_capture");
            h.MenuInput.Captured = KeyCode.DownArrow;     // як натиснута клавіша: зайнята «Вниз» — дії міняються
            h.MenuInput.Capturing = false;
            yield return Wait(0.3f);
            yield return Snap(h, "settings_controls_swapped");
            Nav(h, RingAction.TabNext);
            yield return Wait(0.5f);
            Nav(h, RingAction.Confirm);                   // «Менше руху» — Так
            yield return Wait(0.25f);
            yield return Snap(h, "settings_access_reduced");
            Nav(h, RingAction.TabNext);
            yield return Wait(0.5f);
            yield return Snap(h, "settings_language");
            Nav(h, RingAction.Back);                      // незбережені зміни — діалог
            yield return Wait(0.45f);
            yield return Snap(h, "settings_dirty_dialog");
            Nav(h, RingAction.Down);                      // «Відкинути»
            yield return Wait(0.2f);
            yield return Snap(h, "settings_dirty_discard_focus");
            Nav(h, RingAction.Confirm);
            yield return Wait(0.6f);
            yield return Snap(h, "title_back_from_settings");
            // ---- кнопки дій у налаштуваннях: «Застосувати» ----
            Nav(h, RingAction.Confirm);                   // фокус — «Налаштування»
            yield return Wait(0.6f);
            Nav(h, RingAction.TabNext);                   // «Звук»
            yield return Wait(0.4f);
            for (int i = 0; i < 5; i++) Nav(h, RingAction.Down);
            yield return Wait(0.2f);
            Nav(h, RingAction.Right); Nav(h, RingAction.Right);
            yield return Wait(0.25f);
            yield return Snap(h, "settings_buttons_focus_apply");
            Nav(h, RingAction.Confirm);
            yield return Wait(0.35f);
            yield return Snap(h, "settings_applied_toast");
            Nav(h, RingAction.Back);
            yield return Wait(0.6f);
            // ---- титри ----
            Nav(h, RingAction.Down);
            yield return Wait(0.2f);
            Nav(h, RingAction.Confirm);
            yield return Wait(0.6f);
            yield return Snap(h, "credits");
            Nav(h, RingAction.Back);
            yield return Wait(0.5f);
            // ---- вихід: діалог ----
            Nav(h, RingAction.Down);
            yield return Wait(0.2f);
            Nav(h, RingAction.Confirm);
            yield return Wait(0.5f);
            yield return Snap(h, "quit_dialog");
            Nav(h, RingAction.Back);                      // «Ні»
            yield return Wait(0.5f);
            // ---- помилка сейву ----
            h.WriteBotSave(false);
            Nav(h, RingAction.Down);                      // по колу — «Продовжити»
            yield return Wait(0.25f);
            Nav(h, RingAction.Confirm);
            yield return Wait(0.15f);
            yield return Snap(h, "title_checking");
            yield return Wait(0.5f);
            yield return Snap(h, "title_error");
            yield return Wait(0.25f);
            yield return Snap(h, "title_error_after_shake");
            // ---- успіх: вхід у гру ----
            h.WriteBotSave(true);
            Nav(h, RingAction.Confirm);
            yield return Wait(0.55f);
            yield return Snap(h, "title_success_leaving");
            guard = 0f;
            while (!h.Wipe.Busy && guard < 3f) { guard += Time.unscaledDeltaTime; yield return null; }
            yield return Wait(0.2f);
            yield return Snap(h, "wipe_cover");
            guard = 0f;
            while ((h.CurrentLoading != null || h.Wipe.Busy) && guard < 20f) { guard += Time.unscaledDeltaTime; yield return null; }
            yield return Wait(1.0f);
            yield return Snap(h, "game_after_load");
            Debug.Log("[Ринг] огляд меню: кадри в " + h.MenuShotsDir);
            h.ExitAfterShots();
        }
    }
}
