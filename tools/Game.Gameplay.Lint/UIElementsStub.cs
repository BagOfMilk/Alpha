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
    public enum Justify { FlexStart = 0, Center = 1, FlexEnd = 2, SpaceBetween = 3, SpaceAround = 4, SpaceEvenly = 5 }
    public enum Overflow { Visible = 0, Hidden = 1 }
    public enum Wrap { NoWrap = 0, Wrap = 1, WrapReverse = 2 }
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

    // ---------------- перетворення (меню «Рингу», ТЗ B08) ----------------

    public enum AngleUnit { Degree = 0, Gradian = 1, Radian = 2, Turn = 3 }

    public struct Angle
    {
        public Angle(float value) { }
        public Angle(float value, AngleUnit unit) { }
        public static Angle Degrees(float value) { return new Angle(value); }
    }

    public struct Rotate
    {
        public Rotate(Angle angle) { }
    }

    public struct StyleRotate
    {
        public StyleRotate(Rotate v) { }
        public static implicit operator StyleRotate(Rotate v) { return new StyleRotate(v); }
    }

    public struct Translate
    {
        public Translate(Length x, Length y) { }
        public Translate(Length x, Length y, float z) { }
    }

    public struct StyleTranslate
    {
        public StyleTranslate(Translate v) { }
        public static implicit operator StyleTranslate(Translate v) { return new StyleTranslate(v); }
    }

    public struct Scale
    {
        public Scale(UnityEngine.Vector2 scale) { }
        public Scale(UnityEngine.Vector3 scale) { }
    }

    public struct StyleScale
    {
        public StyleScale(Scale v) { }
        public static implicit operator StyleScale(Scale v) { return new StyleScale(v); }
    }

    public struct TransformOrigin
    {
        public TransformOrigin(Length x, Length y) { }
        public TransformOrigin(Length x, Length y, float z) { }
    }

    public struct StyleTransformOrigin
    {
        public StyleTransformOrigin(TransformOrigin v) { }
        public static implicit operator StyleTransformOrigin(TransformOrigin v) { return new StyleTransformOrigin(v); }
    }

    // ---------------- малювання кодом (Painter2D) ----------------

    public enum ArcDirection { Clockwise = 0, CounterClockwise = 1 }

    public enum FillRule { NonZero = 0, OddEven = 1 }

    public enum LineJoin { Miter = 0, Bevel = 1, Round = 2 }

    public class Painter2D
    {
        public float lineWidth { get; set; }
        public UnityEngine.Color strokeColor { get; set; }
        public UnityEngine.Color fillColor { get; set; }
        public LineJoin lineJoin { get; set; }
        public void BeginPath() { }
        public void ClosePath() { }
        public void MoveTo(UnityEngine.Vector2 pos) { }
        public void LineTo(UnityEngine.Vector2 pos) { }
        public void Arc(UnityEngine.Vector2 center, float radius, Angle startAngle, Angle endAngle, ArcDirection direction = ArcDirection.Clockwise) { }
        public void Stroke() { }
        public void Fill(FillRule fillRule = FillRule.NonZero) { }
    }

    public class MeshGenerationContext
    {
        public Painter2D painter2D { get { return null; } }
        public VisualElement visualElement { get { return null; } }
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
        StyleFloat opacity { get; set; }
        StyleLength flexBasis { get; set; }
        StyleEnum<Overflow> overflow { get; set; }
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
        StyleBackground backgroundImage { get; set; }
        StyleColor unityBackgroundImageTintColor { get; set; }
        StyleEnum<Justify> justifyContent { get; set; }
        StyleEnum<Wrap> flexWrap { get; set; }
        StyleEnum<Align> alignSelf { get; set; }
        StyleLength maxWidth { get; set; }
        StyleLength minHeight { get; set; }
        StyleLength maxHeight { get; set; }
        StyleEnum<TextAnchor> unityTextAlign { get; set; }
        StyleLength borderTopLeftRadius { get; set; }
        StyleLength borderTopRightRadius { get; set; }
        StyleLength borderBottomLeftRadius { get; set; }
        StyleLength borderBottomRightRadius { get; set; }
        // Меню «Рингу» (ТЗ B08): рух лише transform/opacity — без перерахунку розкладки щокадру (PERF-01).
        StyleTranslate translate { get; set; }
        StyleScale scale { get; set; }
        StyleRotate rotate { get; set; }
        StyleTransformOrigin transformOrigin { get; set; }
        StyleLength letterSpacing { get; set; }
    }

    /// <summary>Фонове зображення елемента (значки шапки, Поправка №12.2).</summary>
    public struct Background
    {
        public static Background FromTexture2D(UnityEngine.Texture2D t) { return default(Background); }
    }

    public struct StyleBackground
    {
        public StyleBackground(UnityEngine.Texture2D v) { }
        public StyleBackground(Background v) { }
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
        public StyleFloat opacity { get; set; }
        public StyleLength flexBasis { get; set; }
        public StyleEnum<Overflow> overflow { get; set; }
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
        public StyleBackground backgroundImage { get; set; }
        public StyleColor unityBackgroundImageTintColor { get; set; }
        public StyleEnum<Justify> justifyContent { get; set; }
        public StyleEnum<Wrap> flexWrap { get; set; }
        public StyleEnum<Align> alignSelf { get; set; }
        public StyleLength maxWidth { get; set; }
        public StyleLength minHeight { get; set; }
        public StyleLength maxHeight { get; set; }
        public StyleEnum<TextAnchor> unityTextAlign { get; set; }
        public StyleLength borderTopLeftRadius { get; set; }
        public StyleLength borderTopRightRadius { get; set; }
        public StyleLength borderBottomLeftRadius { get; set; }
        public StyleLength borderBottomRightRadius { get; set; }
        public StyleTranslate translate { get; set; }
        public StyleScale scale { get; set; }
        public StyleRotate rotate { get; set; }
        public StyleTransformOrigin transformOrigin { get; set; }
        public StyleLength letterSpacing { get; set; }
    }

    // ---------------- події ----------------

    public delegate void EventCallback<in TEventType>(TEventType evt);

    public abstract class EventBase
    {
        public void StopPropagation() { }
    }

    public abstract class EventBase<T> : EventBase where T : EventBase<T>, new() { }

    public sealed class ClickEvent : EventBase<ClickEvent> { }

    public sealed class PointerEnterEvent : EventBase<PointerEnterEvent> { }

    public sealed class PointerLeaveEvent : EventBase<PointerLeaveEvent> { }

    public sealed class NavigationMoveEvent : EventBase<NavigationMoveEvent> { }

    public sealed class PointerDownEvent : EventBase<PointerDownEvent>
    {
        public int button { get { return 0; } }
        public UnityEngine.Vector3 localPosition { get { return default(UnityEngine.Vector3); } }
    }

    public sealed class PointerUpEvent : EventBase<PointerUpEvent> { public int button { get { return 0; } } }

    public sealed class WheelEvent : EventBase<WheelEvent> { public UnityEngine.Vector3 delta { get { return default(UnityEngine.Vector3); } } }

    public sealed class KeyDownEvent : EventBase<KeyDownEvent> { public UnityEngine.KeyCode keyCode { get { return UnityEngine.KeyCode.None; } } }

    public sealed class GeometryChangedEvent : EventBase<GeometryChangedEvent> { }

    public abstract class CallbackEventHandler
    {
        public void RegisterCallback<TEventType>(EventCallback<TEventType> callback, TrickleDown useTrickleDown = TrickleDown.NoTrickleDown)
            where TEventType : EventBase<TEventType>, new() { }
    }

    public abstract class Focusable : CallbackEventHandler
    {
        public bool focusable { get; set; }
        public FocusController focusController { get { return null; } }
        public void Blur() { }
    }

    public class FocusController
    {
        public Focusable focusedElement { get { return null; } }
    }

    // ---------------- елементи ----------------

    public class VisualElement : Focusable
    {
        private readonly IStyle _style = new InlineStyleStub();

        public string name { get; set; }
        public PickingMode pickingMode { get; set; }
        public IStyle style { get { return _style; } }
        /// <summary>Прямокутник у координатах панелі (одиниці панелі, y згори).</summary>
        public UnityEngine.Rect worldBound { get { return default(UnityEngine.Rect); } }

        public void Add(VisualElement child) { }
        public void Insert(int index, VisualElement child) { }
        public void Clear() { }
        public void SetEnabled(bool value) { }
        // Меню «Рингу»: форми малюються кодом (Painter2D), шари перемикаються без перебудови екрана.
        public Action<MeshGenerationContext> generateVisualContent { get; set; }
        public void MarkDirtyRepaint() { }
        public UnityEngine.Rect contentRect { get { return default(UnityEngine.Rect); } }
        public UnityEngine.Rect layout { get { return default(UnityEngine.Rect); } }
        public VisualElement parent { get { return null; } }
        public int childCount { get { return 0; } }
        public void Remove(VisualElement element) { }
        public void RemoveFromHierarchy() { }
        public void BringToFront() { }
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

    public class ChangeEvent<T> : EventBase
    {
        public T previousValue { get { return default(T); } }
        public T newValue { get { return default(T); } }
    }

    public class TextField : TextElement
    {
        public string value { get; set; }
        public int maxLength { get; set; }
        public void RegisterValueChangedCallback(EventCallback<ChangeEvent<string>> callback) { }
    }

    public class ScrollView : VisualElement
    {
        public ScrollView() { }
        public ScrollView(ScrollViewMode scrollViewMode) { }

        public Vector2 scrollOffset { get; set; }
        public ScrollerVisibility verticalScrollerVisibility { get; set; }
        public ScrollerVisibility horizontalScrollerVisibility { get; set; }
    }

    public enum ScrollerVisibility { Auto, AlwaysVisible, Hidden }

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
