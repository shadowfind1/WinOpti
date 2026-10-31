using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;

namespace WinOpti.Tweaks
{
    /// <summary>Твик из tweaks.json — того же файла, что у WinOpti 1 (формат не меняется, файлы совместимы).</summary>
    public sealed class Tweak
    {
        public string Id, Name, NameEn, Category, Risk, After, Description, DescriptionEn, Keywords;
        public bool Hidden;
        public List<string> Windows = new List<string> { "10", "11" };
        public List<string> Refresh = new List<string>();
        public List<Dictionary<string, object>> Actions = new List<Dictionary<string, object>>();

        public bool Supported => Windows.Contains(Sys.SysInfo.WindowsVersion);

        public static string Str(Dictionary<string, object> d, string key) => d.TryGetValue(key, out var v) ? v as string : null;

        static List<string> Strings(Dictionary<string, object> d, string key) =>
            d.TryGetValue(key, out var v) && v is IEnumerable e && !(v is string) ? e.Cast<object>().Select(x => x?.ToString()).ToList() : null;

        public static List<Tweak> LoadAll()
        {
            string json;
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("WinOpti.tweaks.json"))
            using (var r = new StreamReader(s)) json = r.ReadToEnd();
            var raw = (IEnumerable)new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(json);
            var list = new List<Tweak>();
            foreach (Dictionary<string, object> d in raw)
            {
                var t = new Tweak
                {
                    Id = Str(d, "id"),
                    Name = Str(d, "name"),
                    NameEn = Str(d, "name_en"),
                    Category = Str(d, "category"),
                    Risk = Str(d, "risk") ?? "low",
                    After = Str(d, "after"),
                    Description = Str(d, "description"),
                    DescriptionEn = Str(d, "description_en"),
                    Hidden = d.TryGetValue("hidden", out var h) && h is bool b && b,
                };
                t.Keywords = (Strings(d, "keywords") is List<string> kw ? string.Join(" ", kw) : Str(d, "keywords")) + " " + t.Name + " " + t.NameEn;
                if (L.En)
                {
                    // английский интерфейс: везде, где показывается твик, берём английский текст
                    t.Name = t.NameEn ?? t.Name;
                    t.Description = t.DescriptionEn ?? t.Description;
                    t.After = Str(d, "after_en") ?? t.After;
                }
                t.Windows = Strings(d, "windows") ?? t.Windows;
                t.Refresh = Strings(d, "refresh") ?? t.Refresh;
                if (d.TryGetValue("actions", out var acts) && acts is IEnumerable e)
                    t.Actions = e.Cast<Dictionary<string, object>>().ToList();
                list.Add(t);
            }
            return list;
        }
    }
}
