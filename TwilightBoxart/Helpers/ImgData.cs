using System;
using System.Collections.Generic;
using System.Text;

namespace TwilightBoxart.Helpers
{
    public static class ImgLib
    {
        public static ImgData DSi = new ImgData
        {
            Name = "Dsi",
            Data = "iVBORw0KGgoAAAANSUhEUgAAAA0AAAANCAYAAABy6+R8AAAACXBIWXMAAAr/AAAK/wE0YpqCAAAF92lUWHRYTUw6Y29tLmFkb2JlLnhtcAAAAAAAPD94cGFja2V0IGJlZ2luPSLvu78iIGlkPSJXNU0wTXBDZWhpSHpyZVN6TlRjemtjOWQiPz4gPHg6eG1wbWV0YSB4bWxuczp4PSJhZG9iZTpuczptZXRhLyIgeDp4bXB0az0iQWRvYmUgWE1QIENvcmUgNS42LWMxNDggNzkuMTY0MDM2LCAyMDE5LzA4LzEzLTAxOjA2OjU3ICAgICAgICAiPiA8cmRmOlJERiB4bWxuczpyZGY9Imh0dHA6Ly93d3cudzMub3JnLzE5OTkvMDIvMjItcmRmLXN5bnRheC1ucyMiPiA8cmRmOkRlc2NyaXB0aW9uIHJkZjphYm91dD0iIiB4bWxuczp4bXA9Imh0dHA6Ly9ucy5hZG9iZS5jb20veGFwLzEuMC8iIHhtbG5zOnhtcE1NPSJodHRwOi8vbnMuYWRvYmUuY29tL3hhcC8xLjAvbW0vIiB4bWxuczpzdEV2dD0iaHR0cDovL25zLmFkb2JlLmNvbS94YXAvMS4wL3NUeXBlL1Jlc291cmNlRXZlbnQjIiB4bWxuczpkYz0iaHR0cDovL3B1cmwub3JnL2RjL2VsZW1lbnRzLzEuMS8iIHhtbG5zOnBob3Rvc2hvcD0iaHR0cDovL25zLmFkb2JlLmNvbS9waG90b3Nob3AvMS4wLyIgeG1wOkNyZWF0b3JUb29sPSJBZG9iZSBQaG90b3Nob3AgMjEuMCAoTWFjaW50b3NoKSIgeG1wOkNyZWF0ZURhdGU9IjIwMjAtMDEtMTRUMjA6MDE6MTArMDE6MDAiIHhtcDpNZXRhZGF0YURhdGU9IjIwMjAtMDEtMTRUMjA6MDE6MTArMDE6MDAiIHhtcDpNb2RpZnlEYXRlPSIyMDIwLTAxLTE0VDIwOjAxOjEwKzAxOjAwIiB4bXBNTTpJbnN0YW5jZUlEPSJ4bXAuaWlkOmNmMWRlODEzLTA3YTgtNDk4Mi1iN2Q5LWU4NTNlNGM5N2E0NiIgeG1wTU06RG9jdW1lbnRJRD0iYWRvYmU6ZG9jaWQ6cGhvdG9zaG9wOmU5NjUyZWYzLTM3ZDItYjM0Yi1iMzQ5LWE1YzdjODdjYzBjNiIgeG1wTU06T3JpZ2luYWxEb2N1bWVudElEPSJ4bXAuZGlkOjE5MWY2OTc1LTA2NGQtNDdhYS04NjZiLTE0NWI0OTMxNmRmMSIgZGM6Zm9ybWF0PSJpbWFnZS9wbmciIHBob3Rvc2hvcDpDb2xvck1vZGU9IjMiIHBob3Rvc2hvcDpJQ0NQcm9maWxlPSJzUkdCIElFQzYxOTY2LTIuMSI+IDx4bXBNTTpIaXN0b3J5PiA8cmRmOlNlcT4gPHJkZjpsaSBzdEV2dDphY3Rpb249ImNyZWF0ZWQiIHN0RXZ0Omluc3RhbmNlSUQ9InhtcC5paWQ6MTkxZjY5NzUtMDY0ZC00N2FhLTg2NmItMTQ1YjQ5MzE2ZGYxIiBzdEV2dDp3aGVuPSIyMDIwLTAxLTE0VDIwOjAxOjEwKzAxOjAwIiBzdEV2dDpzb2Z0d2FyZUFnZW50PSJBZG9iZSBQaG90b3Nob3AgMjEuMCAoTWFjaW50b3NoKSIvPiA8cmRmOmxpIHN0RXZ0OmFjdGlvbj0ic2F2ZWQiIHN0RXZ0Omluc3RhbmNlSUQ9InhtcC5paWQ6Y2YxZGU4MTMtMDdhOC00OTgyLWI3ZDktZTg1M2U0Yzk3YTQ2IiBzdEV2dDp3aGVuPSIyMDIwLTAxLTE0VDIwOjAxOjEwKzAxOjAwIiBzdEV2dDpzb2Z0d2FyZUFnZW50PSJBZG9iZSBQaG90b3Nob3AgMjEuMCAoTWFjaW50b3NoKSIgc3RFdnQ6Y2hhbmdlZD0iLyIvPiA8L3JkZjpTZXE+IDwveG1wTU06SGlzdG9yeT4gPC9yZGY6RGVzY3JpcHRpb24+IDwvcmRmOlJERj4gPC94OnhtcG1ldGE+IDw/eHBhY2tldCBlbmQ9InIiPz5gouNYAAABQklEQVQokXWSPa5aMRBGDzwKU7q7LqegcHm9A1eR7jJYSVbCMiihTWWXVxGFq4fpbOUhgRKhlyZ2QFGmsj7N8Xzzszifz58A6/Wa/X5PSokQAi2cc4gI0zRxu90AWDRot9uRc8YYg7W2Q/M8d3273f6FGjCOIyICwOVyAWAYBlJKxBg7+Oac+zrP8wsAsNlsMMZwvV7RWqOU4nQ68Xg8WKaUAF4Aay0i8qK1d0qJVQiBaZqotbbkX4AFfgI/rLW1WTXGEEJg+ez/fr8DrIAP4B0YtNa9QoslT1FKoZSyAAT4Anz+0Z7TWDrnyDn3idVaKaV8A/bA91prd5JzxjnHSkTIOVNKQWtNjJFhGPrEYowopXo1EWHlve97GMcRrTUppf57A9qevPf/XkSbkoiglOJwOHTt5SJag8fj8b+3573v2m+yRsCKKHTLMgAAAABJRU5ErkJggg==",
            BorderHeight = 4,
            BorderWidth = 1,
            CornerHeight = 6,
            CornerWidth = 6,
            Coords = new List<ImgCoords>
            {
                new ImgCoords
                {
                    BorderX = -7,
                    BorderY = 0,
                    CornerX = 0,
                    CornerY = 0
                },
                new ImgCoords
                {
                    BorderX = 0,
                    BorderY = -7,
                    CornerX = -7,
                    CornerY = 0
                },
                new ImgCoords
                {
                    BorderX = -7,
                    BorderY = -9,
                    CornerX = -7,
                    CornerY = -7
                },
                new ImgCoords
                {
                    BorderX = -9,
                    BorderY = -7,
                    CornerX = 0,
                    CornerY = -7
                }
            }
        };
    }

    public class ImgData
    {
        public string Name { get; set; }
        public string Data { get; set; }
        public int CornerWidth { get; set; }
        public int CornerHeight { get; set; }
        public int BorderWidth { get; set; }
        public int BorderHeight { get; set; }
        public List<ImgCoords> Coords { get; set; }
    }

    public class ImgCoords
    {

        public int CornerX { get; set; }
        public int CornerY { get; set; }
        public int BorderX { get; set; }
        public int BorderY { get; set; }
    }
}
