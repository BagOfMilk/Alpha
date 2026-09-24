using Game.Gameplay.Text;
using UnityEngine;

namespace Game.Gameplay.UI
{
    /// <summary>Підсумок доби 5 (§37 таблиці «одна гра»): хто живий/поранений/пішов/зрадив, будівлі, гаманець, фракції, розв'язка фіналу; «Грати далі» → FreePlay.</summary>
    public sealed class SummaryScreen
    {
        private Vector2 _scroll;

        public void Draw(GameShell shell)
        {
            var g = shell.ProtagonistGender;
            var summary = shell.Session.GetSummaryView();
            var roster = shell.Session.GetRosterView();

            var area = Widgets.CenteredRect(Screen.width * 0.7f, Screen.height * 0.85f);
            GUILayout.BeginArea(area);
            Widgets.Panel(UkrainianText.Get("summary.title", g), () =>
            {
                _scroll = Widgets.ScrollListBegin(_scroll, GUILayout.ExpandHeight(true));

                // Фікс-ревью (Фаза F, знайдено тур-автоплеєм): Finale.ResolveKey
                // (Core/Story/Finale.cs) навмисно повертає ГОЛИЙ ключ
                // ("best"/"good"/"base"/"worst" — власний коментар методу:
                // "той самий ключ, що озвучують finale.outcome.*"), а таблиця
                // тримає його з префіксом "finale.outcome." — без префікса
                // тут гравець бачив видиму заглушку "[worst]" замість
                // фінального абзацу.
                if (summary != null && !string.IsNullOrEmpty(summary.FinaleOutcomeKey))
                    GUILayout.Label(UkrainianText.Get("finale.outcome." + summary.FinaleOutcomeKey, g), AlphaSkin.Body);

                Widgets.Section(UkrainianText.Get("summary.roster", g), () =>
                {
                    var list = summary?.FinalRoster ?? roster?.Companions;
                    if (list != null)
                        foreach (var c in list)
                            Widgets.LabeledRow(
                                ScreenText.ResolveCompanionName(c.Id, g, roster),
                                ScreenText.CompanionStatusLabel(c.Id, c.Status, g) + " · " + ScreenText.LoyaltyLabel(c.Loyalty, g));
                });

                Widgets.Section(UkrainianText.Get("summary.village", g), () =>
                {
                    if (summary?.BuiltBuildings != null)
                        foreach (var id in summary.BuiltBuildings)
                            GUILayout.Label("• " + UkrainianText.Get("building." + id, g), AlphaSkin.Body);

                    if (summary?.Wallet != null)
                    {
                        Widgets.LabeledRow(UkrainianText.Get("resource.gold", g), summary.Wallet.Gold.ToString());
                        Widgets.LabeledRow(UkrainianText.Get("resource.materials", g), summary.Wallet.Materials.ToString());
                        Widgets.LabeledRow(UkrainianText.Get("resource.food", g), summary.Wallet.Food.ToString());
                    }
                });

                Widgets.Section(UkrainianText.Get("summary.tuhar", g), () =>
                {
                    if (summary?.Factions != null)
                        foreach (var f in summary.Factions)
                            Widgets.LabeledRow(UkrainianText.Get("faction." + f.Id, g), ScreenText.FactionBandLabel(f.Band, g));
                });

                Widgets.ScrollListEnd();

                GUILayout.Space(10f);
                if (Widgets.PrimaryButton(UkrainianText.Get("ui.summary.continue", g)))
                    shell.TryRun(() => shell.Session.AcknowledgeSummary());
            }, GUILayout.Width(Screen.width * 0.7f), GUILayout.Height(Screen.height * 0.85f));
            GUILayout.EndArea();
        }
    }
}
