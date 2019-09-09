using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace TwilightBoxart.Downloaders.LibRetro
{
    public class LibRetroArtDownloader : IDisposable
    {
        public string DatUrl { get; }
        public string ContentUrl { get; }
        public WebClient WebClient { get; set; } = new WebClient();

        public LibRetroArtDownloader(string datUrl, string contentUrl)
        {
            DatUrl = datUrl;
            ContentUrl = contentUrl;

            var datData = WebClient.DownloadString(DatUrl);
            var roms = ParseDat<RomData>(datData, "game");
            
        }

        /// <summary>
        /// Parses a libretro dat file. The code is horrible but so is the format ..
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="data"></param>
        /// <param name="root"></param>
        /// <returns></returns>
        public List<T> ParseDat<T>(string data, string root)
        {
            var result = new List<T>();
            var split = new List<string>();
            var instr = false;
            var value = "";         

            // Remove whitespace and shit. Just get the values.
            foreach (var c in data)
            {
                if (c == '"')
                {
                    instr = !instr;
                    continue;
                }
                if (!instr && c == ' ' || c == '\t' || c == '\n')
                {
                    if (value != "")
                    {
                        split.Add(value);
                        value = "";
                    }
                    continue;
                }
                value += c;
            }

            // Parse them.
            var skip = false;
            var indent = 0;
            for (int i = 0; i < split.Count; i++)
            {
                if (!skip)
                {
                    if (split[i].ToLower() == root.ToLower())
                    {
                        i += 2;
                        var entity = Activator.CreateInstance<T>();
                        ListToObj(entity, split, "", ref i);
                        result.Add(entity);
                        continue;
                    }
                    else
                    {
                        skip = true;
                        continue;
                    }
                }
                if (split[i] == "(")
                {
                    indent++;
                }
                if (split[i] == ")")
                {
                    indent--;
                }
                if (skip && indent == 0)
                {
                    skip = false;
                }
            }

            return result;
        }

        public void ListToObj<T>(T obj, List<string> values, string key, ref int index)
        {
            var properties = obj.GetType().GetProperties();
                        var currentKey = "";
            for (int i = index;  i < values.Count; i++)
            {
                var value = values[i];

                if (value == "(")
                {
                    i++;
                    ListToObj(obj, values, currentKey, ref i);
                    continue;
                }
                if (value == ")")
                {
                    index = i++;
                    return;
                }
                if (currentKey == "")
                {
                    currentKey = key + value;
                }
                else
                {
                    var property = properties.FirstOrDefault(c => c.Name.ToLower() == currentKey.ToLower());
                    if (property != null)
                        property.SetValue(obj, value);

                    currentKey = "";
                }
            }

        }

        public void Dispose()
        {
            WebClient?.Dispose();
        }
    }

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
