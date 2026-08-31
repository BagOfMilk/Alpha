using System;

// ЗАГЛУШКА UnityEngine — ТОЛЬКО для проверки компиляции обёрток вне редактора.
//
// Зачем: Game.Gameplay зависит от движка и потому не попадает в headless-тесты.
// Но большинство ошибок в обёртках — это НЕ поведение движка, а обычные ошибки
// C#: опечатка в имени, забытый using, обращение к internal-члену ядра. Такие
// ошибки ловятся компилятором, и глупо ждать ради них запуска Unity.
//
// ЧЕГО ЭТА ЗАГЛУШКА НЕ ДЕЛАЕТ: она не проверяет поведение. Сцены, инспектор,
// сериализация, жизненный цикл MonoBehaviour — только настоящий редактор.
// Зелёный линт означает «код собирается», а не «код работает».
//
// Файл лежит вне Assets/, поэтому Unity его не видит и конфликта имён нет.
namespace UnityEngine
{
    public class Object { }

    public class ScriptableObject : Object { }

    public class MonoBehaviour : Object { }

    public static class Debug
    {
        public static void Log(object message) { }
        public static void LogWarning(object message) { }
        public static void LogError(object message) { }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class CreateAssetMenuAttribute : Attribute
    {
        public string fileName;
        public string menuName;
        public int order;
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class HeaderAttribute : Attribute
    {
        public HeaderAttribute(string header) { }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class TooltipAttribute : Attribute
    {
        public TooltipAttribute(string tooltip) { }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class MinAttribute : Attribute
    {
        public MinAttribute(float min) { }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class RangeAttribute : Attribute
    {
        public RangeAttribute(float min, float max) { }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class ContextMenuAttribute : Attribute
    {
        public ContextMenuAttribute(string name) { }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SerializeFieldAttribute : Attribute { }
}
