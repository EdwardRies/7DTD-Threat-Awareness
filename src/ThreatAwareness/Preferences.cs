using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace ThreatAwareness
{
    public sealed class Preferences
    {
        private readonly string path;
        private readonly Action<string> warn;
        private Dictionary<string, bool> values = new Dictionary<string, bool>(StringComparer.Ordinal);
        private bool writable = true;
        public Preferences(string path, Action<string> warn) { this.path = path; this.warn = warn; Load(); }
        public bool Get(string identity, bool defaultValue) => identity != null && values.TryGetValue(identity, out var value) ? value : defaultValue;
        public bool Has(string identity) => identity != null && values.ContainsKey(identity);

        public bool Set(string identity, bool enabled)
        {
            if (string.IsNullOrWhiteSpace(identity)) return false;
            // Honor this session's preference even if storage is unavailable. Never claim it was saved.
            values[identity] = enabled;
            if (!writable) return false;
            var temp = path + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var doc = new XDocument(new XElement("Preferences", new XAttribute("version", 1),
                    values.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => new XElement("Player", new XAttribute("id", k.Key), new XAttribute("enabled", k.Value)))));
                using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                { doc.Save(stream); stream.Flush(true); }
                if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
                else File.Move(temp, path);
                return true;
            }
            catch (Exception ex) { warn("Preference save failed: " + ex.Message); return false; }
        }

        private static Dictionary<string, bool> Read(string file)
        {
            var root = Settings.ReadXml(file).Root;
            if (root == null || root.Name != "Preferences" || (string)root.Attribute("version") != "1") throw new FormatException("Unknown preferences format.");
            var result = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var entry in root.Elements("Player"))
            {
                string id = (string)entry.Attribute("id");
                if (string.IsNullOrWhiteSpace(id)) throw new FormatException("Missing stable player ID.");
                result.Add(id, bool.Parse((string)entry.Attribute("enabled")));
            }
            return result;
        }

        private void Load()
        {
            try { if (File.Exists(path)) { values = Read(path); return; } }
            catch (Exception ex)
            {
                warn("Preferences unreadable; preserving original and trying backup: " + ex.Message);
                try { File.Move(path, path + ".corrupt-" + Guid.NewGuid().ToString("N")); }
                catch (Exception moveError) { writable = false; warn("Cannot preserve damaged file; disk writes disabled: " + moveError.Message); }
            }
            try { if (File.Exists(path + ".bak")) values = Read(path + ".bak"); }
            catch (Exception ex) { warn("Backup unreadable; using player defaults: " + ex.Message); }
        }
    }

    public static class PlayerCommand
    {
        // Exact token; !threat works around earlier mods that consume all slash chat.
        public static bool TryParse(string message, out string action)
        {
            action = null;
            if (string.IsNullOrWhiteSpace(message)) return false;
            var words = message.Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (!string.Equals(words[0], "/threat", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(words[0], "!threat", StringComparison.OrdinalIgnoreCase)) return false;
            action = words.Length == 1 ? "help" : words.Length == 2 ? words[1].ToLowerInvariant() : "help";
            return true;
        }
    }
}
