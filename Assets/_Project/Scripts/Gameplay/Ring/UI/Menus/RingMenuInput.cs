using System;
using System.Collections.Generic;
using Game.Gameplay.Ring.Models;
using UnityEngine;

namespace Game.Gameplay.Ring.UI.Menus
{
    /// <summary>Звідки прийшла дія (підказки підвалу показують клавіші пристрою, яким гравець користується зараз).</summary>
    public enum MenuDevice { Keyboard = 0, Gamepad, Mouse }

    /// <summary>Екран меню, що приймає навігацію (клавіатура, геймпад, колесо миші — одним шляхом).</summary>
    public interface IMenuNavigable
    {
        void OnMenu(RingAction action, MenuDevice device);
    }

    /// <summary>
    /// Ввід меню (ТЗ B08 §7): клавіші з налаштувань (перепризначувані) + сталі запасні (WASD, Пробіл, Num Enter), геймпад
    /// через менеджер вводу Unity (A — підтвердити, B — назад, LB/RB — вкладки, хрестовина й лівий стік — вибір з автоповтором),
    /// колесо миші — вгору/вниз. Новий пакет Input System не ставимо: проєкт на старому менеджері вводу, а Alpha на ньому ж.
    /// Осі геймпада — <c>RingPadX/RingPadY</c> (хрестовина, 6/7 осі) і <c>RingStickX/RingStickY</c> у ProjectSettings/InputManager.asset;
    /// без них (старий проєкт) геймпад працює кнопками, а осі тихо вимикаються.
    /// </summary>
    public sealed class RingMenuInput
    {
        private const int FirstMouseKey = 323;   // KeyCode.Mouse0: миша й кнопки геймпада не перепризначаються
        private const float RepeatDelay = 0.38f, RepeatEvery = 0.09f, AxisOn = 0.55f, AxisOff = 0.3f;
        private static readonly string[] Axes = { "RingPadX", "RingPadY", "RingStickX", "RingStickY" };

        private readonly Dictionary<RingAction, KeyCode> _keys = new Dictionary<RingAction, KeyCode>();
        private bool _axesOk = true;
        private int _heldDir;          // 0 нема, 1 вгору, 2 вниз, 3 ліворуч, 4 праворуч
        private float _heldFor, _repeatClock;

        public MenuDevice LastDevice { get; private set; }

        /// <summary>Режим «натисни клавішу» (перепризначення): навігація не йде, наступна клавіша — у <see cref="Captured"/>.</summary>
        public bool Capturing;
        public KeyCode? Captured;
        private bool _waitRelease;

        public RingMenuInput() { SetBindings(RingSettingsValues.DefaultKeys()); }

        public void SetBindings(IDictionary<RingAction, string> names)
        {
            _keys.Clear();
            foreach (var kv in names)
            {
                KeyCode k;
                if (TryParseKey(kv.Value, out k)) _keys[kv.Key] = k;
            }
        }

        public static bool TryParseKey(string name, out KeyCode key)
        {
            key = default(KeyCode);
            if (string.IsNullOrEmpty(name)) return false;
            try
            {
                key = (KeyCode)Enum.Parse(typeof(KeyCode), name, false);
                return key != default(KeyCode);
            }
            catch (ArgumentException) { return false; }
        }

        public KeyCode KeyOf(RingAction a)
        {
            KeyCode k;
            return _keys.TryGetValue(a, out k) ? k : default(KeyCode);
        }

        /// <summary>Опитати ввід за кадр і віддати дії екрану. Повертає true, якщо щось прийшло.</summary>
        public bool Poll(IMenuNavigable sink, float dt)
        {
            if (Capturing)
            {
                CaptureStep();
                if (!Capturing) _waitRelease = true;   // клавіша ще затиснута — не читати її як навігацію
                return false;
            }
            if (_waitRelease) { if (Input.anyKey) return false; _waitRelease = false; _heldDir = 0; }
            bool any = false;
            // ---- клавіатура (перепризначувані + сталі запасні) ----
            if (Down(RingAction.Confirm) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.KeypadEnter)) { Send(sink, RingAction.Confirm, MenuDevice.Keyboard); any = true; }
            if (Down(RingAction.Back) || Input.GetKeyDown(KeyCode.Backspace)) { Send(sink, RingAction.Back, MenuDevice.Keyboard); any = true; }
            if (Down(RingAction.TabPrev)) { Send(sink, RingAction.TabPrev, MenuDevice.Keyboard); any = true; }
            if (Down(RingAction.TabNext) || Input.GetKeyDown(KeyCode.Tab)) { Send(sink, RingAction.TabNext, MenuDevice.Keyboard); any = true; }
            // ---- геймпад: кнопки (A, B, LB, RB, Start) ----
            if (Input.GetKeyDown(KeyCode.JoystickButton0) || Input.GetKeyDown(KeyCode.JoystickButton7)) { Send(sink, RingAction.Confirm, MenuDevice.Gamepad); any = true; }
            if (Input.GetKeyDown(KeyCode.JoystickButton1)) { Send(sink, RingAction.Back, MenuDevice.Gamepad); any = true; }
            if (Input.GetKeyDown(KeyCode.JoystickButton4)) { Send(sink, RingAction.TabPrev, MenuDevice.Gamepad); any = true; }
            if (Input.GetKeyDown(KeyCode.JoystickButton5)) { Send(sink, RingAction.TabNext, MenuDevice.Gamepad); any = true; }
            // ---- колесо миші ----
            float wheel = Input.mouseScrollDelta.y;
            if (wheel > 0.5f) { Send(sink, RingAction.Up, MenuDevice.Mouse); any = true; }
            else if (wheel < -0.5f) { Send(sink, RingAction.Down, MenuDevice.Mouse); any = true; }
            // ---- напрямки з автоповтором ----
            MenuDevice dev;
            int dir = HeldDirection(out dev);
            if (dir == 0) { _heldDir = 0; _heldFor = 0f; return any; }
            if (dir != _heldDir)
            {
                _heldDir = dir; _heldFor = 0f; _repeatClock = 0f;
                Send(sink, DirAction(dir), dev);
                return true;
            }
            _heldFor += dt;
            if (_heldFor < RepeatDelay) return any;
            _repeatClock += dt;
            if (_repeatClock < RepeatEvery) return any;
            _repeatClock = 0f;
            Send(sink, DirAction(dir), dev);
            return true;
        }

