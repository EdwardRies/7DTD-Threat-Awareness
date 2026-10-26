using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace ThreatAwareness
{
    public enum ThreatLevel { None, Caution, Warning, HighWarning, Danger }

    public sealed class Settings
    {
        public bool Enabled = true;
        public bool DefaultEnabled = false;
        public double IntervalSeconds = 1;
        public double HysteresisMeters = 1;
        // Index matches ThreatLevel. All four thresholds must be strictly descending.
        public readonly double[] Distances = { 0, 100, 50, 25, 10 };
        public readonly bool[] Levels = { false, true, true, true, true };
        public bool Zombies = true;
        public bool HostileAnimals = true;
        public bool IncludeSleepers = false;
        public readonly HashSet<string> ExcludedClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public double Range
        {
            get
            {
                for (int i = 1; i <= 4; i++) if (Levels[i]) return Distances[i];
                return 0;
            }
        }

        public static Settings Load(string path)
        {
            var s = new Settings();
            if (!File.Exists(path)) return s;
            var root = ReadXml(path).Root;
            if (root == null || root.Name != "ThreatAwareness") throw new FormatException("Expected ThreatAwareness root.");
            s.Enabled = Bool(root, "enabled", true);
            s.DefaultEnabled = Bool(root, "defaultEnabled", false);
            s.IntervalSeconds = Number(root, "intervalSeconds", 1);
            s.HysteresisMeters = Number(root, "hysteresisMeters", 1);
            var threats = root.Element("Levels");
            if (threats != null)
            {
                foreach (var e in threats.Elements())
                {
                    ThreatLevel level;
                    if (!Enum.TryParse(e.Name.LocalName, out level) || level == ThreatLevel.None || !Enum.IsDefined(typeof(ThreatLevel), level))
                        throw new FormatException("Unknown threat level: " + e.Name);
                    s.Distances[(int)level] = Number(e, "meters", s.Distances[(int)level]);
                    s.Levels[(int)level] = Bool(e, "enabled", true);
                }
                if (threats.Elements().GroupBy(e => e.Name).Any(g => g.Count() > 1)) throw new FormatException("Duplicate threat level.");
            }
            var eligibility = root.Element("Eligibility");
            if (eligibility != null)
            {
                s.Zombies = Bool(eligibility, "zombies", true);
                s.HostileAnimals = Bool(eligibility, "hostileAnimals", true);
                s.IncludeSleepers = Bool(eligibility, "includeSleepers", false);
                foreach (var e in eligibility.Elements("Exclude"))
                {
                    var name = (string)e.Attribute("class");
                    if (string.IsNullOrWhiteSpace(name)) throw new FormatException("Exclude requires a class name.");
                    s.ExcludedClasses.Add(name.Trim());
                }
            }
            s.Validate();
            return s;
        }

        public void Validate()
        {
            if (!Finite(IntervalSeconds) || IntervalSeconds < 0.1 || IntervalSeconds > 60) throw new FormatException("intervalSeconds must be 0.1..60.");
            if (!Finite(HysteresisMeters) || HysteresisMeters < 0 || HysteresisMeters > 2) throw new FormatException("hysteresisMeters must be 0..2.");
            for (int i = 1; i <= 4; i++)
            {
                if (!Finite(Distances[i]) || Distances[i] <= 0 || Distances[i] > 250) throw new FormatException("Distances must be >0 and <=250 meters.");
                if (i > 1 && Distances[i] >= Distances[i - 1]) throw new FormatException("Distances must satisfy Caution > Warning > HighWarning > Danger.");
            }
        }

        public static XDocument ReadXml(string path)
        {
            using (var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
                return XDocument.Load(reader);
        }

        private static bool Bool(XElement e, string name, bool fallback) => e.Attribute(name) == null ? fallback : bool.Parse((string)e.Attribute(name));
        private static double Number(XElement e, string name, double fallback) => e.Attribute(name) == null ? fallback : double.Parse((string)e.Attribute(name), CultureInfo.InvariantCulture);
        private static bool Finite(double n) => !double.IsNaN(n) && !double.IsInfinity(n);
    }

    public static class ThreatEvaluator
    {
        public static ThreatLevel Evaluate(double nearestMeters, ThreatLevel previous, Settings s)
        {
            if (!s.Enabled || double.IsNaN(nearestMeters) || nearestMeters < 0 || nearestMeters > s.Range) return ThreatLevel.None;
            var candidate = ThreatLevel.None;
            for (int i = 4; i >= 1; i--)
                if (s.Levels[i] && nearestMeters <= s.Distances[i]) { candidate = (ThreatLevel)i; break; }
            // Escalate immediately. Delay only de-escalation, never beyond the outer detection range.
            int old = (int)previous;
            if (old >= 1 && old <= 4 && s.Levels[old] && candidate < previous && nearestMeters <= s.Distances[old] + s.HysteresisMeters)
                return previous;
            return candidate;
        }
    }
}
