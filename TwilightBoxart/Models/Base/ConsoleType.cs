using SQLite;

namespace TwilightBoxart.Models.Base
{
    [StoreAsText]
    public enum ConsoleType
    {
        Unknown,
        Gb,
        Gbc,
        Gba,
        Nds,
        Dsi,
        Nes,
        Snes
    }
}
