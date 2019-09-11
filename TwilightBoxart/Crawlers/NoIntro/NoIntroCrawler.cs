using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;
using KirovAir.Core.Extensions;

namespace TwilightBoxart.Crawlers.NoIntro
{
    public static class NoIntroCrawler
    {
        public static async Task<DataFile> GetDataFile(NoIntroConsoleType consoleType)
        {
            var baseAddress = new Uri("https://datomatic.no-intro.org/");
            var downloadUri = "/index.php?page=download&fun=wut";
            using (var handler = new HttpClientHandler { UseCookies = true })
            using (var client = new HttpClient(handler) { BaseAddress = baseAddress })
            {
                var message = new HttpRequestMessage(HttpMethod.Get, downloadUri);
                var result = await client.SendAsync(message);
                result.EnsureSuccessStatusCode();

                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("download", "Download"),
                    new KeyValuePair<string, string>("sel_s", consoleType.GetDescription())
                });
                result = await client.PostAsync(downloadUri, content);
                result.EnsureSuccessStatusCode();

                var file = await result.Content.ReadAsStreamAsync(); // get the actual content stream
                using (var archive = new ZipArchive(file))
                {
                    var xmlFile = archive.Entries[0];
                    var ser = new XmlSerializer(typeof(DataFile));
                    using (XmlReader reader = XmlReader.Create(xmlFile.Open(), new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore }))
                    {
                        var data = (DataFile)ser.Deserialize(reader);
                        return data;
                    }
                }
            }
        }
    }
}
