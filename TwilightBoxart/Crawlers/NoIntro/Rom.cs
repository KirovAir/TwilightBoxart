using System.Xml.Serialization;

namespace TwilightBoxart.Crawlers.NoIntro
{
    [XmlRoot(ElementName = "rom")]
    public class Rom
    {
        [XmlAttribute(AttributeName = "name")]
        public string Name { get; set; }
        [XmlAttribute(AttributeName = "size")]
        public string Size { get; set; }
        [XmlAttribute(AttributeName = "crc")]
        public string Crc { get; set; }
        [XmlAttribute(AttributeName = "md5")]
        public string Md5 { get; set; }
        [XmlAttribute(AttributeName = "sha1")]
        public string Sha1 { get; set; }
        [XmlAttribute(AttributeName = "status")]
        public string Status { get; set; }
        [XmlAttribute(AttributeName = "serial")]
        public string Serial { get; set; }
    }
}
