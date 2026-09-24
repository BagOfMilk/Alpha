using System.Collections.Generic;
using Game.Gameplay.UI;
using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Пакет E2 — портрети: офскрін-камера рендерить живу модель персонажа
    /// (перефарбовану тим самим детермінованим ключем, що арена —
    /// <see cref="BattleArenaView.CharacterTint"/>) у <see cref="RenderTexture"/>,
    /// зчитує в кешований <see cref="Texture2D"/> на id. PNG-оверрайд
    /// (<c>Resources/Portraits/&lt;id&gt;.png</c>) має пріоритет над рендером;
    /// відсутність і моделі, і PNG повертає <c>null</c> — фолбек іменної
    /// заглушки малює оболонка (E1b, контракт <c>Presenters.cs</c>).
    ///
    /// ЛІНТ-ВИКЛЮЧЕНО — та сама причина, що <see cref="BattleArenaController"/>
    /// (Renderer/Camera/RenderTexture заглушка чесно не покриває).
    ///
    /// РЕНДЕР: <c>Camera.targetTexture</c> + <c>camera.Render()</c> — підхід,
    /// явно дозволений для офскрін-камер у URP 6.x цим самим пакетом завдання
    /// (простіший за <c>RenderPipeline.SubmitRenderRequest</c>, який
    /// використовує <c>VillageShowcase.Capture</c>, і не тягне нову залежність
    /// <c>Unity.RenderPipelines.Universal.Runtime</c> у спільний
    /// <c>Game.Gameplay.asmdef</c> — той файл лежить поза володінням пакета
    /// E2 і його паралельно можуть правити інші пакети). ВІДКРИТЕ ПИТАННЯ
    /// (див. звіт пакета E2): коментар <c>VillageShowcase.Capture</c>
    /// документує, що **пакетовщик SRP-batcher у batch-режимі не оновлює
    /// буфери матеріалів між об'єктами** — контрольний кадр із трьох кубів
    /// вийшов одним кольором. Якщо колись портрети/арена підуть під
    /// автоматичний скріншот у <c>-batchmode</c> (сьогоднішній автопрогін
    /// цього не робить — лише грає GameSession), і кольори виявляться
    /// однаковими, рішення — те саме, що в <c>VillageShowcase</c>: тимчасово
    /// вимкнути <c>UniversalRenderPipelineAsset.useSRPBatcher</c> на час
    /// рендера (і тоді таки додати посилання на пакет у asmdef).
    /// </summary>
    public sealed class PortraitRig : MonoBehaviour, IPortraitProvider
    {
        public GameObject[] MaleCharacterPrefabs = new GameObject[0];
        public GameObject[] FemaleCharacterPrefabs = new GameObject[0];
        public int TextureSize = 512;

        private Camera _camera;
        private Transform _stage;
        private GameObject _currentModel;
        private readonly Dictionary<string, Texture2D> _cache = new Dictionary<string, Texture2D>();

        public Texture2D GetPortrait(string characterId)
        {
            if (string.IsNullOrEmpty(characterId)) return null;
            if (_cache.TryGetValue(characterId, out var cached)) return cached; // може бути null — і це теж кешований результат

            var overrideTex = Resources.Load<Texture2D>("Portraits/" + characterId);
            if (overrideTex != null)
            {
                _cache[characterId] = overrideTex;
                return overrideTex;
            }

            var rendered = RenderPortrait(characterId);
            _cache[characterId] = rendered;
            return rendered;
        }

        private Texture2D RenderPortrait(string characterId)
        {
            EnsureRig();
            var prefab = PickPrefab(characterId);
            if (prefab == null || _camera == null) return null;

            if (_currentModel != null) Destroy(_currentModel);
            _currentModel = Instantiate(prefab, _stage);
            _currentModel.transform.localPosition = Vector3.zero;
            _currentModel.transform.localRotation = Quaternion.Euler(0f, 200f, 0f);

            TintModel(_currentModel, characterId);

            var rt = new RenderTexture(TextureSize, TextureSize, 16);
            var previousTarget = _camera.targetTexture;
            _camera.targetTexture = rt;
            _camera.Render();

            var previousActive = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, TextureSize, TextureSize), 0, 0);
            tex.Apply();
            RenderTexture.active = previousActive;

            _camera.targetTexture = previousTarget;
            rt.Release();
            Destroy(rt);

            return tex;
        }

        /// <summary>Той самий детермінований палітровий ключ, що й арена (§BattleArenaView) — портрет і бойова модель того самого персонажа завжди одного кольору.</summary>
        private static void TintModel(GameObject model, string characterId)
        {
            var palette = BattleArenaView.CharacterTint("u_" + characterId, "Player", characterId);
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", new Color(palette.R, palette.G, palette.B));
            foreach (var r in model.GetComponentsInChildren<Renderer>()) r.SetPropertyBlock(block);
        }

        private GameObject PickPrefab(string characterId)
        {
            bool female = string.Equals(characterId, "myroslava", System.StringComparison.Ordinal);
            var pool = female ? FemaleCharacterPrefabs : MaleCharacterPrefabs;
            if (pool == null || pool.Length == 0) return null;
            int index = (int)(BattleArenaView.Hash01(characterId) * pool.Length);
            if (index >= pool.Length) index = pool.Length - 1;
            return pool[index];
        }

        /// <summary>
        /// Сцена-станок далеко під основною сценою (Y=-400) — окрема камера,
        /// що нічого не бачить із решти гри, і навпаки. Камера вимкнена
        /// (<c>enabled=false</c>): рендеримо вручну по кадру, не щотик.
        /// </summary>
        private void EnsureRig()
        {
            if (_camera != null) return;

            var stageGo = new GameObject("PortraitStage");
            stageGo.transform.SetParent(transform, false);
            stageGo.transform.position = new Vector3(0f, -400f, 0f);
            _stage = stageGo.transform;

            var camGo = new GameObject("PortraitCamera");
            camGo.transform.SetParent(_stage, false);
            camGo.transform.localPosition = new Vector3(0f, 1.1f, -2.2f);
            camGo.transform.localRotation = Quaternion.Euler(6f, 0f, 0f);

            _camera = camGo.AddComponent<Camera>();
            _camera.orthographic = false;
            _camera.fieldOfView = 24f;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.08f, 0.07f, 0.06f, 1f);
            _camera.nearClipPlane = 0.05f;
            _camera.farClipPlane = 20f;
            _camera.enabled = false;

            var lightGo = new GameObject("PortraitLight");
            lightGo.transform.SetParent(_stage, false);
            lightGo.transform.localRotation = Quaternion.Euler(35f, -25f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
        }
    }
}
