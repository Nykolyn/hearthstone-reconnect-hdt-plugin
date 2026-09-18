using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace MyReconnector
{
    /// <summary>
    /// Tiny key=value settings file, stored outside HDT so plugin updates never lose it.
    /// </summary>
    public class PluginConfig
    {
        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MyReconnector", "config.txt");

        public double ButtonX = 0.85; // fraction of overlay width
        public double ButtonY = 0.75; // fraction of overlay height
        public bool ShowButton = true;
        public bool EnableHotkey = true;

        public static PluginConfig Load()
        {
            var cfg = new PluginConfig();
            try
            {
                if (File.Exists(ConfigPath))
                {
                    foreach (var line in File.ReadAllLines(ConfigPath))
                    {
                        var idx = line.IndexOf('=');
                        if (idx <= 0)
                            continue;
                        var key = line.Substring(0, idx).Trim();
                        var value = line.Substring(idx + 1).Trim();
                        switch (key)
                        {
                            case "ButtonX":
                                double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out cfg.ButtonX);
                                break;
                            case "ButtonY":
                                double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out cfg.ButtonY);
                                break;
                            case "ShowButton":
                                bool.TryParse(value, out cfg.ShowButton);
                                break;
                            case "EnableHotkey":
                                bool.TryParse(value, out cfg.EnableHotkey);
                                break;
                        }
                    }
                }
            }
            catch
            {
                // corrupted config — fall back to defaults
            }
            cfg.ButtonX = Clamp01(cfg.ButtonX);
            cfg.ButtonY = Clamp01(cfg.ButtonY);
            return cfg;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath));
                var lines = new List<string>
                {
                    "ButtonX=" + ButtonX.ToString(CultureInfo.InvariantCulture),
                    "ButtonY=" + ButtonY.ToString(CultureInfo.InvariantCulture),
                    "ShowButton=" + ShowButton,
                    "EnableHotkey=" + EnableHotkey
                };
                File.WriteAllLines(ConfigPath, lines);
            }
            catch
            {
                // non-fatal
            }
        }

        private static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);
    }
}
