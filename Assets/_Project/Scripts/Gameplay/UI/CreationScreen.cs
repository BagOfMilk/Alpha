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

        public void Draw(GameShell shell)
        {
            var view = shell.Session.GetProtagonistCreationView();
            var g = shell.ProtagonistGender;

            if (!_nameTouched)
                _name = view.Name ?? UkrainianText.Get("ui.creation.name.default", g);

            var area = Widgets.CenteredRect(760f, 620f);
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
                    for (int i = 0; i < view.AvailableBackgrounds.Count; i++)
                    {
                        string id = view.AvailableBackgrounds[i];
                        bool selected = id == view.BackgroundId;

                        GUILayout.BeginHorizontal(GUI.skin.box);
                        GUILayout.BeginVertical();
                        GUILayout.Label((selected ? "» " : "") + UkrainianText.Get("background." + id + ".label", g), AlphaSkin.SubHeader);
                        GUILayout.Label(UkrainianText.Get("background." + id, g), AlphaSkin.Body);
                        GUILayout.EndVertical();
                        if (!selected && Widgets.SecondaryButton(UkrainianText.Get("ui.common.confirm", g), GUILayout.Width(160f)))
                            shell.TryRun(() => shell.Session.SetProtagonistBackground(id));
                        GUILayout.EndHorizontal();
                    }
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
    }
}
