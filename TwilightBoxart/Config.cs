using KirovAir.Core.Config;

namespace TwilightBoxart
{
    public class Config : IniSettings
    {
        public string SdRoot { get; set; } = "";
        public string RomsDir { get; set; } = "";
        public string BoxArtDir { get; set; } = @"_nds\TWiLightMenu\boxart";
    }
}
