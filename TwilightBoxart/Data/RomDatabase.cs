using System;
using System.Collections.Generic;
using System.Linq;
using KirovAir.Core.Extensions;
using SQLite;
using TwilightBoxart.Crawlers.NoIntro;
using TwilightBoxart.Models.Base;

namespace TwilightBoxart.Data
{
    public class RomDatabase
    {
        private readonly string _databasePath;
        private SQLiteConnection _db;

        public RomDatabase(string databasePath)
        {
            _databasePath = databasePath;
        }

        public void Initialize(IProgress<string> progress = null)
        {
            _db = new SQLiteConnection(_databasePath);
            _db.CreateTable<RomMetaData>();
            
            var hasRecords = false;
            try
            {
                hasRecords = _db.Table<RomMetaData>().Any();
            }
            catch (Exception e)
            {
                progress?.Report("An error occured while accessing localDb: " + e.Message);
            }

            if (!hasRecords)
            {
                var roms = new List<RomMetaData>();
                progress?.Report("No valid database was found! Downloading No-Intro DB..");
                _db.DropTable<RomMetaData>();
                _db.CreateTable<RomMetaData>();
                foreach (var (key, value) in BoxartConfig.NoIntroDbMapping)
                {
                    progress?.Report($"{key.GetDescription()}.. ");

                    var data = NoIntroCrawler.GetDataFile(key).Result;
                    foreach (var game in data.Game)
                    {
                        var rom = new RomMetaData
                        {
                            ConsoleType = value,
                            ConsoleSubType = key,
                            GameId = game.Game_id,
                            Name = game.Name,
                            Serial = game.Rom?.Serial,
                            Sha1 = game.Rom?.Sha1.ToLower(),
                            Status = game.Rom?.Status
                        };
                        roms.Add(rom);
                    }

                    progress?.Report($"Found {data.Game.Count} roms");
                }

                progress?.Report("Flushing data..");
                _db.InsertAll(roms);
                roms = null;
                progress?.Report("Done!");
            }

            progress?.Report($"Database contains {_db.Table<RomMetaData>().Count()} roms.");
        }

        public void AddMetadata(IRom rom)
        {
            RomMetaData result = null;
            if (!string.IsNullOrEmpty(rom.Sha1))
            {
                result = _db.Table<RomMetaData>().FirstOrDefault(c => c.Sha1.ToLower() == rom.Sha1.ToLower());
            }

            if (result == null && !string.IsNullOrEmpty(rom.TitleId))
            {
                var results =
                 _db.Table<RomMetaData>().Where(c => c.ConsoleType == rom.ConsoleType &&
                                                                      (c.Serial.ToLower() == rom.TitleId.ToLower()
                                                                       || c.TitleId.ToLower() == rom.TitleId.ToLower())).ToList();
                // Do logic to fix dupes..
                if (results.Count > 1)
                {
                    result = results.FirstOrDefault(c => !c.Status.Contains("bad")); // lol
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
