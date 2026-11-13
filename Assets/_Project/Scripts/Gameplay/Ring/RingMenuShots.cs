using System;
using System.Collections;
using Game.Core.Ring.Career;
using Game.Gameplay.Ring.Models;
using Game.Gameplay.Ring.UI.Menus;
using UnityEngine;

namespace Game.Gameplay.Ring
{
    /// <summary>
    /// Огляд меню (<c>Ring.exe -ring-menu-shots &lt;тека&gt;</c>): бот проходить вхід, налаштування, титри, діалоги, помилку
    /// сейву, вхід у гру й екран завантаження, паузу в грі ТИМ САМИМ шляхом вводу, що й гравець (<see cref="IMenuNavigable.OnMenu"/> —
    /// туди ж приходять клавіатура й геймпад), і знімає кадр кожного стану. Сейв — свій (ring_menu_bot.txt), налаштування —
    /// у пам'яті: гравцеві нічого не змінюється. Наведення мишею бот не імітує (події вказівника йдуть лише від миші).
    /// </summary>
    internal static class MenuShots
    {
        /// <summary>Реальний час: під паузою гри (Time.timeScale = 0) звичайне очікування стояло б вічно.</summary>
        private static IEnumerator Wait(float s) { yield return new WaitForSecondsRealtime(s); }

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
            var nav = (h.CurrentPause as IMenuNavigable) ?? (h.CurrentView as IMenuNavigable);   // пауза — поверх екрана
            if (nav != null) nav.OnMenu(a, d);
        }

        /// <summary>
        /// Геймпад на екрані кар'єри (P3, 11.11.2026): дія тим самим шляхом, що A / B / хрестовина, і кадр; у лог — ключ цілі
        /// у фокусі, щоб кадр читався без здогадок. Повертає ключ через <paramref name="key"/>[0].
        /// </summary>
        private static IEnumerator Pad(RingGameBootstrap h, RingAction a, string name, string[] key)
        {
            h.PadNavForShots(a);
            yield return Wait(0.3f);
            string k = h.PadKeyForShots;
            if (key != null) key[0] = k;
            Debug.Log("[Ринг] огляд меню: " + name + " — фокус «" + (k ?? "немає") + "»");
            yield return Snap(h, name);
        }

