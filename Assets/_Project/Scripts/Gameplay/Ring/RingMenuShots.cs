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
                if (!s30 && f >= 0.3f) { s30 = true; h.Shot("loading_30"); }
                if (!s70 && f >= 0.7f) { s70 = true; h.Shot("loading_70"); }
                if (h.CurrentLoading.CanExit) { h.Shot("loading_100"); yield break; }
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
            h.Shot("title_intro_word");
            yield return Wait(0.5f);
            h.Shot("title_intro_plates");
            yield return Wait(1.2f);
            h.Shot("title_idle_continue");
            // ---- фокус по пунктах (клавіатура) ----
            string[] names = { "new", "settings", "credits", "quit" };
            foreach (var n in names)
            {
                Nav(h, RingAction.Down);
                yield return Wait(0.3f);
                h.Shot("title_focus_" + n);
            }
            Nav(h, RingAction.Down);                      // по колу — знову «Продовжити»
            yield return Wait(0.3f);
            Nav(h, RingAction.Down, MenuDevice.Gamepad);  // підказки підвалу — геймпад
            yield return Wait(0.3f);
            h.Shot("title_focus_new_pad_hints");
            // ---- налаштування ----
            Nav(h, RingAction.Down);                      // «Налаштування»
            yield return Wait(0.25f);
            Nav(h, RingAction.Confirm);
            yield return Wait(0.06f);
            h.Shot("settings_opening");
            yield return Wait(0.7f);
            h.Shot("settings_graphics");
            Nav(h, RingAction.Down); Nav(h, RingAction.Down);
            yield return Wait(0.25f);
            h.Shot("settings_graphics_focus_quality");
            Nav(h, RingAction.Right);
            yield return Wait(0.25f);
            h.Shot("settings_graphics_changed");
            Nav(h, RingAction.TabNext);
            yield return Wait(0.5f);
            h.Shot("settings_sound");
            Nav(h, RingAction.Down);
            Nav(h, RingAction.Right); Nav(h, RingAction.Right);
            yield return Wait(0.3f);
            h.Shot("settings_sound_music_up");
            Nav(h, RingAction.TabNext);
            yield return Wait(0.5f);
            h.Shot("settings_controls");
            Nav(h, RingAction.Confirm);                   // «Вгору» — натисни клавішу
            yield return Wait(0.2f);
            h.Shot("settings_controls_capture");
            h.MenuInput.Captured = KeyCode.DownArrow;     // як натиснута клавіша: зайнята «Вниз» — дії міняються
            h.MenuInput.Capturing = false;
            yield return Wait(0.3f);
            h.Shot("settings_controls_swapped");
            Nav(h, RingAction.TabNext);
            yield return Wait(0.5f);
            Nav(h, RingAction.Confirm);                   // «Менше руху» — Так
            yield return Wait(0.25f);
            h.Shot("settings_access_reduced");
            Nav(h, RingAction.TabNext);
            yield return Wait(0.5f);
            h.Shot("settings_language");
            Nav(h, RingAction.Back);                      // незбережені зміни — діалог
            yield return Wait(0.45f);
            h.Shot("settings_dirty_dialog");
            Nav(h, RingAction.Down);                      // «Відкинути»
            yield return Wait(0.2f);
            h.Shot("settings_dirty_discard_focus");
            Nav(h, RingAction.Confirm);
            yield return Wait(0.6f);
            h.Shot("title_back_from_settings");
            // ---- кнопки дій у налаштуваннях: «Застосувати» ----
            Nav(h, RingAction.Confirm);                   // фокус — «Налаштування»
            yield return Wait(0.6f);
            Nav(h, RingAction.TabNext);                   // «Звук»
            yield return Wait(0.4f);
            for (int i = 0; i < 5; i++) Nav(h, RingAction.Down);
            yield return Wait(0.2f);
            Nav(h, RingAction.Right); Nav(h, RingAction.Right);
            yield return Wait(0.25f);
            h.Shot("settings_buttons_focus_apply");
            Nav(h, RingAction.Confirm);
            yield return Wait(0.35f);
            h.Shot("settings_applied_toast");
            Nav(h, RingAction.Back);
            yield return Wait(0.6f);
            // ---- титри ----
            Nav(h, RingAction.Down);
            yield return Wait(0.2f);
            Nav(h, RingAction.Confirm);
            yield return Wait(0.6f);
            h.Shot("credits");
            Nav(h, RingAction.Back);
            yield return Wait(0.5f);
            // ---- вихід: діалог ----
            Nav(h, RingAction.Down);
            yield return Wait(0.2f);
            Nav(h, RingAction.Confirm);
            yield return Wait(0.5f);
            h.Shot("quit_dialog");
            Nav(h, RingAction.Back);                      // «Ні»
            yield return Wait(0.5f);
            // ---- помилка сейву ----
            h.WriteBotSave(false);
            Nav(h, RingAction.Down);                      // по колу — «Продовжити»
            yield return Wait(0.25f);
            Nav(h, RingAction.Confirm);
            yield return Wait(0.15f);
            h.Shot("title_checking");
            yield return Wait(0.5f);
            h.Shot("title_error");
            yield return Wait(0.25f);
            h.Shot("title_error_after_shake");
            // ---- успіх: вхід у гру ----
            h.WriteBotSave(true);
            Nav(h, RingAction.Confirm);
            yield return Wait(0.55f);
            h.Shot("title_success_leaving");
            guard = 0f;
            while (!h.Wipe.Busy && guard < 3f) { guard += Time.unscaledDeltaTime; yield return null; }
            yield return Wait(0.2f);
            h.Shot("wipe_cover");
            guard = 0f;
            while ((h.CurrentLoading != null || h.Wipe.Busy) && guard < 20f) { guard += Time.unscaledDeltaTime; yield return null; }
            yield return Wait(1.0f);
            h.Shot("game_after_load");
            Debug.Log("[Ринг] огляд меню: кадри в " + h.MenuShotsDir);
            h.ExitAfterShots();
        }
    }
}
