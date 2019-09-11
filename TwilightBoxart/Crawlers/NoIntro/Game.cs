using System.Xml.Serialization;

namespace TwilightBoxart.Crawlers.NoIntro
{
    [XmlRoot(ElementName = "game")]
    public class Game
    {
        [XmlElement(ElementName = "description")]
        public string Description { get; set; }
        [XmlElement(ElementName = "rom")]
        public Rom Rom { get; set; }
        [XmlAttribute(AttributeName = "name")]
        public string Name { get; set; }
        [XmlElement(ElementName = "game_id")]
        public string Game_id { get; set; }
    }
}
