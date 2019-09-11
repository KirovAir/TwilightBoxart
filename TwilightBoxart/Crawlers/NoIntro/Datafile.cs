using System.Collections.Generic;
using System.Xml.Serialization;

namespace TwilightBoxart.Crawlers.NoIntro
{
    [XmlRoot(ElementName = "datafile")]
    public class DataFile
    {
        [XmlElement(ElementName = "header")]
        public Header Header { get; set; }

        [XmlElement(ElementName = "game")]
        public List<Game> Game { get; set; }
    }
}
