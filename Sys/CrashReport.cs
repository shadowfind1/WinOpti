using System;
using System.IO;
using System.Text;

namespace WinOpti.Sys
{
    /// <summary>Текст отчёта об ошибке для GitHub: версия, Windows, железо и сама ошибка. Имя пользователя из путей вырезаем.</summary>
    public static class CrashReport
    {
        const string Repo = "shadowfind1/WinOpti";

        /// <summary>Сюда пишется отчёт, если программа упала целиком: при следующем запуске предложим его отправить.</summary>
        public static string PendingFile => Path.Combine(App.DataDir, "last_crash.txt");

        public static string Build(Exception e)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"WinOpti {Update.Updater.Current}");
            sb.AppendLine($"Windows {SysInfo.WindowsVersion} · build {Environment.OSVersion.Version.Build} · {(Environment.Is64BitOperatingSystem ? "x64" : "x86")}");
            sb.AppendLine($"Admin: {(SysInfo.IsAdmin ? "yes" : "no")} · UI: {(L.En ? "EN" : "RU")}");
            try { sb.AppendLine(UI.ShareCard.PcSpec().Replace("  ·  ", " · ")); } catch { } // WMI может сломаться вместе с программой
            sb.AppendLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm}");
            sb.AppendLine();
            sb.Append(e?.ToString() ?? "(no exception)");
            return Anonymize(sb.ToString());
        }

        /// <summary>C:\Users\Вася\... → C:\Users\&lt;user&gt;\...</summary>
        static string Anonymize(string s)
        {
            var name = Environment.UserName;
            return string.IsNullOrEmpty(name) ? s : s.Replace("\\" + name + "\\", "\\<user>\\").Replace("\\" + name + "\"", "\\<user>\"");
        }

        /// <summary>Ссылка на новый issue с заполненным заголовком и текстом. Длинный текст обрезаем: у браузеров предел длины адреса.</summary>
        public static string IssueUrl(string report)
        {
            var firstLine = report.Split('\n');
            string title = "Crash: ";
            foreach (var line in firstLine)
                if (line.Contains("Exception")) { title += line.Trim(); break; }
            if (title.Length > 120) title = title.Substring(0, 120) + "…";
            var body = report.Length > 5000 ? report.Substring(0, 5000) + "\n…" : report;
            body = L.T("Что делал перед ошибкой: \n\n", "What I was doing: \n\n") + "```\n" + body + "\n```";
            return $"https://github.com/{Repo}/issues/new?title={Uri.EscapeDataString(title)}&body={Uri.EscapeDataString(body)}";
        }
    }
}
