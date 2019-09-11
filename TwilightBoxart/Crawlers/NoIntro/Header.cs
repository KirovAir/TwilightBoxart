using System.Xml.Serialization;

namespace TwilightBoxart.Crawlers.NoIntro
{
    [XmlRoot(ElementName = "header")]
    public class Header
    {
        [XmlElement(ElementName = "name")]
        public string Name { get; set; }
        [XmlElement(ElementName = "description")]
        public string Description { get; set; }
        [XmlElement(ElementName = "version")]
        public string Version { get; set; }
        [XmlElement(ElementName = "author")]
        public string Author { get; set; }
        [XmlElement(ElementName = "homepage")]
        public string Homepage { get; set; }
        [XmlElement(ElementName = "url")]
        public string Url { get; set; }
    }
}
