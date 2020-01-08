using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using KirovAir.Core.Utilities;
using TwilightBoxart.Models.Base;
using Utf8Json;

namespace TwilightBoxart.Data
{
    public class RomDatabase
    {
        private readonly string _databasePath;
        private List<RomMetaData> _roms = new List<RomMetaData>();

        public RomDatabase(string databasePath)
        {
            _databasePath = databasePath;
        }

        public void Initialize(IProgress<string> progress = null)
        {
            if (!File.Exists(_databasePath))
            {
                progress?.Report($"Database was not found at {_databasePath}. Downloading..");
                using (var wc = new WebClient())
                {
                    wc.DownloadFile(BoxartConfig.NoIntroDbUrl, _databasePath);
                    progress?.Report("Downloaded database from Github.");
                }
            }

            try
            {
                using (var compressed = File.OpenRead(_databasePath))
                {
                    _roms = JsonSerializer.Deserialize<List<RomMetaData>>(FileHelper.Decompress(compressed));
                }
            }
            catch (Exception e)
            {
                progress?.Report("Error reading NoIntro DB: " + e);
            }

            if (_roms == null || _roms.Count == 0)
            {
                throw new Exception("NoIntro.db is empty!");
            }

            progress?.Report($"Loaded NoIntro.db. Database contains {_roms?.Count} roms.");
        }

        public void AddMetadata(IRom rom)
        {
            RomMetaData result = null;
            if (!string.IsNullOrEmpty(rom.Sha1))
            {
                result = _roms.FirstOrDefault(c => c.Sha1.ToLower() == rom.Sha1.ToLower());
            }

            if (result == null && !string.IsNullOrEmpty(rom.TitleId))
            {
                var results =
                 _roms.Where(c => c.ConsoleType == rom.ConsoleType &&
                                                                      (c.Serial?.ToLower() == rom.TitleId.ToLower()
                                                                       || c.TitleId?.ToLower() == rom.TitleId.ToLower())).ToList();

                // Do logic to fix dupes.. Todo: make this actually solid but for now we filter bad dumps.
                if (results.Count > 1)
                {
                    result = results.FirstOrDefault(c => !c.Status?.Contains("bad") ?? true); // lol
                }
                else
                {
                    result = results.FirstOrDefault();
                }
            }

            if (result == null) return;

            rom.NoIntroName = result.Name;
            rom.NoIntroConsoleType = result.ConsoleSubType;
        }
    }
}