        /// <summary>Хрестовиною в один бік до краю (фокус перестав рухатись) — кадр на кожну ціль; ← → один раз на перемикачі.</summary>
        private static IEnumerator PadWalk(RingGameBootstrap h, RingAction dir, string prefix, int max)
        {
            var key = new string[1];
            string was = h.PadKeyForShots;
            bool stepped = false;
            for (int i = 0; i < max; i++)
            {
                yield return Pad(h, dir, prefix + "_" + i, key);
                if (key[0] == was) yield break;
                was = key[0];
                if (!stepped && was != null && (was.StartsWith("carousel:") || was.StartsWith("costume")))
                {
                    stepped = true;
                    yield return Pad(h, RingAction.Right, prefix + "_" + i + "_right", key);
                }
            }
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

        /// <summary>
        /// Сцена огляду (<c>-ring-menu-scene &lt;назва&gt;</c>, слот 32, ПМ 13.11.2026) замість повного туру: «Продовжити» з сейву
        /// огляду (нова кар'єра на створенні — або <c>-ring-menu-load &lt;файл&gt;</c>) і кадри одного місця.
        /// <c>create</c> — афіша створення: кожен ґімік екрана × роль × стать (назва за статтю, кегль назви за місцем до плити ролі);
        /// <c>week</c> — тиждень із сейву: стрічки (стать, зона тіла, чемпіон), шухляда розвитку з підказками атрибутів (ціна),
        /// крамниця з підказками тренерів (крок), далі картка шоу (суперник, рядок тіла). Стан кар'єри бот не чіпає — лише фокус.
        /// </summary>
        public static IEnumerator Scene(RingGameBootstrap h, string scene)
        {
            UI.CareerScreenView.ExpandForShots = scene == "week";   // тиждень відкривається з шухлядою (є очки — «Розвиток»)
            float guard = 0f;
            while (!(h.CurrentView is TitleMenuView) && guard < 10f) { guard += Time.unscaledDeltaTime; yield return null; }
            // вступ титулу (≤ 2 с) — перше натискання під час нього лише пропускає вступ (кадри слоту 32: стояли на титулі);
            // тому чекаємо, як повний тур, і тиснемо «Продовжити», доки гра не рушить (перевірка сейву — 0,45 с, далі розріз)
            yield return Wait(2.6f);
            for (int attempt = 0; attempt < 4 && h.OnTitle && h.CurrentLoading == null && !h.Wipe.Busy; attempt++)
            {
                Nav(h, RingAction.Confirm);               // «Продовжити» — фокус титулу, коли є сейв
                guard = 0f;
                while (h.OnTitle && h.CurrentLoading == null && !h.Wipe.Busy && guard < 2.5f) { guard += Time.unscaledDeltaTime; yield return null; }
            }
            guard = 0f;
            while ((h.OnTitle || h.CurrentLoading != null || h.Wipe.Busy) && guard < 25f) { guard += Time.unscaledDeltaTime; yield return null; }
            yield return Wait(1.2f);
            if (h.OnTitle) Debug.LogError("[Ринг] сцена огляду: «Продовжити» не відкрило гру");
            else if (scene == "create") yield return CreatePosters(h);
            else if (scene == "week") yield return WeekScene(h);
            else Debug.LogError("[Ринг] -ring-menu-scene: невідома сцена '" + scene + "' (create, week)");
            UI.CareerScreenView.ExpandForShots = false;
            Debug.Log("[Ринг] сцена огляду " + scene + ": кадри в " + h.MenuShotsDir);
            h.ExitAfterShots();
        }

        /// <summary>Афіша створення: чоловік і героїня × ґіміки екрана × Герой / Лиходій — кадр на кожне поєднання.</summary>
        private static IEnumerator CreatePosters(RingGameBootstrap h)
        {
            var create = h.CurrentView as UI.CreateScreenView;
            if (create == null) { Debug.LogError("[Ринг] сцена create: після «Продовжити» не створення, а " + Name(h)); yield break; }
            int femaleBody = Array.FindIndex(CareerLooks.HeroBodies, CareerLooks.IsFemaleBody);
            foreach (bool female in new[] { false, true })
            {
                h.HeroBody = female ? femaleBody : 0;   // на створенні тіло одразу міняє форми звертання (HeroForms)
                foreach (var g in create.GimmicksForShots())
                    foreach (var r in new[] { CareerRole.Hero, CareerRole.Villain })
                    {
                        create.SetForShots(g, r);
                        yield return Wait(0.4f);
                        yield return Snap(h, "poster_" + (female ? "f" : "m") + "_" + g + "_" + r);
                    }
            }
        }

        /// <summary>Тиждень із сейву: стрічки, підказки розвитку й крамниці фокусом без миші, картка шоу.</summary>
        private static IEnumerator WeekScene(RingGameBootstrap h)
        {
            var week = h.CurrentView as UI.WeekScreenView;
            if (week == null) { Debug.LogError("[Ринг] сцена week: після «Продовжити» не тиждень, а " + Name(h)); yield break; }
            yield return Snap(h, "week");
            // розвиток (з -ring-shot-dev-all — «Усе» розгорнуто): фокус на кожному атрибуті — підказка з ціною, яку спише покупка
            for (int i = 0; i < 5; i++)
            {
                int k = i;
                var t = week.PadForShots(key => key == "dev:attr:" + k);
                if (t == null) { if (i == 0) Debug.LogWarning("[Ринг] сцена week: атрибутів у шухляді немає (без очок розвитку чи без -ring-shot-dev-all)"); break; }
                t.PadHot(true);
                yield return Wait(0.25f);
                yield return Snap(h, "week_dev_attr_" + i);
                t.PadHot(false);
            }
            // крамниця: вкладка «Витрати», фокус на кожному тренері — підказка з кроком цього тренера
            string shop = UI.RingTheme.Caps(h.TextForShots.Get("c.hub.shop"));
            var tab = week.PadForShots(key => key.StartsWith("tab:", StringComparison.Ordinal) && UI.RingTheme.Caps(key.Substring(4)) == shop);
            if (tab == null) Debug.LogError("[Ринг] сцена week: немає вкладки «" + shop + "»");
            else
            {
                if (week.PadForShots(key => key.StartsWith("shop:coach:", StringComparison.Ordinal)) == null) tab.PadActivate();
                yield return Wait(0.5f);
                yield return Snap(h, "week_shop");
                for (int i = 0; i < CareerRules.Coaches.Length; i++)
                {
                    int k = i;
                    var t = week.PadForShots(key => key == "shop:coach:" + k);
                    if (t == null) { Debug.LogError("[Ринг] сцена week: немає тренера " + k + " у крамниці"); break; }
                    t.PadHot(true);
                    yield return Wait(0.25f);
                    yield return Snap(h, "week_shop_coach_" + i);
                    t.PadHot(false);
                }
            }
            // картка шоу: «Почати шоу» з тижня (той самий виклик, що Enter) — суперник, рядок тіла, «переріс щабель»
            h.CurrentView.OnConfirm();
            float guard = 0f;
            while (h.CurrentView is UI.WeekScreenView && guard < 5f) { guard += Time.unscaledDeltaTime; yield return null; }
            yield return Wait(1.0f);
            yield return Snap(h, "after_week_" + Name(h));
        }

        private static string Name(RingGameBootstrap h) { return h.CurrentView == null ? "null" : h.CurrentView.GetType().Name; }

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
            // ---- пауза в грі (B01 §6.2): поява, фокус, налаштування поверх, діалог «На титул?», повернення ----
            h.OpenPauseForShots();
            yield return Wait(0.09f);
            yield return Snap(h, "pause_opening");
            yield return Wait(0.6f);
            yield return Snap(h, "pause_idle");
            Nav(h, RingAction.Down);
            yield return Wait(0.3f);
            yield return Snap(h, "pause_focus_settings");
            Nav(h, RingAction.Confirm);
            yield return Wait(0.7f);
            yield return Snap(h, "pause_settings");
            Nav(h, RingAction.Back);                      // змін немає — одразу назад у паузу
            yield return Wait(0.5f);
            yield return Snap(h, "pause_back_from_settings");
            Nav(h, RingAction.Down);                      // «На титул»
            yield return Wait(0.3f);
            Nav(h, RingAction.Confirm);
            yield return Wait(0.5f);
            yield return Snap(h, "pause_leave_dialog");
            Nav(h, RingAction.Back);                      // «Ні, лишитися»
            yield return Wait(0.4f);
            Nav(h, RingAction.Back);                      // Esc на корені — назад у гру
            yield return Wait(0.08f);
            yield return Snap(h, "pause_closing");
            yield return Wait(0.6f);
            yield return Snap(h, "pause_resumed");
            // ---- плитки створення героя: назва в рядок, бонуси ґіміка не більше двох рядків (ПМ 11.11.2026) ----
            yield return Snap(h, "create_tiles");
            // ---- геймпад без миші на створенні героя (P3, 11.11.2026): перше A лише показує фокус, хрестовина водить, ← → крутить
            //      карусель; натискань, що створюють героя, немає. Ітерація 2 (власник 11.11.2026) — у М1 прапорець вимкнений ----
            var padKey = new string[1];
            bool pad = CareerPadFeature.Enabled;
            if (!pad) Debug.Log("[Ринг] огляд меню: кадри геймпада пропущено — ітерація 2 (CareerPadFeature.Enabled = false)");
            if (pad)
            {
                yield return Pad(h, RingAction.Confirm, "create_pad_focus", padKey);
                if (padKey[0] == null) Debug.LogError("[Ринг] огляд меню: перше A на створенні не показало фокус");
                yield return PadWalk(h, RingAction.Up, "create_pad_up", 10);
                yield return PadWalk(h, RingAction.Down, "create_pad_down", 16);
                h.PadHideForShots();
                yield return Wait(0.3f);
                yield return Snap(h, "create_pad_mouse");
            }
            // ---- тиждень: пауза з рядком «Сезон · Тиждень» і безпечним «На титул?» ----
            UI.CareerScreenView.ExpandForShots = true;   // тиждень відкриється з шухлядою (є очки — «Розвиток», P1-5; інакше «Витрати») — перевірка Esc
            h.CurrentView.OnConfirm();                    // «Почати кар'єру» на створенні героя → тиждень
            yield return Wait(1.6f);
            UI.CareerScreenView.ExpandForShots = false;
            yield return Snap(h, "week_drawer_open");
            // ---- Esc спершу закриває верхнє (шухляду), паузи немає (ПМ 08.11.2026) ----
            h.EscForShots();
            yield return Wait(0.5f);
            if (h.CurrentPause != null) Debug.LogError("[Ринг] огляд меню: Esc на відкритій шухляді відкрив паузу замість закрити шухляду");
            yield return Snap(h, "week_before_pause");
            // ---- тривога травми на плитці «Відновлення» (P2-3, 8c3f16b): за сезон бот травмується не завжди, тож травма
            //      корпусу ставиться лише на кадр і знімається до паузи й збереження ----
            var injury = h.CareerForShots.State.Injury;
            int wasInjury = injury[(int)Game.Core.Ring.Career.BodyZone.Body];
            injury[(int)Game.Core.Ring.Career.BodyZone.Body] = 2;
            h.CurrentView.Refresh();
            yield return Wait(0.4f);
            yield return Snap(h, "week_injury_tile");
            injury[(int)Game.Core.Ring.Career.BodyZone.Body] = wasInjury;
            h.CurrentView.Refresh();
            yield return Wait(0.3f);
            // ---- наведення на «Промо» (ПМ 11.11.2026, У0 власника): плитка з коротшим описом і рядок-підказка шапки — не більше
            //      двох рядків ----
            var week = h.CurrentView as UI.WeekScreenView;
            if (week != null && week.HoverTileForShots(4, true))
            {
                yield return Wait(0.4f);
                yield return Snap(h, "week_hover_promo");
                week.HoverTileForShots(4, false);
                yield return Wait(0.3f);
            }
            else Debug.LogError("[Ринг] огляд меню: на тижні немає плитки «Промо» (ключ 4) — кадр підказки шапки не знято");
            // ---- геймпад без миші на тижні: фокус на плитці, хрестовиною до вкладки шухляди, A відкриває, B закриває (не пауза) ----
            if (pad)
            {
                yield return Pad(h, RingAction.Confirm, "week_pad_focus", padKey);
                yield return Pad(h, RingAction.Right, "week_pad_right", padKey);
                bool onTab = false;
                for (int i = 0; i < 6 && !onTab; i++)
                {
                    yield return Pad(h, RingAction.Up, "week_pad_up_" + i, padKey);
                    onTab = padKey[0] != null && padKey[0].StartsWith("tab:");
                }
                if (onTab)
                {
                    yield return Pad(h, RingAction.Confirm, "week_pad_drawer", padKey);
                    yield return PadWalk(h, RingAction.Up, "week_pad_drawer_up", 6);   // шухляда росте вгору над рядом вкладок
                    yield return Pad(h, RingAction.Back, "week_pad_back", padKey);
                    if (h.CurrentPause != null) Debug.LogError("[Ринг] огляд меню: B на відкритій шухляді відкрив паузу замість закрити шухляду");
                }
                else Debug.LogError("[Ринг] огляд меню: хрестовина вгору з плиток тижня не дійшла до вкладки шухляди");
                h.PadHideForShots();
                yield return Wait(0.3f);
                yield return Snap(h, "week_pad_mouse");
            }
            h.OpenPauseForShots();
            yield return Wait(0.6f);
            yield return Snap(h, "pause_week");
            Nav(h, RingAction.Down); Nav(h, RingAction.Down);
            yield return Wait(0.3f);
            Nav(h, RingAction.Confirm);
            yield return Wait(0.5f);
            yield return Snap(h, "pause_leave_dialog_week");
            // ---- «На титул» з паузи: збереження, розріз, титул ----
            Nav(h, RingAction.Confirm);                   // «Так, на титул»: на тижні нічого не втрачається — фокус уже на ньому
            guard = 0f;
            while ((h.CurrentPause != null || h.Wipe.Busy) && guard < 5f) { guard += Time.unscaledDeltaTime; yield return null; }
            yield return Wait(2.2f);                      // поява титулу ≤ 2 с
            yield return Snap(h, "title_after_pause");
            Debug.Log("[Ринг] огляд меню: кадри в " + h.MenuShotsDir);
            h.ExitAfterShots();
        }
    }
}
