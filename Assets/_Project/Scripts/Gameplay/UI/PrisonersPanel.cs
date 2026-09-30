using Game.Core.Characters.Creation;
using Game.Core.Session.Views;
using Game.Gameplay.Text;
using UnityEngine;

namespace Game.Gameplay.UI
{
    /// <summary>
    /// Полонені на віче (Поправка №14.2; власник, 29.09.2026: «полон добре бо можна
    /// собі потім його переманити»). Хто в полоні, наскільки готовий перейти (полосою,
    /// без чисел — інваріант 3), чи неспокійний, і три рішення віча: переманити,
    /// викуп, відпустити. Ціна викупу — до кліку (Статут UI-02); «переманити» сіре з
    /// поясненням, поки полонений не готовий або його переманити не можна (UI-04).
    /// Окремий файл, щоб вкладка віча (трек віча) мала лише один рядок виклику.
    /// </summary>
    public static class PrisonersPanel
    {
        public static void Draw(GameShell shell, Gender g)
        {
            var prisoners = shell?.Session?.GetPrisonersView();
            if (prisoners == null || prisoners.Count == 0) return;

            Widgets.Section(UkrainianText.Get("ui.prisoners.title", g), () =>
            {
                if (!prisoners[0].Guarded)
                    GUILayout.Label(UkrainianText.Get("ui.prisoners.unguarded", g), AlphaSkin.Tooltip);

                foreach (var p in prisoners) DrawOne(shell, g, p);
            });
        }

        private static void DrawOne(GameShell shell, Gender g, PrisonerView p)
        {
            string name = UkrainianText.Has("enemy." + p.DisplayNameKey, g)
                ? UkrainianText.Get("enemy." + p.DisplayNameKey, g)
                : p.DisplayNameKey;

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label(name, AlphaSkin.Body);
            GUILayout.Label(UkrainianText.Get("ui.prisoners.disposition." + p.Disposition, g) + " · "
                + UkrainianText.Get("ui.prisoners.restless." + p.Restlessness, g), AlphaSkin.HintLine);

            GUILayout.BeginHorizontal();
            string recruit = UkrainianText.Get("ui.prisoners.recruit", g);
            string prisonerId = p.Id;
            if (p.CanRecruitNow)
            {
                if (Widgets.PrimaryButton(recruit))
                    shell.TryRun(() => shell.Session.RecruitPrisoner(prisonerId));
            }
            else
                Widgets.DisabledButton(recruit, UkrainianText.Get(p.NeverRecruitable ? "ui.prisoners.never" : "ui.prisoners.not_ready", g));

            if (Widgets.SecondaryButton(UkrainianText.Format("ui.prisoners.ransom", g, "gold", p.RansomGold.ToString())))
                shell.TryRun(() => shell.Session.RansomPrisoner(prisonerId));
            if (Widgets.SecondaryButton(UkrainianText.Get("ui.prisoners.release", g)))
                shell.TryRun(() => shell.Session.ReleasePrisoner(prisonerId));
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }
    }
}
