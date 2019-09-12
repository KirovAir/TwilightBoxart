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
        private readonly SQLiteConnection _db;

        public RomDatabase(string databasePath)
        {
            _db = new SQLiteConnection(databasePath);
            _db.CreateTable<RomMetaData>();

            var hasRecords = false;
            try
            {
                hasRecords = _db.Table<RomMetaData>().Any();
            }
            catch (Exception e)
            {
                Console.WriteLine("An error occured while accessing localDb: " + e.Message);
            }

            if (!hasRecords)
            {
                ReInitDb();
            }

            Console.WriteLine($"Database contains {_db.Table<RomMetaData>().Count()} roms.");
        }

        public void ReInitDb()
        {
            var roms = new List<RomMetaData>();
            Console.WriteLine("No valid database was found! Downloading No-Intro DB..");
            _db.DropTable<RomMetaData>();
            _db.CreateTable<RomMetaData>();
            foreach (var (key, value) in Config.NoIntroDbMapping)
            {
                Console.Write($"{key.GetDescription()}.. ");

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

                Console.WriteLine($"Found {data.Game.Count} roms");
            }

            Console.Write("Flushing data..");
            _db.InsertAll(roms);
            roms = null;
            Console.WriteLine("Done!");
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
