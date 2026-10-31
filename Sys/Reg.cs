using Microsoft.Win32;

namespace WinOpti.Sys
{
    /// <summary>Реестр всегда в 64-битном представлении: иначе на 64-битной Windows часть ключей уезжает в WOW6432Node.</summary>
    internal static class Reg
    {
        public static RegistryKey Root(RegistryHive hive) => RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);

        public static object Get(RegistryHive hive, string path, string name)
        {
            using (var k = Root(hive).OpenSubKey(path))
                return k?.GetValue(name);
        }

        public static int? GetInt(RegistryHive hive, string path, string name)
        {
            var v = Get(hive, path, name);
            if (v is int i) return i;
            if (v is string s && int.TryParse(s, out var n)) return n;
            return null;
        }

        public static void SetDword(RegistryHive hive, string path, string name, int value)
        {
            using (var k = Root(hive).CreateSubKey(path, true))
                k.SetValue(name, value, RegistryValueKind.DWord);
        }

        public static void SetString(RegistryHive hive, string path, string name, string value)
        {
            using (var k = Root(hive).CreateSubKey(path, true))
                k.SetValue(name, value, RegistryValueKind.String);
        }
    }
}
