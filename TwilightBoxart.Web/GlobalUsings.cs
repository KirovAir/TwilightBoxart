// The shared contracts are used by nearly every file in this project; the endpoints, the art
// pipeline and the cache all speak in ConsoleType / RomIdentity / RenderOptions.
global using TwilightBoxart.Core.Models;

// Same reason, and the same idiom as TwilightBoxart.Data's own GlobalUsings: the stores and the
// cache index all take an IDbContextFactory and query through EF's extension methods.
global using Microsoft.EntityFrameworkCore;
