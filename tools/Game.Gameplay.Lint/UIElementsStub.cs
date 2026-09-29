// Заглушка UnityEngine.UIElements для лінту (спайк H4, docs/HUD_DESIGN.md §8,
// критерій 3): компілює код-зв'язку шапки і стрічки на UI Toolkit
// (Gameplay/UI/Toolkit/*.cs) без Unity. Лише ті типи і члени, які реально
// викликає код. Сигнатури написані нами за документацією Unity 6 — лінт
// НЕ підтверджує, що вони збігаються зі справжнім API (межа лінту, CLAUDE.md):
// він ловить одруки, забуті using і помилки типів у нашому коді.
using System;

namespace UnityEngine.UIElements
{
    public enum PickingMode { Position = 0, Ignore = 1 }
    public enum DisplayStyle { Flex = 0, None = 1 }
    public enum Position { Relative = 0, Absolute = 1 }
    public enum FlexDirection { Column = 0, ColumnReverse = 1, Row = 2, RowReverse = 3 }
    public enum Align { Auto = 0, FlexStart = 1, Center = 2, FlexEnd = 3, Stretch = 4 }
    public enum WhiteSpace { Normal = 0, NoWrap = 1 }
    public enum ScrollViewMode { Vertical = 0, Horizontal = 1, VerticalAndHorizontal = 2 }
    public enum PanelScaleMode { ConstantPixelSize = 0, ConstantPhysicalSize = 1, ScaleWithScreenSize = 2 }
    public enum LengthUnit { Pixel = 0, Percent = 1 }
    public enum TrickleDown { NoTrickleDown = 0, TrickleDown = 1 }

    public struct Length
    {
        public float value;
        public LengthUnit unit;

        public Length(float value) { this.value = value; unit = LengthUnit.Pixel; }
        public Length(float value, LengthUnit unit) { this.value = value; this.unit = unit; }

        public static Length Percent(float value) { return new Length(value, LengthUnit.Percent); }
    }

    public struct StyleLength
    {
        public Length value;

        public StyleLength(float v) { value = new Length(v); }
        public StyleLength(Length v) { value = v; }

        public static implicit operator StyleLength(float v) { return new StyleLength(v); }
        public static implicit operator StyleLength(Length v) { return new StyleLength(v); }
    }

    public struct StyleFloat
    {
        public float value;

        public StyleFloat(float v) { value = v; }

        public static implicit operator StyleFloat(float v) { return new StyleFloat(v); }
    }

    public struct StyleColor
    {
        public Color value;

        public StyleColor(Color v) { value = v; }

        public static implicit operator StyleColor(Color v) { return new StyleColor(v); }
    }

    public struct StyleEnum<T> where T : struct, IConvertible
    {
        public T value;

        public StyleEnum(T v) { value = v; }

        public static implicit operator StyleEnum<T>(T v) { return new StyleEnum<T>(v); }
    }

    public struct FontDefinition
    {
        public Font font;

        public static FontDefinition FromFont(Font f) { return new FontDefinition { font = f }; }
    }

    public struct StyleFontDefinition
    {
        public FontDefinition value;

        public StyleFontDefinition(FontDefinition f) { value = f; }
    }

    /// <summary>Вбудований стиль елемента (у Unity — інтерфейс <c>IStyle</c>); лише ті властивості, які задає наш код.</summary>
    public interface IStyle
    {
        StyleEnum<Position> position { get; set; }
        StyleLength left { get; set; }
        StyleLength top { get; set; }
        StyleLength right { get; set; }
        StyleLength bottom { get; set; }
        StyleLength width { get; set; }
        StyleLength height { get; set; }
        StyleLength minWidth { get; set; }
        StyleEnum<FlexDirection> flexDirection { get; set; }
        StyleEnum<Align> alignItems { get; set; }
        StyleFloat flexGrow { get; set; }
        StyleFloat flexShrink { get; set; }
        StyleEnum<DisplayStyle> display { get; set; }
        StyleColor backgroundColor { get; set; }
        StyleColor color { get; set; }
        StyleLength fontSize { get; set; }
        StyleFontDefinition unityFontDefinition { get; set; }
        StyleEnum<FontStyle> unityFontStyleAndWeight { get; set; }
        StyleEnum<WhiteSpace> whiteSpace { get; set; }
        StyleLength paddingTop { get; set; }
        StyleLength paddingBottom { get; set; }
        StyleLength paddingLeft { get; set; }
        StyleLength paddingRight { get; set; }
        StyleLength marginTop { get; set; }
        StyleLength marginBottom { get; set; }
        StyleLength marginLeft { get; set; }
        StyleLength marginRight { get; set; }
        StyleFloat borderTopWidth { get; set; }
        StyleFloat borderBottomWidth { get; set; }
        StyleFloat borderLeftWidth { get; set; }
        StyleFloat borderRightWidth { get; set; }
        StyleColor borderTopColor { get; set; }
        StyleColor borderBottomColor { get; set; }
        StyleColor borderLeftColor { get; set; }
        StyleColor borderRightColor { get; set; }
    }

