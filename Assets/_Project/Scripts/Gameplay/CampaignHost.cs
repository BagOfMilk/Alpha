using Game.Core.Balance;
using Game.Core.Economy;
using Game.Core.Saves;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Хост кампании с быстрым сейвом/лоадом как в обычных играх (US-16.1):
    /// <b>F5</b> — quicksave, <b>F9</b> — quickload (legacy Input — у проекта
    /// Active Input Handling = Input Manager). Повесь на пустой GameObject, нажми
    /// Play: на экране подсказка и сводка; правь состояние (через другие демо или
    /// инспектор), жми F5/F9 — кампания пишется в JSON и читается обратно.
    /// </summary>
    public sealed class CampaignHost : MonoBehaviour
    {
        [Tooltip("Необязательно. Если пусто — дефолтный баланс из кода.")]
        public BalanceConfigAsset balanceAsset;
        public KeyCode quickSaveKey = KeyCode.F5;
        public KeyCode quickLoadKey = KeyCode.F9;

        /// <summary>Текущая сессия кампании (то, что сохраняется/загружается).</summary>
        public Campaign Session { get; private set; }

        private string _status = "";

        private void Awake()
        {
            var cfg = balanceAsset != null ? balanceAsset.ToConfig() : new BalanceConfig();
            Session = Campaign.NewGame(cfg);
            _status = "Новая игра. Файл сейва: " + SaveSerializer.QuickSavePath;
        }

        private void Update()
        {
            if (Input.GetKeyDown(quickSaveKey)) QuickSave();
            else if (Input.GetKeyDown(quickLoadKey)) QuickLoad();
        }

        [ContextMenu("Quick Save (F5)")]
        public void QuickSave()
        {
            if (!Session.CanQuickSave)
            {
                Log("⛔ Быстрый сейв заблокирован: вылазка под айронменом (US-16.1)");
                return;
            }
            var data = SaveSystem.Capture(Session);
            SaveSerializer.SaveToFile(data, SaveSerializer.QuickSavePath);
            Log($"💾 Quicksave — день {data.day}, золото {data.gold}, ростер {data.companions.Count}");
        }

        [ContextMenu("Quick Load (F9)")]
        public void QuickLoad()
        {
            var data = SaveSerializer.LoadFromFile(SaveSerializer.QuickSavePath);
            if (data == null) { Log("Нет файла быстрого сейва — сначала сохрани (F5)."); return; }
            Session = SaveSystem.Restore(data, Session.Cfg, ContentCatalog.Default());
            Log($"📂 Quickload — день {Session.Base.CurrentDay}, золото {Session.Base.Resources.Get(ResourceType.Gold)}");
        }

        private void Log(string msg)
        {
            _status = msg;
            Debug.Log("[CampaignHost] " + msg);
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 760, 124), GUI.skin.box);
            GUILayout.Label("Отладочный хост (не игра): <b>F5</b> — быстрое сохранение   ·   <b>F9</b> — быстрая загрузка");
            GUILayout.Label(_status);
            if (Session != null)
                GUILayout.Label($"День {Session.Base.CurrentDay} · золото " +
                                $"{Session.Base.Resources.Get(ResourceType.Gold)} · ростер {Session.Roster.Count}" +
                                (Session.Ironman ? "  ·  АЙРОНМЕН" : ""));
            // Сцена входа в игру — Campaign; отсюда до неё был тупик (US-17: билд
            // стартовал в этом демо и не давал попасть в настоящую кампанию).
            if (GUILayout.Button("Играть кампанию →"))
                UnityEngine.SceneManagement.SceneManager.LoadScene("Campaign");
            GUILayout.EndArea();
        }
    }
}
