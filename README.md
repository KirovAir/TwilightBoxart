<a href="https://github.com/KirovAir/TwilightBoxart/raw/master/img/screenshot.png"><img alt="screenshot" src="https://github.com/KirovAir/TwilightBoxart/raw/master/img/screenshot.png" height="450"></a>

# Twilight Boxart
A boxart downloader written in C#. Uses various sources and scan methods to determine the correct boxart. 
Written for [TwilightMenu++](https://github.com/DS-Homebrew/TWiLightMenu) but can be used for other loader UI's with some config changes. 😊

## Supported rom types
 System | Matching (in order)
 --- | ---
 Nintendo - Game Boy | (sha1 / filename)
 Nintendo - Game Boy Color | (sha1 / filename)
 Nintendo - Game Boy Advance | (sha1 / filename)
 Nintendo - Nintendo DS | (titleid / sha1 / filename)
 Nintendo - Nintendo DSi | (titleid / sha1 / filename)
 Nintendo - Nintendo DSi (DSiWare) | (titleid / sha1 / filename)
 Nintendo - Nintendo Entertainment System | (sha1 / filename)
 Nintendo - Super Nintendo Entertainment System | (sha1 / filename)
 Nintendo - Family Computer Disk System | (sha1 / filename)
 Sega - Mega Drive - Genesis | (sha1 / filename)
 Sega - Master System - Mark III | (sha1 / filename)
 Sega - Game Gear | (sha1 / filename)

## Boxart sources
* [GameTDB](https://gametdb.com) using titleid matching.
* [LibRetro](https://github.com/libretro/libretro-thumbnails) using [NoIntro](https://datomatic.no-intro.org) sha1 matching or simply filename matching. [LibRetro DAT](https://github.com/libretro/libretro-database/tree/master/dat) is currently added as extra NES sha1 source.

## Download
[Here](https://github.com/KirovAir/TwilightBoxart/releases).

## To-do
* Add support for more consoles. (redump.org as disc source)
* Prefilled config support for different loaders. (RetroArch, Wii etc.)