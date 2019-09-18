using System.Collections.Specialized;
using System.IO;

namespace KirovAir.Core.Config
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

                var s = line.Split('=');
                if (s.Length <= 1)
                    continue;

                var key = s[0];
                var value = s[1];

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
