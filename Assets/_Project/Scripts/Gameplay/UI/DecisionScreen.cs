using Game.Core.Loop;
using Game.Core.Session.Views;
using Game.Gameplay.Text;
using UnityEngine;

namespace Game.Gameplay.UI
{
    /// <summary>
    /// Модалка точки рішення (§4 рядок "Decision modal for each pending offer
    /// in the queue"): обидва шляхи з порогом/кандидатом/очікуваною полосою
    /// (інваріант 8 — поріг видно заздалегідь), позначка кризи. Черга рішень
    /// фази (§1.1 П10) означає, що після <c>ResolveIncident</c> сесія може
    /// одразу дати НАСТУПНИЙ офер того ж кадру — DecisionScreen про це не
    /// знає, просто перечитує <c>GetPendingOffer()</c> щокадру.
    /// </summary>
    public sealed class DecisionScreen
    {
        public void DrawBody(GameShell shell)
        {
            var g = shell.ProtagonistGender;
            var offer = shell.Session.GetPendingOffer();
            if (offer == null)
            {
                GUILayout.Label(UkrainianText.Get("ui.common.empty", g), AlphaSkin.Body);
                return;
            }

            if (offer.IsCrisis)
                Widgets.Badge("!", AlphaSkin.Danger);

            // Фікс-ревью (Фаза F, знайдено тур-автоплеєм): PendingOfferView.
            // TopicId ВЖЕ несе префікс "incident." (DefaultIncidents.cs/
            // OpeningContent.cs: кожен запис — `TopicId = "incident.<id>"`),
            // а таблиця тримає ключі як "incident.<id>.title" (один префікс,
            // без подвоєння). Старий рядок додавав "incident." ЗНОВУ —
            // "incident.incident.pass_vanguard.title" ніколи не існував, тож
            // МОДАЛКА КОЖНОГО рішення в грі показувала сирий TopicId
            // ("incident.pass_vanguard") замість перекладеного заголовка.
            string titleKey = offer.TopicId + ".title";
            GUILayout.Label(UkrainianText.Has(titleKey, g) ? UkrainianText.Get(titleKey, g) : offer.TopicId, AlphaSkin.Body);
            GUILayout.Space(10f);

            if (offer.Options != null)
                foreach (var option in offer.Options)
                    DrawOption(shell, option, g);
        }

        private static void DrawOption(GameShell shell, DecisionOptionView option, Game.Core.Characters.Creation.Gender g)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label(ScreenText.DecisionOptionLine(option, g), AlphaSkin.Body);

            bool bloody = option.Path == IncidentPathView.Bloody;
            string label = bloody ? UkrainianText.Get("ui.decision.path.bloody", g) : UkrainianText.Get("ui.decision.path.quiet", g);

            bool clicked = bloody
                ? Widgets.DangerButton(label, GUILayout.Width(220f))
                : Widgets.PrimaryButton(label, GUILayout.Width(220f));

            if (clicked)
            {
                var path = bloody ? IncidentPath.Bloody : IncidentPath.Quiet;
                shell.TryRun(() => shell.Session.ResolveIncident(path));
            }
            GUILayout.EndVertical();
        }
    }
}
