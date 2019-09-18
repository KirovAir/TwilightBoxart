![Screenshot](https://https://raw.githubusercontent.com/KirovAir/TwilightBoxart/master/img/screenshot.png)

# Twilight Boxart
A boxart downloader written in C#. Uses various sources and scan methods to determine the correct boxart. 
Written for TwilightMenu++ but can be used for other loader UI's with some config changes. 😊

## Supported rom types
* Nintendo - Game Boy
* Nintendo - Game Boy Color
* Nintendo - Game Boy Advance
* Nintendo - Nintendo DS
* Nintendo - Nintendo DSi
* Nintendo - Nintendo DSi (DSiWare)
* Nintendo - Nintendo Entertainment System
* Nintendo - Super Nintendo Entertainment System
* Sega - Mega Drive - Genesis
* Sega - Master System - Mark III
* Sega - Game Gear

## Boxart sources
* [GameTDB](https://gametdb.com) using titleid matching.
* [LibRetro](https://github.com/libretro/libretro-thumbnails) using [NoIntro](https://datomatic.no-intro.org) sha1 matching.

## Download
[Here](https://github.com/KirovAir/TwilightBoxart/releases).

## To-do
* Add support for more consoles. (redump.org as disc source)
* Prefilled config support for different loaders. (RetroArch, Wii etc.)