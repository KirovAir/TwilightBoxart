using KirovAir.Core.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using TwilightBoxart.Models;

namespace TwilightBoxart.Downloaders.LibRetro
{
    public class LibRetroArtDownloader : IDisposable
    {
        public string DatUrl { get; }
        public string ContentUrl { get; }
        public WebClient WebClient { get; set; } = new WebClient();
        public List<RomData> Roms { get; set; } = new List<RomData>();

        public LibRetroArtDownloader(string datUrl, string contentUrl)
        {
            DatUrl = datUrl;
            ContentUrl = contentUrl;

            var datData = WebClient.DownloadString(DatUrl);
            Roms = ParseDat<RomData>(datData, "game");
        }

        public void Download(IRom rom, string location)
        {
            var romData = Roms.FirstOrDefault(c => c.RomMd5.ToLower() == rom.Md5Hash.ToLower());

            if (romData == null)
            {
                throw new Exception("Could not find rom by md5 hash!");
            }

            var url = FileHelper.CombineUri(ContentUrl, $"{romData.Name}.png");
            WebClient.DownloadFile(url, location);
        }
        
        /// <summary>
        /// Parses a libretro .dat file. The code is horrible and so is the format ..
        /// y no standards libretro? 
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

            for (int i = index; i < values.Count; i++)
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
}
