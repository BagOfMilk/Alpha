using System.Collections.Generic;
using Game.Core.Characters.Creation;
using Game.Core.Session;
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

        /// <summary>
        /// Рід протагоніста — <c>IPortraitProvider.GetPortrait</c> не приймає
        /// сесію (фіксована сигнатура шва Presenters.cs), тож
        /// <see cref="BattleArenaController.Enter"/> (сусідній компонент на
        /// тому самому <c>ArenaRoot</c>) виставляє це поле з
        /// <c>GameSession.GetProtagonistCreationView().Gender</c> перед боєм.
        /// За замовчуванням — Male, як і в самому фасаді до створення персонажа.
        /// </summary>
        public Gender ProtagonistGender = Gender.Male;

        /// <summary>
        /// Фікс-ревью (блокер): шар 30 (незайнятий у TagManager.asset — усі
        /// шари 8..31 без імені) ізолює світло/камеру станка від решти сцени.
        /// Без цього обидва <c>Light</c> станка (напрямні — освітлюють УСЮ
        /// сцену за напрямком, незалежно від позиції) підсвічували б заразом
        /// хаб/арену боєвки нагорі: cullingMask на камері й на світлі + цей
        /// шар на щойно заспавненій моделі (і всіх її дітях) тримають рендер
        /// станка повністю відрізаним від решти гри — так само, як YOffset=-400
        /// вже ізолює його просторово для самої камери.
        /// </summary>
        private const int StageLayer = 30;

        private Camera _camera;
        private Transform _stage;
        private GameObject _currentModel;
        private readonly Dictionary<string, Texture2D> _cache = new Dictionary<string, Texture2D>();

        /// <summary>
        /// Фаза F знахідка: перший запит нового id не рендерить одразу —
        /// накопичується тут, рендер іде з <see cref="LateUpdate"/> (див. її
        /// коментар — <c>Camera.Render()</c> не можна кликати з середини
        /// OnGUI під URP).
        /// </summary>
        private readonly HashSet<string> _pendingRenders = new HashSet<string>();

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

            // Фікс-ревью (Фаза F, знайдено тур-автоплеєм): цей метод кличе
            // SceneScreen.DrawPortrait ЗСЕРЕДИНИ GameShell.OnGUI — того самого
            // кадрового вікна, у якому основна камера ще НЕ завершила Submit.
            // Синхронний виклик _camera.Render() тут кидав
            // InvalidOperationException ("UniversalCameraData has already
            // been created") — URP не дозволяє реєнтерабельний Camera.Render()
            // усередині рендеру іншої камери. Замість негайного рендеру —
            // черга: цей кадр повертаємо null (SceneScreen малює іменну
            // заглушку — не порожньо), а сам рендер іде з LateUpdate()
            // (окрема фаза кадру, поза Submit), портрет з'являється з
            // наступного кадру.
            _pendingRenders.Add(characterId);
            return null;
        }

        private void LateUpdate()
        {
            if (_pendingRenders.Count == 0) return;
            foreach (var id in _pendingRenders)
            {
                if (_cache.ContainsKey(id)) continue;
                _cache[id] = RenderPortrait(id);
            }
            _pendingRenders.Clear();
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
            SetLayerRecursively(_currentModel);

            TintModel(_currentModel, characterId);
            FrameCameraOnModel(_currentModel);

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

        /// <summary>
        /// Фікс-ревью (блокер, знайдено тур-автоплеєм): камера рахувалась на
        /// один вигаданий зріст моделі наперед (фіксована позиція/FOV) — на
        /// різних Kenney-моделях (Mini Characters, зріст різниться) це давало
        /// або суцільне тло, або ледь верхівку голови в кадрі (скріншот
        /// "лише верхівка голови, обрізана по підборіддю, на чорному тлі").
        /// Кадруємо по РЕАЛЬНИХ межах щойно заспавненої моделі (Renderer.bounds
        /// охоплює всіх дітей одразу після Instantiate, до першого Render) —
        /// «бюст» (верхні ~42% зросту — голова й плечі) завжди влучає в кадр,
        /// хоч би яку модель з пулу підібрав <see cref="PickPrefab"/>.
        /// </summary>
        private void FrameCameraOnModel(GameObject model)
        {
            if (_camera == null || model == null) return;

            var renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            if (bounds.size.y <= 0f) return;

            // Kenney Mini Characters — «чіબі»-пропорції: голова сама ~40-50%
            // від зросту моделі. Перша спроба (0.42 зросту, запас ×1.25)
            // виявилась ще ЗАНАДТО тісною на реальному знімку — кадр впирався
            // просто в очі/рот, не показуючи голову цілком. 0.6 зросту з
            // запасом ×1.7 лишає видимою всю голову з невеликим повітрям
            // навколо, а не тільки її нижню частину.
            float bustHeight = bounds.size.y * 0.6f;
            float focusY = bounds.max.y - bustHeight * 0.5f;

            float halfFovRad = _camera.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float distance = (bustHeight * 0.5f) / Mathf.Tan(halfFovRad) * 1.7f;

            var focusLocal = _stage.InverseTransformPoint(new Vector3(bounds.center.x, focusY, bounds.center.z));
            _camera.transform.localPosition = new Vector3(focusLocal.x, focusLocal.y, focusLocal.z - distance);
            // Камера й фокус тепер на однаковій локальній X/Y станка — дивимось
            // прямо вздовж +Z станка, без нахилу (той нахил, що був тут
            // раніше, рахувався на фіксовану позицію камери, якої більше нема).
            _camera.transform.localRotation = Quaternion.identity;
        }

        private GameObject PickPrefab(string characterId)
        {
            bool female = string.Equals(characterId, "myroslava", System.StringComparison.Ordinal) ||
                (string.Equals(characterId, GameSession.ProtagonistId, System.StringComparison.Ordinal) &&
                 ProtagonistGender == Gender.Female);
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
            // Позиція/поворот тут — заглушка на перший кадр: FrameCameraOnModel
            // перераховує обидва щоразу з реальних меж моделі, перш ніж
            // камера взагалі рендерить (RenderPortrait кличе його до Render()).
            _camera = camGo.AddComponent<Camera>();
            _camera.orthographic = false;
            _camera.fieldOfView = 24f;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.08f, 0.07f, 0.06f, 1f);
            _camera.nearClipPlane = 0.05f;
            _camera.farClipPlane = 20f;
            _camera.enabled = false;
            _camera.cullingMask = 1 << StageLayer;

            // Фікс-ревью (блокер): ключове світло було одне й доволі тьмяне
            // (1.1) — разом зі старою обрізаною рамкою кадру портрет читався
            // як «майже чорний». Друге, м'якше світло з протилежного боку
            // (fill) прибирає повністю чорну half-face сторону моделі, не
            // подвоюючи яскравість напряму в камеру.
            var keyGo = new GameObject("PortraitKeyLight");
            keyGo.transform.SetParent(_stage, false);
            keyGo.transform.localRotation = Quaternion.Euler(35f, -25f, 0f);
            var key = keyGo.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.7f;
            key.cullingMask = 1 << StageLayer;

            var fillGo = new GameObject("PortraitFillLight");
            fillGo.transform.SetParent(_stage, false);
            fillGo.transform.localRotation = Quaternion.Euler(25f, 150f, 0f);
            var fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.6f;
            fill.cullingMask = 1 << StageLayer;
        }

        /// <summary>Шар станка (див. <see cref="StageLayer"/>) — рекурсивно, бо GameObject.layer не успадковується дітьми автоматично.</summary>
        private static void SetLayerRecursively(GameObject go)
        {
            go.layer = StageLayer;
            foreach (Transform child in go.transform) SetLayerRecursively(child.gameObject);
        }
    }
}
