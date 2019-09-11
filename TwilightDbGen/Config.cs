using KirovAir.Core.Config;

namespace TwilightDbGen
{
    public class Config : IniSettings
    {
        public string ScanDir { get; set; }
        public string OutputFile { get; set; }
    }
}
