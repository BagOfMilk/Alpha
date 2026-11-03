using System;
using System.Collections.Generic;
using Game.Core.Ring;
using Game.Core.Randomness;
using Game.Gameplay.Combat;
using Game.Gameplay.Ring.Models;
using Game.Gameplay.Ring.UI;
using Game.Gameplay.UI.Toolkit;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.Gameplay.Ring
{
    /// <summary>
    /// Компонент сцени <c>RingFeud</c> (зріз «Ворожнеча»): вантажить дані й рядки з Resources/Ring, перевіряє граф промо,
    /// створює <see cref="RingCampaign"/> із сидованим кубиком (зерно від часу; <c>-ring-seed N</c> фіксує) і показує екран
    /// поточного етапу на UI Toolkit. Правил тут немає — лише показ і клавіші (1–9, Enter/Пробіл).
    /// </summary>
    public sealed class RingFeudBootstrap : MonoBehaviour
    {
        private const string FeudPath = "Ring/feud_hammer";
        private const string StringsPath = "Ring/strings_uk";
        private const string UiStringsPath = "Ring/ui_uk";
        private const string SeedArg = "-ring-seed";

        private RingText _text;
        private FeudScript _script;
        private RingCampaign _campaign;
        private ulong _seed;
        private RingUiContext _ctx;

        private GameObject _host;
        private UIDocument _document;
        private PanelSettings _panel;
        private VisualElement _root;
        private float _appliedScale = -1f;

        private RingScreenView _view;
        private object _viewKey;

        private void Start()
        {
            _seed = ReadSeed();
            if (!BuildUi()) return;
            if (!LoadData()) return;
            NewCampaign();
        }

        private void OnDestroy()
        {
            if (_host != null) UnityEngine.Object.Destroy(_host);
            if (_panel != null) UnityEngine.Object.Destroy(_panel);
        }

        // ---------------- запуск ----------------

        private static ulong ReadSeed()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
            {
                ulong v;
                if (args[i] == SeedArg && ulong.TryParse(args[i + 1], out v)) return v;
            }
            return (ulong)DateTime.UtcNow.Ticks;
        }

        private bool BuildUi()
        {
            var asset = Resources.Load<PanelSettings>(HudToolkitTheme.PanelResourcePath);
            _panel = asset != null ? UnityEngine.Object.Instantiate(asset) : ScriptableObject.CreateInstance<PanelSettings>();
            _panel.name = "RingFeudPanel";
            _panel.scaleMode = PanelScaleMode.ConstantPixelSize;
            _panel.sortingOrder = 1f;
            if (_panel.themeStyleSheet == null)
            {
                var theme = Resources.Load<ThemeStyleSheet>(HudToolkitTheme.ThemeResourcePath);
                if (theme != null) _panel.themeStyleSheet = theme;
            }
            ApplyScale();

            _host = new GameObject("RingFeud (UI Toolkit)");
            _host.SetActive(false);
            _document = _host.AddComponent<UIDocument>();
            _document.panelSettings = _panel;
            _host.SetActive(true);
            _root = _document.rootVisualElement;
            if (_root == null)
            {
                Debug.LogError("[Ринг] UIDocument.rootVisualElement == null");
                return false;
            }
            _root.style.position = Position.Absolute;
            _root.style.left = 0f; _root.style.right = 0f; _root.style.top = 0f; _root.style.bottom = 0f;
            _root.style.backgroundColor = RingUi.Bg;
            return true;
        }

        /// <summary>Масштаб панелі: 1 на 720p, 1,5 на 1080p — розміри екранів задані для еталона 1280×720.</summary>
        private void ApplyScale()
        {
            float s = Mathf.Clamp(Screen.height / 720f, 0.75f, 3f);
            if (Mathf.Abs(s - _appliedScale) < 0.001f) return;
            _appliedScale = s;
            _panel.scale = s;
        }

        private bool LoadData()
        {
            var feud = Resources.Load<TextAsset>(FeudPath);
            var strings = Resources.Load<TextAsset>(StringsPath);
            var ui = Resources.Load<TextAsset>(UiStringsPath);
            if (feud == null || strings == null || ui == null)
            {
                ShowError(new List<string> { "Resources not found: " + FeudPath + ", " + StringsPath + ", " + UiStringsPath });
                return false;
            }
            try
            {
                _text = RingText.FromJson(strings.text, ui.text);
                _script = FeudScriptLoader.Load(feud.text);
            }
            catch (Exception ex)
            {
                ShowError(new List<string> { ex.Message });
                return false;
            }
            var errors = PromoValidator.Validate(_script, _text.AsDictionary());
            if (errors.Count > 0)
            {
                var lines = new List<string>();
                foreach (var e in errors) lines.Add(e.ToString());
                ShowError(lines);
                return false;
            }
            return true;
        }

        private void NewCampaign()
        {
            _campaign = RingCampaign.New(_script, new SeededDiceRoller(_seed));
            _seed += 1;
            _ctx = new RingUiContext { Campaign = _campaign, Text = _text, Restart = NewCampaign };
            _view = null;
            _viewKey = null;
            Sync();
        }

        /// <summary>Плашка помилки даних: у консоль і на екран (грати без валідних даних не даємо).</summary>
        private void ShowError(List<string> lines)
        {
            foreach (var l in lines) Debug.LogError("[Ринг] " + l);
            _root.Clear();
            var box = RingUi.Card(20f);
            box.style.marginLeft = 40f; box.style.marginRight = 40f; box.style.marginTop = 40f; box.style.marginBottom = 40f;
            box.Add(RingUi.Lbl(_text != null ? _text.Get("ui.error.title") : "Ring data error", 26f, RingUi.Bad, true));
            foreach (var l in lines) box.Add(RingUi.Lbl(l, 16f));
            _root.Add(box);
        }

        // ---------------- кадр ----------------

        private void Update()
        {
            if (_campaign == null || _root == null) return;
            ApplyScale();
            Sync();
            if (_view == null) return;
            HandleKeys();
            if (_view != null) _view.Tick(Time.unscaledDeltaTime);
            Sync();
        }

        /// <summary>Екран за етапом кампанії; нове промо, новий матч і нова ворожнеча — нові екрани.</summary>
        private void Sync()
        {
            object key;
            switch (_campaign.Stage)
            {
                case RingStage.Overview:
                case RingStage.MatchPrep:
                    key = "feud:" + _campaign.Stage + ":" + _campaign.PromoIndex;
                    break;
                case RingStage.Promo: key = _campaign.CurrentPromoRunner; break;
                case RingStage.Match: key = _campaign.CurrentMatch; break;
                default: key = "end"; break;
            }
            if (_view != null && Equals(key, _viewKey)) return;
            _viewKey = key;
            switch (_campaign.Stage)
            {
                case RingStage.Overview:
                case RingStage.MatchPrep: _view = new FeudScreenView(_ctx); break;
                case RingStage.Promo: _view = new PromoScreenView(_ctx, _campaign.CurrentPromoRunner); break;
                case RingStage.Match: _view = new MatchScreenView(_ctx, _campaign.CurrentMatch); break;
                default: _view = new EndingScreenView(_ctx); break;
            }
            _root.Clear();
            _root.Add(_view.Root);
            _view.Refresh();
        }

        private void HandleKeys()
        {
            for (int i = 1; i <= 9; i++)
            {
                if (!Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + i - 1))) continue;
                _view.OnDigit(i);
                return;
            }
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space))
                _view.OnConfirm();
            // Esc нічого не ламає: виходу з кампанії зі зрізу нема.
        }
    }
}
