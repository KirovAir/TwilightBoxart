namespace TwilightBoxart.Downloaders.LibRetro
{
    //    game (
    //      name "2 Game Pack! - Hot Wheels - Stunt Track Challenge + Hot Wheels - World Race (USA, Europe)"
    //      description "2 Game Pack! - Hot Wheels - Stunt Track Challenge + Hot Wheels - World Race (USA, Europe)"
    //      rom (name "2 Game Pack! - Hot Wheels - Stunt Track Challenge + Hot Wheels - World Race (USA, Europe).gba" size 16777216 crc 20929EC1 md5 4DAF3D378D5F91277F43A5555829FDC7 sha1 717B2A739C8932374AB48A9C2BBD76A44B4CF2F3 )
    //    )
    public class RomData
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string RomName { get; set; }
        public string RomSize { get; set; }
        public string RomCrc { get; set; }
        public string RomMd5 { get; set; }
        public string RomSha1 { get; set; }
        
        public override string ToString()
        {
            return $"{Name} - {RomSize}";
        }
    }
}
