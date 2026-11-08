using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Gameplay.Ring.Stage
{
    /// <summary>
    /// Світло-шоу й ефекти рингу (ітерація 1, TRPG_DESIGN §11.0.3; сцени — venue_gym_cues.txt з ring_show.py): «бій» (тепле
    /// робоче світло), «вихід» (зала гасне, червоні PAR, прожектор супроводу на борця, рухомі голови по такту, CO2 і холодні
    /// іскри біля завіси, густіший дим), «фінішер» (стробоскоп і віяло променів), «перемога» (конфеті над рингом, промені по
    /// трибунах). Спортзал — «клубно»: без вогняного піро; вогняні фонтани — `Pyro()` для великих моментів.
    /// Частинки — ParticleSystem з матеріалами-шаблонами збирача сцени (шейдер URP Particles є в білді). Не лінтується.
    /// </summary>
    public sealed class RingFx : MonoBehaviour
    {
        private RingStage3D _s;
        private readonly List<Light> _par = new List<Light>();
        private Light _key, _fill, _rimCool, _rimWarm;
        private readonly List<Light> _heads = new List<Light>();
        private Light _follow;
        private Transform _followTarget;
        private float[] _parBase;
        private Color[] _parColorBase;
        private float _keyBase, _fillBase, _rimCoolBase, _rimWarmBase;
        private Color _ambientBase;
        private float _fogBase;

        private string _cue = "match";
        private float _cueT;
        private float _bpm;
        private Color[] _headColors = new Color[0];
        private string _headMode = "off";

        private ParticleSystem _co2L, _co2R, _sparkL, _sparkR, _confetti, _pyroL, _pyroR;

        private static readonly Color Red = new Color(1f, 0.12f, 0.08f), Gold = new Color(1f, 0.7f, 0.2f), Blue = new Color(0.15f, 0.3f, 1f),
            White = new Color(1f, 0.97f, 0.92f), Purple = new Color(0.6f, 0.15f, 1f);

        public void Init(RingStage3D stage, Material additive, Material alpha)
        {
            _s = stage;
            var dot = SoftDot();
            // Конфеті — тверді прямокутні листки (з м'якою плямою виходили круглі розмиті цятки, огляд 04.11.2026).
            var paper = WithTex(alpha, Paper());
            additive = WithTex(additive, dot);
            alpha = WithTex(alpha, dot);
            foreach (var l in stage.GetComponentsInChildren<Light>(true))
            {
                if (l.name.StartsWith("PAR")) _par.Add(l);
                else if (l.name == "Key") _key = l;
                else if (l.name == "Fill_front") _fill = l;
                else if (l.name == "Rim_cool") _rimCool = l;
                else if (l.name == "Rim_warm") _rimWarm = l;
            }
            _parBase = new float[_par.Count];
            _parColorBase = new Color[_par.Count];
            for (int i = 0; i < _par.Count; i++) { _parBase[i] = _par[i].intensity; _parColorBase[i] = _par[i].color; }
            _keyBase = _key != null ? _key.intensity : 0f;
            _fillBase = _fill != null ? _fill.intensity : 0f;
            _rimCoolBase = _rimCool != null ? _rimCool.intensity : 0f;
            _rimWarmBase = _rimWarm != null ? _rimWarm.intensity : 0f;
            _ambientBase = RenderSettings.ambientLight;
            _fogBase = RenderSettings.fogDensity;

            // Шість рухомих голів на «воротах» над рингом (спортзал: ферма з PAR) — у бою вимкнені.
            for (int i = 0; i < 6; i++)
            {
                float a = i / 6f * Mathf.PI * 2f;
                var l = new GameObject("Head" + i).AddComponent<Light>();
                l.transform.SetParent(transform, false);
                l.type = LightType.Spot;
                l.transform.position = new Vector3(Mathf.Cos(a) * 3.2f, 6.2f, Mathf.Sin(a) * 3.2f);
                l.spotAngle = 18f;
                l.innerSpotAngle = 6f;
                l.range = 24f;
                l.intensity = 0f;
                l.shadows = LightShadows.None;
                _heads.Add(l);
            }
            _follow = new GameObject("Follow").AddComponent<Light>();
            _follow.transform.SetParent(transform, false);
            _follow.type = LightType.Spot;
            _follow.transform.position = new Vector3(6f, 8f, -6f);
            _follow.spotAngle = 9f;
            _follow.innerSpotAngle = 4f;
            _follow.range = 40f;
            _follow.intensity = 0f;
            _follow.color = White;
            _follow.shadows = LightShadows.None;

            // Ефекти біля завіси (якір anchor_curtain: −16, 0, 0; вихід дивиться на +X).
            Vector3 cl = new Vector3(-15.2f, 0.1f, -1.9f), cr = new Vector3(-15.2f, 0.1f, 1.9f);
            _co2L = Co2(cl, alpha); _co2R = Co2(cr, alpha);
            _sparkL = Sparks(cl + new Vector3(0.3f, 0f, -0.4f), additive, 5.5f, new Color(1f, 0.9f, 0.6f));
            _sparkR = Sparks(cr + new Vector3(0.3f, 0f, 0.4f), additive, 5.5f, new Color(1f, 0.9f, 0.6f));
            // Фонтани — на задніх кутах (за переможцем з боку камери); на передніх полум'я закривало ринг (огляд 04.11.2026).
            _pyroL = Sparks(new Vector3(-2.6f, 1.05f, 2.6f), additive, 9f, new Color(1f, 0.55f, 0.15f));
            _pyroR = Sparks(new Vector3(2.6f, 1.05f, 2.6f), additive, 9f, new Color(1f, 0.55f, 0.15f));
            _confetti = Confetti(paper);
        }

        // ============================ сцени ============================

        /// <summary>Бій: робоче тепле світло над рингом, голови й прожектор вимкнені.</summary>
        public void Match()
        {
            _cue = "match"; _cueT = 0f; _headMode = "off"; _bpm = 0f; _followTarget = null;
            if (_entranceCo != null) { StopCoroutine(_entranceCo); _entranceCo = null; }
            _entranceRing = false;
            Stop(_co2L); Stop(_co2R); Stop(_sparkL); Stop(_sparkR);
        }

        /// <summary>
        /// Вихід: зала гасне (лиходію — ще на 30 %), PAR червоніють, голови ходять по такту ролика екрана виходу
        /// (<paramref name="bpm"/> — його темп, ТЗ B07 п. 2) кольорами профілю (ENTRANCE_PROFILE.md біт 5), прожектор веде
        /// борця. CO2 — на ударі екрана (<paramref name="hitIn"/> с), холодні іскри — лише з Харизмою ≥ 2.
        /// </summary>
        public void Entrance(Transform wrestler, Color[] heads, float[] gain, float bpm, float hitIn, bool sparks, float hall)
        {
            if (_entranceCo != null) StopCoroutine(_entranceCo);
            _cue = "entrance"; _cueT = 0f; _headMode = "curtain"; _bpm = bpm > 0f ? bpm : 120f; _followTarget = wrestler;
            _headColors = heads != null && heads.Length > 0 ? heads : new[] { White };
            _headGain = gain;
            _hall = hall > 0f ? hall : 1f;
            _entranceRing = false;
            _beatAt = -hitIn;   // такт голів — від удару: перший колір міняється разом зі спалахом екрана
            _entranceCo = StartCoroutine(EntranceFx(hitIn, sparks, _bpm));
        }

        private Coroutine _entranceCo;
        private float[] _headGain;
        private float _hall = 1f, _beatAt;

        private IEnumerator EntranceFx(float hitIn, bool sparks, float bpm)
        {
            if (hitIn > 0f) yield return new WaitForSeconds(hitIn);
            Burst(_co2L); Burst(_co2R);
            if (sparks) { Burst(_sparkL); Burst(_sparkR); }
            // Другий подих CO2 — через чотири такти після удару (на сітці бітів).
            yield return new WaitForSeconds(4f * 60f / bpm);
            Burst(_co2L); Burst(_co2R);
        }

        /// <summary>
        /// Борець уже в рингу (поза, пояс): ринг підсвічено спереду, як для бою, зала лишається темною, голови йдуть далі тим
        /// самим тактом (огляд виходів 08.11.2026: поза на канаті в куті виходила майже чорною).
        /// </summary>
        public void EntranceRing() { if (_cue == "entrance") _entranceRing = true; }

        private bool _entranceRing;

        /// <summary>Вогняні фонтани на задніх кутах рингу — на позі головного, титульного бою чи чемпіона (біт 5).</summary>
        public void RingPyro() { Burst(_pyroL); Burst(_pyroR); }

        /// <summary>Фінішер: короткий стробоскоп і віяло білих і золотих променів на ринг.</summary>
        /// <summary>Лічильник великих моментів (фінішер, перемога) — огляд ботом знімає кадр саме тоді.</summary>
        public int Moments { get; private set; }
        public string LastMoment { get; private set; }

        public void Finisher()
        {
            Moments++; LastMoment = "finisher";
            _cue = "finisher"; _cueT = 0f; _headMode = "fan"; _bpm = 0f;
            _headColors = new[] { White, Gold, White, Purple };
            StartCoroutine(BackToMatch(2.2f, "finisher"));
        }

        /// <summary>Перемога: конфеті над рингом, промені по трибунах, яскраві PAR; вогняні фонтани з кутів.</summary>
        public void Victory(Transform winner)
        {
            Moments++; LastMoment = "victory";
            _cue = "victory"; _cueT = 0f; _headMode = "crowd"; _bpm = 128f; _followTarget = winner;
            _headColors = new[] { Gold, Blue, Gold, Purple };
            Burst(_confetti);
            Burst(_pyroL); Burst(_pyroR);
        }

        /// <summary>
        /// Поразка гравця: жодного свята (огляд 06.11.2026 — конфеті на «Поразці»). Зала тьмяніє в холодне, PAR сині й
        /// слабкі, холодний прожектор супроводу на тому, хто програв.
        /// </summary>
        public void Defeat(Transform loser)
        {
            Moments++; LastMoment = "defeat";
            _cue = "defeat"; _cueT = 0f; _headMode = "off"; _bpm = 0f; _followTarget = loser;
        }

        /// <summary>Світло перемоги без нових частинок (екран результату після матчу).</summary>
        public void VictoryLights(Transform winner)
        {
            _cue = "victory"; _cueT = 0f; _headMode = "crowd"; _bpm = 128f; _followTarget = winner;
            _headColors = new[] { Gold, Blue, Gold, Purple };
        }

        private float _flash;

        /// <summary>
        /// Спалах удару: PAR на мить яскравішають (чистий удар — сильніше, «частково» — ледь). Налаштування → Доступність →
        /// «Спалахи» вимкнено — світло не спалахує.
        /// </summary>
        public void Impact(float strength) { if (Game.Gameplay.Ring.UI.Menus.RingAccessibility.Flashes) _flash = Mathf.Max(_flash, strength); }

        /// <summary>Великий момент вступу (назва гри на титантроні): іскри й CO2 біля завіси.</summary>
        public void TitleHit()
        {
            Burst(_sparkL); Burst(_sparkR); Burst(_co2L); Burst(_co2R);
        }

        private IEnumerator BackToMatch(float after, string cue)
        {
            yield return new WaitForSeconds(after);
            if (_cue == cue) Match();
        }

        // ============================ кадр ============================

        private void LateUpdate()
        {
            if (_s == null) return;
            float dt = Time.deltaTime;
            _cueT += dt;
            float k = 1f - Mathf.Exp(-4f * dt);
            float house, parMul, haze, keyMul;
            Color parTint;
            switch (_cue)
            {
                case "entrance":
                    if (_entranceRing) { house = 0.5f * _hall; parMul = 1.0f; haze = 1.4f; keyMul = 1.0f; parTint = Color.clear; }
                    else { house = 0.35f * _hall; parMul = 0.55f * _hall; haze = 1.8f; keyMul = 0.5f; parTint = new Color(1f, 0.45f, 0.4f); }
                    break;
                // стробоскоп фінішера; «Спалахи» вимкнено — рівне яскраве світло без блимання (фотосенситивність)
                case "finisher": house = 0.3f; parMul = !Game.Gameplay.Ring.UI.Menus.RingAccessibility.Flashes ? 1.2f : Strobe() ? 1.8f : 0.15f; haze = 1.4f; keyMul = 0.6f; parTint = White; break;
                case "victory": house = 1.1f; parMul = 1.3f; haze = 1.3f; keyMul = 1.1f; parTint = Gold; break;
                case "defeat": house = 0.45f; parMul = 0.35f; haze = 1.5f; keyMul = 0.5f; parTint = new Color(0.45f, 0.58f, 1f); break;
                default: house = 1f; parMul = 1f; haze = 1f; keyMul = 1f; parTint = Color.clear; break;
            }
            _flash = Mathf.MoveTowards(_flash, 0f, dt * 5f);
            parMul *= 1f + _flash * 1.6f;
            for (int i = 0; i < _par.Count; i++)
            {
                _par[i].intensity = Mathf.Lerp(_par[i].intensity, _parBase[i] * parMul, (_cue == "finisher" && UI.Menus.RingAccessibility.Flashes) || _flash > 0f ? 1f : k);
                var target = parTint.a > 0f ? Color.Lerp(_parColorBase[i], parTint, 0.75f) : _parColorBase[i];
                _par[i].color = Color.Lerp(_par[i].color, target, k);
            }
            if (_key != null) _key.intensity = Mathf.Lerp(_key.intensity, _keyBase * keyMul, k);
            if (_fill != null) _fill.intensity = Mathf.Lerp(_fill.intensity, _fillBase * house, k);
            if (_rimCool != null) _rimCool.intensity = Mathf.Lerp(_rimCool.intensity, _rimCoolBase * house, k);
            if (_rimWarm != null) _rimWarm.intensity = Mathf.Lerp(_rimWarm.intensity, _rimWarmBase * house, k);
            RenderSettings.ambientLight = Color.Lerp(RenderSettings.ambientLight, _ambientBase * house, k);
            RenderSettings.fogDensity = Mathf.Lerp(RenderSettings.fogDensity, _fogBase * haze, k);

            // Рухомі голови: колір по черзі за тактом, напрям — за режимом.
            int beat = _bpm > 0f ? Mathf.FloorToInt((_cueT + (_cue == "entrance" ? _beatAt : 0f)) * _bpm / 60f) : 0;
            if (beat < 0) beat = 0;
            for (int i = 0; i < _heads.Count; i++)
            {
                var h = _heads[i];
                if (_headMode == "off" || _headColors.Length == 0) { h.intensity = Mathf.Lerp(h.intensity, 0f, k); continue; }
                int ci = (i + beat) % _headColors.Length;
                h.color = _headColors[ci];
                float g = _cue == "entrance" && _headGain != null && ci < _headGain.Length ? _headGain[ci] : 1f;
                h.intensity = Mathf.Lerp(h.intensity, (_headMode == "curtain" ? 16f : 40f) * g, k);
                Vector3 tgt = HeadTarget(i, _cueT);
                h.transform.rotation = Quaternion.Slerp(h.transform.rotation, Quaternion.LookRotation(tgt - h.transform.position), 1f - Mathf.Exp(-6f * dt));
            }
            // Прожектор супроводу: на таз борця, трохи вище.
            if (_followTarget != null && (_cue == "entrance" || _cue == "victory" || _cue == "defeat"))
            {
                _follow.intensity = Mathf.Lerp(_follow.intensity, _cue == "defeat" ? 70f : 120f, k);
                _follow.color = Color.Lerp(_follow.color, _cue == "defeat" ? new Color(0.6f, 0.72f, 1f) : Color.white, k);
                var p = _followTarget.position + Vector3.up * 0.3f;
                _follow.transform.rotation = Quaternion.LookRotation(p - _follow.transform.position);
            }
            else _follow.intensity = Mathf.Lerp(_follow.intensity, 0f, k);
        }

        private bool Strobe() { return Mathf.FloorToInt(_cueT * 12f) % 2 == 0 && _cueT < 1.2f; }

        private Vector3 HeadTarget(int i, float t)
        {
            float a = i / 6f * Mathf.PI * 2f;
            switch (_headMode)
            {
                case "curtain":
                    // Віяло на прохід і завісу, з легким гойданням.
                    return new Vector3(-15.5f + 9f * (i / 5f), 0.5f, Mathf.Sin(t * 2f + i) * 2.2f);
                case "fan":
                    return new Vector3(Mathf.Cos(a) * 1.2f, 1f, Mathf.Sin(a) * 1.2f);
                case "crowd":
                    float sweep = t * 0.9f + i;
                    return new Vector3(Mathf.Cos(sweep) * 8f, 1.5f, Mathf.Sin(sweep) * 8f);
            }
            return Vector3.zero;
        }

        // ============================ частинки ============================

        private ParticleSystem MakePs(string name, Vector3 pos, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.position = pos;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        /// <summary>CO2: білий струмінь угору (~1,2 с), розходиться й тане.</summary>
        private ParticleSystem Co2(Vector3 pos, Material mat)
        {
            var ps = MakePs("CO2", pos, mat);
            var main = ps.main;
            main.duration = 1.2f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(7f, 10f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
            main.startColor = new Color(0.95f, 0.97f, 1f, 0.55f);
            main.gravityModifier = -0.05f;
            main.maxParticles = 400;
            var em = ps.emission;
            em.rateOverTime = 260f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = 7f;
            sh.radius = 0.08f;
            sh.rotation = new Vector3(-90f, 0f, 0f);
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.4f, 1f, 2.2f));
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = Fade(new Color(0.95f, 0.97f, 1f), 0.6f);
            var damp = ps.limitVelocityOverLifetime;
            damp.enabled = true;
            damp.drag = 2.2f;
            return ps;
        }

        /// <summary>Холодні іскри / фонтан: дрібні яскраві точки параболами (адитивно).</summary>
        private ParticleSystem Sparks(Vector3 pos, Material mat, float speed, Color c)
        {
            var ps = MakePs("Sparks", pos, mat);
            var main = ps.main;
            main.duration = 1.6f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.7f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
            main.startColor = c;
            main.gravityModifier = 1f;
            main.maxParticles = 900;
            var em = ps.emission;
            em.rateOverTime = 520f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = 11f;
            sh.radius = 0.05f;
            sh.rotation = new Vector3(-90f, 0f, 0f);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = Fade(c, 1f);
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.04f;
            r.lengthScale = 1f;
            var glow = new GameObject("Відсвіт").AddComponent<Light>();
            glow.transform.SetParent(ps.transform, false);
            glow.transform.localPosition = Vector3.up * 0.8f;
            glow.type = LightType.Point;
            glow.color = c;
            glow.range = 5f;
            glow.intensity = 0f;
            glow.shadows = LightShadows.None;
            var lights = ps.lights;
            lights.enabled = false;
            return ps;
        }

        /// <summary>Конфеті: різнокольорові листки з-під ферми над рингом, повільно кружляють донизу.</summary>
        private ParticleSystem Confetti(Material mat)
        {
            var ps = MakePs("Конфеті", new Vector3(0f, 6.4f, 0f), mat);
            var main = ps.main;
            main.duration = 2.5f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.11f);
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, 6.28f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, 6.28f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, 6.28f);
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Gold, 0f), new GradientColorKey(Red, 0.33f), new GradientColorKey(Blue, 0.66f), new GradientColorKey(White, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            main.startColor = new ParticleSystem.MinMaxGradient(g) { mode = ParticleSystemGradientMode.RandomColor };
            main.gravityModifier = 0.06f;
            main.maxParticles = 3500;
            var em = ps.emission;
            em.rateOverTime = 1300f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Circle;
            sh.radius = 3.5f;
            sh.rotation = new Vector3(90f, 0f, 0f);
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.6f;
            noise.frequency = 0.5f;
            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.separateAxes = true;
            rot.x = new ParticleSystem.MinMaxCurve(-4f, 4f);
            rot.y = new ParticleSystem.MinMaxCurve(-4f, 4f);
            var damp = ps.limitVelocityOverLifetime;
            damp.enabled = true;
            damp.drag = 0.8f;
            return ps;
        }

        /// <summary>М'яка кругла пляма (радіальний спад) — щоб частинки не були квадратами.</summary>
        private static Texture2D SoftDot()
        {
            const int n = 64;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "soft_dot", wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    a = a * a * (3f - 2f * a);
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            t.Apply(true);
            return t;
        }

        /// <summary>Листок конфеті: суцільний білий прямокутник 2:1 з прозорими полями зверху й знизу.</summary>
        private static Texture2D Paper()
        {
            const int n = 16;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "confetti_paper", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, y >= 4 && y < 12 ? 1f : 0f));
            t.Apply(false);
            return t;
        }

        private static Material WithTex(Material m, Texture2D tex)
        {
            if (m == null) return null;
            var c = new Material(m);
            if (c.HasProperty("_BaseMap")) c.SetTexture("_BaseMap", tex);
            if (c.HasProperty("_MainTex")) c.SetTexture("_MainTex", tex);
            return c;
        }

        private static Gradient Fade(Color c, float a0)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                new[] { new GradientAlphaKey(a0, 0f), new GradientAlphaKey(a0 * 0.6f, 0.5f), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        private void Burst(ParticleSystem ps)
        {
            if (ps == null) return;
            ps.Clear(true);
            ps.Play(true);
            var glow = ps.GetComponentInChildren<Light>();
            if (glow != null) StartCoroutine(Glow(glow, ps.main.duration));
        }

        private static void Stop(ParticleSystem ps) { if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting); }

        private static IEnumerator Glow(Light l, float dur)
        {
            for (float t = 0f; t < dur + 0.5f; t += Time.deltaTime)
            {
                // мерехтіння вогню; «Спалахи» вимкнено — рівне сяйво без миготіння
                float burn = Game.Gameplay.Ring.UI.Menus.RingAccessibility.Flashes ? 6f + Mathf.PerlinNoise(t * 20f, 0f) * 6f : 9f;
                l.intensity = t < dur ? burn : Mathf.Lerp(Game.Gameplay.Ring.UI.Menus.RingAccessibility.Flashes ? 12f : 9f, 0f, (t - dur) / 0.5f);
                yield return null;
            }
            l.intensity = 0f;
        }
    }
}
