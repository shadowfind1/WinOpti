using System;

namespace WinOpti.Diagnostics
{
    public enum Severity { Ok, Info, Warn, Critical }

    /// <summary>Результат одной проверки: что нашли, почему это важно и как исправить.</summary>
    public sealed class Finding
    {
        public string Id;
        public Severity Severity;
        public string Title;
        public string Detail;
        /// <summary>Короткая плашка: «до +40% плавности», «нужна перезагрузка»…</summary>
        public string Impact;
        /// <summary>Исправление в один клик. Возвращает текст результата. null — автоисправления нет.</summary>
        public Func<string> Fix;
        public string FixLabel = "Исправить";
        public bool FixNeedsAdmin;
        public bool FixNeedsReboot;
        /// <summary>Пошаговая инструкция, если исправить можно только руками (BIOS, железо).</summary>
        public string Guide;

        public static Finding Ok(string id, string title, string detail) =>
            new Finding { Id = id, Severity = Severity.Ok, Title = title, Detail = detail };
    }
}
