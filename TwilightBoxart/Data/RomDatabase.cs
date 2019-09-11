using System;
using System.Collections.Generic;
using System.Linq;
using KirovAir.Core.Extensions;
using SQLite;
using TwilightBoxart.Crawlers.NoIntro;
using TwilightBoxart.Models.Base;
using static System.String;

namespace TwilightBoxart.Data
{
    public class RomDatabase
    {
        private readonly SQLiteConnection _db;
        public List<RomMetaData> Roms = new List<RomMetaData>();

        public RomDatabase(string databasePath)
        {
            _db = new SQLiteConnection(databasePath);
            _db.CreateTable<RomMetaData>();

            if (!_db.Table<RomMetaData>().Any())
            {
                Console.WriteLine("No database was found! Downloading No-Intro DB..");

                foreach (var (key, value) in Config.NoIntroDbMapping)
                {
                    Console.Write($"{key.GetDescription()}.. ");

                    var data = NoIntroCrawler.GetDataFile(key).Result;
                    foreach (var game in data.Game)
                    {
                        var rom = new RomMetaData
                        {
                            ConsoleType = value,
                            NoIntroConsoleType = key,
                            GameId = game.Game_id,
                            Name = game.Name,
                            Serial = game.Rom?.Serial,
                            Sha1 = game.Rom?.Sha1.ToLower(),
                            Status = game.Rom?.Status
                        };
                        Roms.Add(rom);
                    }

                    Console.WriteLine($"Found {data.Game.Count} roms");
                }

                Console.Write("Flushing data..");
                _db.InsertAll(Roms);
                Console.WriteLine("Done!");
            }
            else
            {
               Roms = _db.Table<RomMetaData>().ToList();
            }

            Console.WriteLine($"Database contains {_db.Table<RomMetaData>().Count()} roms.");
        }
        
        public void AddMetadata(IRom rom)
        {
            RomMetaData result = null;
            if (!IsNullOrEmpty(rom.Sha1))
            {
                result = _db.Table<RomMetaData>().FirstOrDefault(c => c.Sha1.ToLower() == rom.Sha1.ToLower());
            }

            if (result == null && !IsNullOrEmpty(rom.TitleId))
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

            rom.NoIntroName = result?.Name;
        }
    }
}
