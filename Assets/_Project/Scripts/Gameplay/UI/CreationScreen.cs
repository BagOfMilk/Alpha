using Game.Core.Characters.Creation;
using Game.Gameplay.Text;
using UnityEngine;

namespace Game.Gameplay.UI
{
    /// <summary>Швидкий екран створення протагоніста (R12): ім'я, рід, 3 преси передісторії з коротким описом.</summary>
    public sealed class CreationScreen
    {
        private string _name;
        private bool _nameTouched;
        private Vector2 _backgroundScroll;

        public void Draw(GameShell shell)
        {
            var view = shell.Session.GetProtagonistCreationView();
            var g = shell.ProtagonistGender;

            if (!_nameTouched)
                _name = view.Name ?? UkrainianText.Get("ui.creation.name.default", g);

            // Фікс-ревью (major, знайдено тур-автоплеєм, раунд 2): 620 не
            // вистачало навіть із скролом — виміряно по реальному скріншоту:
            // три картки передісторії разом потребують ~390px, а решта вмісту
            // (заголовок/ім'я/рід/заголовок списку/кнопка) — ще ~320px. 740
            // (з запасом) дає скролу піти на 400px і все одно лишити
            // "Вирушати" видимою, а не притиснутою рівно до нижньої межі.
            float panelHeight = Clamp(Screen.height - 60f, 480f, 740f);
            var area = Widgets.CenteredRect(760f, panelHeight);
            GUILayout.BeginArea(area);
            Widgets.Panel(UkrainianText.Get("ui.creation.title", g), () =>
            {
                Widgets.Section(UkrainianText.Get("ui.creation.name", g), () =>
                {
                    string next = GUILayout.TextField(_name, GUILayout.Width(400f));
                    if (next != _name)
                    {
                        _name = next;
                        _nameTouched = true;
                        shell.TryRun(() => shell.Session.SetProtagonistName(_name));
                    }
                });

                Widgets.Section(UkrainianText.Get("ui.creation.gender", g), () =>
                {
                    GUILayout.BeginHorizontal();
                    if (Widgets.TabButton(UkrainianText.Get("ui.creation.gender.male", g), view.Gender == Gender.Male, GUILayout.Width(160f)))
                        SetGender(shell, Gender.Male);
                    if (Widgets.TabButton(UkrainianText.Get("ui.creation.gender.female", g), view.Gender == Gender.Female, GUILayout.Width(160f)))
                        SetGender(shell, Gender.Female);
                    GUILayout.EndHorizontal();
                });

                Widgets.Section(UkrainianText.Get("ui.creation.background.title", g), () =>
                {
                    // Фікс-ревью (major, знайдено тур-автоплеєм): три картки
                    // передісторії + ім'я/рід + оцей заголовок разом не влазили
                    // у фіксовану висоту панелі (620px) — «Вирушати» нижче
                    // просто обрізався за межею BeginArea, без жодного скролу.
                    // Список тепер — свій скрол-контейнер фіксованої висоти,
                    // кнопка «Вирушати» лишається поза ним і завжди видима.
                    _backgroundScroll = Widgets.ScrollListBegin(_backgroundScroll, GUILayout.Height(400f));
                    for (int i = 0; i < view.AvailableBackgrounds.Count; i++)
                    {
                        string id = view.AvailableBackgrounds[i];
                        bool selected = id == view.BackgroundId;

                        GUILayout.BeginHorizontal(GUI.skin.box);
                        GUILayout.BeginVertical();
                        GUILayout.Label((selected ? "» " : "") + UkrainianText.Get("background." + id + ".label", g), AlphaSkin.SubHeader);
                        GUILayout.Label(UkrainianText.Get("background." + id, g), AlphaSkin.Body);
                        GUILayout.EndVertical();
                        // Фікс-ревью (major): обрана картка раніше не мала тут
                        // жодної кнопки — гравець, задоволений вже підсвіченим
                        // за замовчуванням пресетом, не бачив, ЧИМ саме його
                        // підтвердити (лише клік по ІНШІЙ картці і обирав, і
                        // одразу фіналізував ІНШИЙ вибір). Задизейблений
                        // індикатор замість порожнечі — картка читається
                        // однаково, обрана чи ні; сам вибір фіналізує спільна
                        // «Вирушати» нижче, поза скролом.
                        if (selected)
                            Widgets.DisabledButton(UkrainianText.Get("ui.creation.background.selected", g), null, GUILayout.Width(160f));
                        else if (Widgets.SecondaryButton(UkrainianText.Get("ui.common.confirm", g), GUILayout.Width(160f)))
                            shell.TryRun(() => shell.Session.SetProtagonistBackground(id));
                        GUILayout.EndHorizontal();
                    }
                    Widgets.ScrollListEnd();
                });

                GUILayout.Space(12f);
                if (Widgets.PrimaryButton(UkrainianText.Get("ui.creation.confirm", g)))
                {
                    shell.TryRun(() => shell.Session.ConfirmCreation());
                    _nameTouched = false;
                }
            }, GUILayout.Width(760f));
            GUILayout.EndArea();
        }

        private static void SetGender(GameShell shell, Gender gender)
        {
            shell.TryRun(() => shell.Session.SetProtagonistGender(gender));
            shell.ProtagonistGender = gender;
        }

        // Немає Mathf у заглушці лінту (той самий прийом, що BattleHudScreen.Clamp/SceneScreen.Mathf01Clamp).
        private static float Clamp(float value, float min, float max) => value < min ? min : (value > max ? max : value);
    }
}