    internal sealed class InlineStyleStub : IStyle
    {
        public StyleEnum<Position> position { get; set; }
        public StyleLength left { get; set; }
        public StyleLength top { get; set; }
        public StyleLength right { get; set; }
        public StyleLength bottom { get; set; }
        public StyleLength width { get; set; }
        public StyleLength height { get; set; }
        public StyleLength minWidth { get; set; }
        public StyleEnum<FlexDirection> flexDirection { get; set; }
        public StyleEnum<Align> alignItems { get; set; }
        public StyleFloat flexGrow { get; set; }
        public StyleFloat flexShrink { get; set; }
        public StyleEnum<DisplayStyle> display { get; set; }
        public StyleColor backgroundColor { get; set; }
        public StyleColor color { get; set; }
        public StyleLength fontSize { get; set; }
        public StyleFontDefinition unityFontDefinition { get; set; }
        public StyleEnum<FontStyle> unityFontStyleAndWeight { get; set; }
        public StyleEnum<WhiteSpace> whiteSpace { get; set; }
        public StyleLength paddingTop { get; set; }
        public StyleLength paddingBottom { get; set; }
        public StyleLength paddingLeft { get; set; }
        public StyleLength paddingRight { get; set; }
        public StyleLength marginTop { get; set; }
        public StyleLength marginBottom { get; set; }
        public StyleLength marginLeft { get; set; }
        public StyleLength marginRight { get; set; }
        public StyleFloat borderTopWidth { get; set; }
        public StyleFloat borderBottomWidth { get; set; }
        public StyleFloat borderLeftWidth { get; set; }
        public StyleFloat borderRightWidth { get; set; }
        public StyleColor borderTopColor { get; set; }
        public StyleColor borderBottomColor { get; set; }
        public StyleColor borderLeftColor { get; set; }
        public StyleColor borderRightColor { get; set; }
    }

    // ---------------- події ----------------

    public delegate void EventCallback<in TEventType>(TEventType evt);

    public abstract class EventBase { }

    public abstract class EventBase<T> : EventBase where T : EventBase<T>, new() { }

    public sealed class PointerEnterEvent : EventBase<PointerEnterEvent> { }

    public sealed class PointerLeaveEvent : EventBase<PointerLeaveEvent> { }

    public abstract class CallbackEventHandler
    {
        public void RegisterCallback<TEventType>(EventCallback<TEventType> callback, TrickleDown useTrickleDown = TrickleDown.NoTrickleDown)
            where TEventType : EventBase<TEventType>, new() { }
    }

    public abstract class Focusable : CallbackEventHandler
    {
        public bool focusable { get; set; }
    }

    // ---------------- елементи ----------------

    public class VisualElement : Focusable
    {
        private readonly IStyle _style = new InlineStyleStub();

        public string name { get; set; }
        public PickingMode pickingMode { get; set; }
        public IStyle style { get { return _style; } }

        public void Add(VisualElement child) { }
        public void Clear() { }
        public void SetEnabled(bool value) { }
    }

    public class TextElement : VisualElement
    {
        public virtual string text { get; set; }
    }

    public class Label : TextElement
    {
        public Label() { }
        public Label(string text) { this.text = text; }
    }

    public class Button : TextElement
    {
        public event Action clicked;

        public Button() { }
        public Button(Action clickEvent) { clicked += clickEvent; }
    }

    public class ScrollView : VisualElement
    {
        public ScrollView() { }
        public ScrollView(ScrollViewMode scrollViewMode) { }

        public Vector2 scrollOffset { get; set; }
    }

    // ---------------- панель і документ ----------------

    public class StyleSheet : ScriptableObject { }

    public class ThemeStyleSheet : StyleSheet { }

    public class PanelSettings : ScriptableObject
    {
        public PanelScaleMode scaleMode { get; set; }
        public float scale { get; set; }
        public float sortingOrder { get; set; }
        public ThemeStyleSheet themeStyleSheet { get; set; }
    }

    public sealed class UIDocument : MonoBehaviour
    {
        public PanelSettings panelSettings { get; set; }
        public VisualElement rootVisualElement { get { return null; } }
    }
}
