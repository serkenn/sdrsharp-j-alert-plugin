// Per-install plugin settings, persisted as JSON under
// %APPDATA%\SDRSharp.JAlert\settings.json. Kept self-contained (rather than
// depending on SDR#'s internal settings store) so the plugin owns its own
// configuration lifecycle. Mirrors the persisted fields of the original SDR++
// module (main.cpp config keys).

using System;
using System.IO;
using System.Text.Json;

namespace SDRSharp.JAlert
{
    public sealed class JAlertSettings
    {
        public string XmlOutputDir { get; set; }
        public bool FileOutputEnabled { get; set; }
        public string JsonlFilePath { get; set; }
        public int JsonlTcpPort { get; set; } = 7355;
        public bool JsonlFileEnabled { get; set; }
        public bool JsonlTcpEnabled { get; set; }

        private static string ConfigDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         "SDRSharp.JAlert");

        private static string ConfigPath => Path.Combine(ConfigDir, "settings.json");

        // Default output locations under the plugin config root.
        public static string DefaultOutputDir => Path.Combine(ConfigDir, "j_alert");

        public static JAlertSettings Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath);
                    JAlertSettings s = JsonSerializer.Deserialize<JAlertSettings>(json);
                    if (s != null)
                    {
                        s.Normalize();
                        return s;
                    }
                }
            }
            catch
            {
                // Fall through to defaults on any read/parse failure.
            }
            JAlertSettings d = new JAlertSettings();
            d.Normalize();
            return d;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(ConfigDir);
                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ConfigPath, json);
            }
            catch
            {
                // Best-effort persistence; ignore write failures.
            }
        }

        private void Normalize()
        {
            if (string.IsNullOrEmpty(XmlOutputDir)) XmlOutputDir = DefaultOutputDir;
            if (string.IsNullOrEmpty(JsonlFilePath)) JsonlFilePath = Path.Combine(DefaultOutputDir, "decoded.jsonl");
            if (JsonlTcpPort <= 0 || JsonlTcpPort > 65535) JsonlTcpPort = 7355;
        }
    }
}
