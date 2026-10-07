using Game.Core.Session;

namespace Game.Gameplay.UI
{
    /// <summary>
    /// Екран на UI Toolkit, що повністю замінює IMGUI-екран свого стану (Поправка №17.4: нових IMGUI-екранів
    /// не створюємо). Сама реалізація живе поза лінтом (їй потрібні Image/RenderTexture/3D-прев'ю), тому
    /// оболонка знаходить її рефлексією і говорить лише через цей інтерфейс; немає реалізації — лишається
    /// IMGUI-екран як фолбек.
    /// </summary>
    public interface IShellOverlay
    {
        /// <summary>Чи показує цей екран стан <paramref name="state"/> (тоді IMGUI-екран стану не малюється).</summary>
        bool Handles(SessionState state);

        /// <summary>Раз на кадр після Update (оболонка кличе з LateUpdate).</summary>
        void Tick();

        void Dispose();
    }
}
