using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KirovAir.Core.Extensions;
using KirovAir.Core.Utilities;
using TwilightBoxart.Crawlers.LibRetro;
using TwilightBoxart.Crawlers.NoIntro;
using TwilightBoxart.Models.Base;
using Utf8Json;

namespace TwilightBoxart.Data
{
    public class RomDatabase
    {
        private readonly string _databasePath;
        private List<RomMetaData> _roms = new List<RomMetaData>();
        private readonly RetryHelper _retryHelper = new RetryHelper(TimeSpan.FromSeconds(3));

        public RomDatabase(string databasePath)
        {
            _databasePath = databasePath;
        }

        public void Initialize(IProgress<string> progress = null)
        {
            if (File.Exists(_databasePath))
            {
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
            }

            if (_roms == null || _roms.Count == 0)
            {
                progress?.Report("No valid database was found! Downloading No-Intro DB..");
                _roms = new List<RomMetaData>();

                foreach (var map in BoxartConfig.NoIntroDbMapping)
                {
                    progress?.Report($"{map.Key.GetDescription()}.. ");

                    DataFile data = null;
                    _retryHelper.RetryOnException(() =>
                    {
                        data = NoIntroCrawler.GetDataFile(map.Key).Result;
                    });

                    foreach (var game in data.Game)
                    {
                        var rom = new RomMetaData
                        {
                            ConsoleType = map.Value,
                            ConsoleSubType = map.Key,
                            GameId = game.Game_id,
                            Name = game.Name,
                            Serial = game.Rom?.Serial,
                            Sha1 = game.Rom?.Sha1.ToLower(),
                            Status = game.Rom?.Status
                        };
                        _roms.Add(rom);
                    }

                    progress?.Report($"Found {data.Game.Count} roms");
                }

                progress?.Report("Downloading extra LibRetro data..");

                foreach (var map in BoxartConfig.LibRetroDatUrls)
                {
                    progress?.Report($"{map.Key.GetDescription()}.. ");

                    List<LibRetroRomData> data = null;
                    _retryHelper.RetryOnException(() => { data = LibRetroDat.DownloadDat(map.Value); });

                    foreach (var game in data)
                    {
                        var rom = new RomMetaData
                        {
                            ConsoleType = map.Key,
                            ConsoleSubType = map.Key,
                            Name = game.Name,
                            Sha1 = game.RomSha1.ToLower()
                        };
                        _roms.Add(rom);
                    }

                    progress?.Report($"Found {data.Count} roms");
                }

                progress?.Report("Flushing data..");
                using (var ms = new MemoryStream())
                {
                    JsonSerializer.Serialize(ms, _roms);
                    ms.Seek(0, SeekOrigin.Begin);
                    File.WriteAllBytes(_databasePath, FileHelper.Compress(ms));
                }

                progress?.Report("Done!");
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
