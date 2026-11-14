using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Game.Gameplay.Ring
{
    /// <summary>
    /// Зонд частоти кадрів для звіту М1 (P0-3, зона «Відео огляд»): <c>-ring-fps &lt;файл&gt;</c> у будь-якому запуску
    /// (зазвичай разом з <c>-ring-demo</c>). Після розігріву міряє кадри й пише підсумок у файл щосекунди — гра може
    /// закритись через HardExit (TerminateProcess), тож останній запис не губиться.
    ///
    /// Два числа, бо огляди йдуть із вікном за краєм екрана, а Windows таке вікно пригальмовує (~10 к/с): «стіновий» FPS
    /// (середній і 1 % найгірших кадрів) правдивий лише з видимим вікном, а час кадру з <see cref="FrameTimingManager"/>
    /// (CPU головного потоку й GPU, мс) — правдивий і за краєм: «потенційний FPS» = 1000 / max(CPU, GPU).
    /// FrameTimingManager потребує PlayerSettings.enableFrameTimingStats — його вмикає збірка Ring.exe (RingSceneBuilder.Player).
    /// «Робота» кадру = max(CPU головного потоку мінус очікування Present, CPU потоку рендера, GPU): очікування Present —
    /// саме той простій, яким Windows гальмує вікно за краєм екрана. Медіана кадру — поруч із середнім, бо демо
    /// (-ring-demo) знімає PNG і дає поодинокі піки; «1 % найгірших» їх містить.
    /// Замір — без звуку: гучність слухача тримається на 0 весь час, поки живе зонд (тури Ring.exe завжди заглушені;
    /// другий запобіжник — Core Audio у tools/run-offscreen.ps1).
    ///
    /// Параметри: <c>-ring-fps-warmup</c> (с, 5), <c>-ring-fps-seconds</c> (с, 30), <c>-ring-fps-label</c> (мітка, напр. gym),
    /// <c>-ring-fps-exit</c> — закрити гру після заміру. На час заміру вимкнено VSync і обмеження частоти, інакше міряли б
    /// частоту монітора.
    /// </summary>
    public sealed class RingFpsProbe : MonoBehaviour
    {
        private string _path, _label;
        private float _warmup = 5f, _seconds = 30f, _t, _nextWrite;
        private bool _exit, _done;
        private readonly List<float> _frames = new List<float>(4096);
        private readonly FrameTiming[] _timing = new FrameTiming[1];
        private double _cpuSum, _waitSum, _renderSum, _gpuSum, _workSum;
        private int _cpuN, _gpuN, _workN;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            string path = RingLaunchArgs.Value("-ring-fps");
            if (string.IsNullOrEmpty(path)) return;
            var go = new GameObject("RingFpsProbe");
            DontDestroyOnLoad(go);
            var p = go.AddComponent<RingFpsProbe>();
            p._path = path;
            p._label = RingLaunchArgs.Value("-ring-fps-label") ?? "";
            p._warmup = Seconds("-ring-fps-warmup", 5f);
            p._seconds = Seconds("-ring-fps-seconds", 30f);
            p._exit = RingLaunchArgs.Has("-ring-fps-exit");
            AudioListener.volume = 0f;
            Debug.Log("[Ринг] зонд FPS: " + path + " · розігрів " + p._warmup + " с · замір " + p._seconds + " с");
        }

        private static float Seconds(string name, float def)
        {
            float v;
            string s = RingLaunchArgs.Value(name);
            return s != null && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v >= 0f ? v : def;
        }

        private void Update()
        {
            if (_done) return;
            // Налаштування гри можуть увімкнути VSync пізніше за старт — на час заміру тримаємо вимкненим.
            if (QualitySettings.vSyncCount != 0) QualitySettings.vSyncCount = 0;
            if (Application.targetFrameRate != -1) Application.targetFrameRate = -1;
            if (AudioListener.volume != 0f) AudioListener.volume = 0f;
            float dt = Time.unscaledDeltaTime;
            _t += dt;
            if (_t < _warmup) return;
            _frames.Add(dt);
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, _timing) > 0)
            {
                FrameTiming ft = _timing[0];
                double work = 0;
                if (ft.cpuMainThreadFrameTime > 0)
                {
                    _cpuSum += ft.cpuMainThreadFrameTime; _waitSum += ft.cpuMainThreadPresentWaitTime;
                    _renderSum += ft.cpuRenderThreadFrameTime; _cpuN++;
                    work = System.Math.Max(ft.cpuMainThreadFrameTime - ft.cpuMainThreadPresentWaitTime, ft.cpuRenderThreadFrameTime);
                }
                if (ft.gpuFrameTime > 0) { _gpuSum += ft.gpuFrameTime; _gpuN++; work = System.Math.Max(work, ft.gpuFrameTime); }
                if (work > 0) { _workSum += work; _workN++; }
            }
            bool finished = _t >= _warmup + _seconds;
            if (_t >= _nextWrite || finished)
            {
                _nextWrite = _t + 1f;
                Write(finished);
            }
            if (finished)
            {
                _done = true;
                Debug.Log("[Ринг] зонд FPS: готово — " + _path);
                if (_exit) HardExit.Now(0);
            }
        }

        private void Write(bool finished)
        {
            int n = _frames.Count;
            if (n == 0) return;
            var sorted = new List<float>(_frames);
            sorted.Sort();
            double sum = 0;
            foreach (float f in _frames) sum += f;
            int worst = Mathf.Max(1, n / 100);
            double worstSum = 0;
            for (int i = n - worst; i < n; i++) worstSum += sorted[i];
            double avgMs = 1000.0 * sum / n, lowMs = 1000.0 * worstSum / worst;
            double p95Ms = 1000.0 * sorted[Mathf.Min(n - 1, (int)(n * 0.95f))];
            double medMs = 1000.0 * sorted[n / 2];
            double cpu = _cpuN > 0 ? _cpuSum / _cpuN : 0, wait = _cpuN > 0 ? _waitSum / _cpuN : 0;
            double render = _cpuN > 0 ? _renderSum / _cpuN : 0, gpu = _gpuN > 0 ? _gpuSum / _gpuN : 0;
            double work = _workN > 0 ? _workSum / _workN : 0;
            double cpuWork = System.Math.Max(cpu - wait, render);
            var c = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine("label=" + _label);
            sb.AppendLine("done=" + (finished ? 1 : 0));
            sb.AppendLine("seconds=" + (_t - _warmup).ToString("0.0", c));
            sb.AppendLine("frames=" + n);
            sb.AppendLine("fps_avg=" + (1000.0 / avgMs).ToString("0.0", c));
            sb.AppendLine("fps_median=" + (1000.0 / medMs).ToString("0.0", c));
            sb.AppendLine("fps_1pct_low=" + (1000.0 / lowMs).ToString("0.0", c));
            sb.AppendLine("ms_avg=" + avgMs.ToString("0.00", c));
            sb.AppendLine("ms_median=" + medMs.ToString("0.00", c));
            sb.AppendLine("ms_p95=" + p95Ms.ToString("0.00", c));
            sb.AppendLine("cpu_main_ms=" + cpu.ToString("0.00", c) + " (" + _cpuN + " кадрів)");
            sb.AppendLine("cpu_present_wait_ms=" + wait.ToString("0.00", c));
            sb.AppendLine("cpu_render_ms=" + render.ToString("0.00", c));
            sb.AppendLine("gpu_ms=" + gpu.ToString("0.00", c) + " (" + _gpuN + " кадрів)");
            sb.AppendLine("work_ms=" + work.ToString("0.00", c) + " (max: CPU без очікування Present, рендер, GPU; по кадрах)");
            sb.AppendLine("fps_potential=" + (work > 0 ? (1000.0 / work).ToString("0.0", c) : "н/д (FrameTimingManager вимкнений)"));
            sb.AppendLine("bound=" + (work <= 0 ? "н/д" : gpu >= cpuWork ? "GPU" : "CPU"));
            sb.AppendLine("screen=" + Screen.width + "x" + Screen.height + " " + Screen.fullScreenMode);
            sb.AppendLine("quality=" + QualitySettings.names[QualitySettings.GetQualityLevel()]);
            sb.AppendLine("gpu_name=" + SystemInfo.graphicsDeviceName + " · " + SystemInfo.graphicsDeviceType);
            sb.AppendLine("cpu_name=" + SystemInfo.processorType + " · " + SystemInfo.processorCount + " потоків");
            sb.AppendLine("editor=" + (Application.isEditor ? 1 : 0));
            try
            {
                string dir = Path.GetDirectoryName(Path.GetFullPath(_path));
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(_path, sb.ToString(), new UTF8Encoding(false));
            }
            catch (System.Exception e) { Debug.LogWarning("[Ринг] зонд FPS: не вдалося записати " + _path + ": " + e.Message); }
        }
    }
}
