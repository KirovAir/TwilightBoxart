using System;
using System.Collections.Specialized;
using System.IO;

namespace TwilightBoxart.Helpers
{
    public abstract class IniSettings : AppSettings
    {
        public void Load(string file, bool prefixSections = false)
        {
            var nv = new NameValueCollection();
            var lines = File.ReadAllLines(file);
            var currentSection = "";
            foreach (var line in lines)
            {
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    currentSection = line.TrimStart('[').TrimEnd(']');
                }

                // Ignore comments
                if (line.StartsWith(";") || line.StartsWith("/") || !line.Contains("="))
                    continue;

                var s = line.Split(new [] { '=' }, StringSplitOptions.RemoveEmptyEntries);
                if (s.Length <= 1)
                    continue;

                if (s[1].Contains(";"))
                {
                    s[1] = s[1].Split(';')[0];
                }

                var key = s[0].Trim();
                var value = s[1].Trim();

                if (s.Length > 2) // Handle values with '=' in it.
                {
                    for (var i = 2; i < s.Length; i++)
                    {
                        value += $"={s[i]}";
                    }
                }

                nv.Add((prefixSections ? currentSection : "") + key, value);
            }

            Load(nv);
        }
    }
}