        private void Send(IMenuNavigable sink, RingAction a, MenuDevice d)
        {
            LastDevice = d;
            if (sink != null) sink.OnMenu(a, d);
        }

        private static RingAction DirAction(int dir)
        {
            switch (dir)
            {
                case 1: return RingAction.Up;
                case 2: return RingAction.Down;
                case 3: return RingAction.Left;
                default: return RingAction.Right;
            }
        }

        private bool Down(RingAction a)
        {
            KeyCode k;
            return _keys.TryGetValue(a, out k) && Input.GetKeyDown(k);
        }

        private bool Held(RingAction a)
        {
            KeyCode k;
            return _keys.TryGetValue(a, out k) && Input.GetKey(k);
        }

        private int HeldDirection(out MenuDevice dev)
        {
            dev = MenuDevice.Keyboard;
            if (Held(RingAction.Up) || Input.GetKey(KeyCode.W)) return 1;
            if (Held(RingAction.Down) || Input.GetKey(KeyCode.S)) return 2;
            if (Held(RingAction.Left) || Input.GetKey(KeyCode.A)) return 3;
            if (Held(RingAction.Right) || Input.GetKey(KeyCode.D)) return 4;
            if (!_axesOk) return 0;
            dev = MenuDevice.Gamepad;
            float x = Axis(0) + Axis(2), y = Axis(1) - Axis(3);    // стік Y у менеджері вводу інвертований
            if (!_axesOk) return 0;
            float th = _heldDir == 0 ? AxisOn : AxisOff;           // гістерезис: стік не «тремтить» на межі
            if (Math.Abs(y) >= Math.Abs(x))
            {
                if (y > th) return 1;
                if (y < -th) return 2;
            }
            else
            {
                if (x < -th) return 3;
                if (x > th) return 4;
            }
            return 0;
        }

        private float Axis(int i)
        {
            if (!_axesOk) return 0f;
            try { return Input.GetAxisRaw(Axes[i]); }
            catch (ArgumentException)
            {
                _axesOk = false;
                Debug.LogWarning("[Ринг] меню: осі геймпада " + Axes[i] + " немає в InputManager — геймпад лише кнопками.");
                return 0f;
            }
        }

        /// <summary>Сталі запасні клавіші меню (WASD, Пробіл, Num Enter, Backspace, Tab) — не перепризначаються: інакше одна
        /// клавіша давала б дві дії.</summary>
        public static bool IsReserved(KeyCode k)
        {
            return k == KeyCode.W || k == KeyCode.A || k == KeyCode.S || k == KeyCode.D || k == KeyCode.Space
                || k == KeyCode.KeypadEnter || k == KeyCode.Backspace || k == KeyCode.Tab;
        }

        /// <summary>Клавіша скасування перепризначення (Esc; B геймпада приходить як Esc).</summary>
        public static bool IsCancel(KeyCode k) { return k == KeyCode.Escape; }

        /// <summary>Перепризначення: перша натиснута клавіша клавіатури (миша й кнопки геймпада — ні; Esc — скасувати).</summary>
        private void CaptureStep()
        {
            if (!Input.anyKeyDown) return;
            // геймпад не призначає клавіш клавіатури: B — скасувати (як Esc)
            if (Input.GetKeyDown(KeyCode.JoystickButton1)) { Captured = KeyCode.Escape; Capturing = false; return; }
            foreach (KeyCode k in Enum.GetValues(typeof(KeyCode)))
            {
                if ((int)k == 0 || (int)k >= FirstMouseKey) continue;
                if (!Input.GetKeyDown(k)) continue;
                Captured = k;
                Capturing = false;
                return;
            }
        }
    }
}
