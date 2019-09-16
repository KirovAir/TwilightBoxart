using TwilightBoxart.Models.Base;

namespace TwilightBoxart.Data
{
    public class RomMetaData
    {
        public ConsoleType ConsoleType { get; set; }
        public ConsoleType ConsoleSubType { get; set; }
        public string Name { get; set; }
        public string Sha1 { get; set; }
        public string Serial { get; set; }
        public string GameId { get; set; }
        public string Status { get; set; }
        public string Title { get; set; }
        public string TitleId { get; set; }
    }
}