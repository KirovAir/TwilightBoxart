using System.Collections.Generic;

namespace TwilightBoxart.Helpers
{
    public static class ImgLib
    {
        public static ImgData DSi = new ImgData
        {
            Name = "Dsi",
            Data = "iVBORw0KGgoAAAANSUhEUgAAAA0AAAANCAYAAABy6+R8AAAACXBIWXMAAAr/AAAK/wE0YpqCAAAF92lUWHRYTUw6Y29tLmFkb2JlLnhtcAAAAAAAPD94cGFja2V0IGJlZ2luPSLvu78iIGlkPSJXNU0wTXBDZWhpSHpyZVN6TlRjemtjOWQiPz4gPHg6eG1wbWV0YSB4bWxuczp4PSJhZG9iZTpuczptZXRhLyIgeDp4bXB0az0iQWRvYmUgWE1QIENvcmUgNS42LWMxNDggNzkuMTY0MDM2LCAyMDE5LzA4LzEzLTAxOjA2OjU3ICAgICAgICAiPiA8cmRmOlJERiB4bWxuczpyZGY9Imh0dHA6Ly93d3cudzMub3JnLzE5OTkvMDIvMjItcmRmLXN5bnRheC1ucyMiPiA8cmRmOkRlc2NyaXB0aW9uIHJkZjphYm91dD0iIiB4bWxuczp4bXA9Imh0dHA6Ly9ucy5hZG9iZS5jb20veGFwLzEuMC8iIHhtbG5zOnhtcE1NPSJodHRwOi8vbnMuYWRvYmUuY29tL3hhcC8xLjAvbW0vIiB4bWxuczpzdEV2dD0iaHR0cDovL25zLmFkb2JlLmNvbS94YXAvMS4wL3NUeXBlL1Jlc291cmNlRXZlbnQjIiB4bWxuczpkYz0iaHR0cDovL3B1cmwub3JnL2RjL2VsZW1lbnRzLzEuMS8iIHhtbG5zOnBob3Rvc2hvcD0iaHR0cDovL25zLmFkb2JlLmNvbS9waG90b3Nob3AvMS4wLyIgeG1wOkNyZWF0b3JUb29sPSJBZG9iZSBQaG90b3Nob3AgMjEuMCAoTWFjaW50b3NoKSIgeG1wOkNyZWF0ZURhdGU9IjIwMjAtMDEtMTVUMTA6NTQ6MTgrMDE6MDAiIHhtcDpNZXRhZGF0YURhdGU9IjIwMjAtMDEtMTVUMTA6NTQ6MTgrMDE6MDAiIHhtcDpNb2RpZnlEYXRlPSIyMDIwLTAxLTE1VDEwOjU0OjE4KzAxOjAwIiB4bXBNTTpJbnN0YW5jZUlEPSJ4bXAuaWlkOmM5Y2E4ZDg1LWFjM2UtNDRmNC1iZjhjLWQ5N2ZhZDkzOGRlMiIgeG1wTU06RG9jdW1lbnRJRD0iYWRvYmU6ZG9jaWQ6cGhvdG9zaG9wOjYwOTZjMGFlLWQwMDYtYTA0NC1hYjg0LWNjOTY2ZjNmYTE4MCIgeG1wTU06T3JpZ2luYWxEb2N1bWVudElEPSJ4bXAuZGlkOjM0MTY5MzM1LTg0YWItNDBlMi1iYjgxLTczOWQ2NjJkMzdjZSIgZGM6Zm9ybWF0PSJpbWFnZS9wbmciIHBob3Rvc2hvcDpDb2xvck1vZGU9IjMiIHBob3Rvc2hvcDpJQ0NQcm9maWxlPSJzUkdCIElFQzYxOTY2LTIuMSI+IDx4bXBNTTpIaXN0b3J5PiA8cmRmOlNlcT4gPHJkZjpsaSBzdEV2dDphY3Rpb249ImNyZWF0ZWQiIHN0RXZ0Omluc3RhbmNlSUQ9InhtcC5paWQ6MzQxNjkzMzUtODRhYi00MGUyLWJiODEtNzM5ZDY2MmQzN2NlIiBzdEV2dDp3aGVuPSIyMDIwLTAxLTE1VDEwOjU0OjE4KzAxOjAwIiBzdEV2dDpzb2Z0d2FyZUFnZW50PSJBZG9iZSBQaG90b3Nob3AgMjEuMCAoTWFjaW50b3NoKSIvPiA8cmRmOmxpIHN0RXZ0OmFjdGlvbj0ic2F2ZWQiIHN0RXZ0Omluc3RhbmNlSUQ9InhtcC5paWQ6YzljYThkODUtYWMzZS00NGY0LWJmOGMtZDk3ZmFkOTM4ZGUyIiBzdEV2dDp3aGVuPSIyMDIwLTAxLTE1VDEwOjU0OjE4KzAxOjAwIiBzdEV2dDpzb2Z0d2FyZUFnZW50PSJBZG9iZSBQaG90b3Nob3AgMjEuMCAoTWFjaW50b3NoKSIgc3RFdnQ6Y2hhbmdlZD0iLyIvPiA8L3JkZjpTZXE+IDwveG1wTU06SGlzdG9yeT4gPC9yZGY6RGVzY3JpcHRpb24+IDwvcmRmOlJERj4gPC94OnhtcG1ldGE+IDw/eHBhY2tldCBlbmQ9InIiPz7V0wNbAAAA/ElEQVQokX2SMW7CQBBFVwlKh9KlAymKFCoKmnCGdMgcw9fwMdy69BF8BZc+glsXSFgCKct/K8YajJTiIfbP/NmZWYe+71/FzzAMsaqqWBRFzLJsgjO64iflvYu3oJ+tOBPM8zwl1XU94XXlwRrTFCjLMjZNk6A68B/dGwOBuQG6rkvY2YzkB7vFG6g2jmPC63ZbYFiq0D+VlXgRX2IlGDy2bZuwJQXbECYCSvwTH+JFbLgN3ZbyZHKt7cWv+OaM/mCymRAZkhb9TJzRiU8z+e2Zkcq05A0P25u/kyXYO3nD/Z0umJbiakYL+pZmX8QO06c4IPz37d0NR7G4Af9+7Gp6J51rAAAAAElFTkSuQmCC",
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

        public static ImgData N3DS = new ImgData
        {
            Name = "3ds",
            Data = "iVBORw0KGgoAAAANSUhEUgAAABkAAAAZCAYAAADE6YVjAAAACXBIWXMAAAsTAAALEwEAmpwYAAAGbWlUWHRYTUw6Y29tLmFkb2JlLnhtcAAAAAAAPD94cGFja2V0IGJlZ2luPSLvu78iIGlkPSJXNU0wTXBDZWhpSHpyZVN6TlRjemtjOWQiPz4gPHg6eG1wbWV0YSB4bWxuczp4PSJhZG9iZTpuczptZXRhLyIgeDp4bXB0az0iQWRvYmUgWE1QIENvcmUgNS42LWMxNDggNzkuMTY0MDM2LCAyMDE5LzA4LzEzLTAxOjA2OjU3ICAgICAgICAiPiA8cmRmOlJERiB4bWxuczpyZGY9Imh0dHA6Ly93d3cudzMub3JnLzE5OTkvMDIvMjItcmRmLXN5bnRheC1ucyMiPiA8cmRmOkRlc2NyaXB0aW9uIHJkZjphYm91dD0iIiB4bWxuczp4bXA9Imh0dHA6Ly9ucy5hZG9iZS5jb20veGFwLzEuMC8iIHhtbG5zOnhtcE1NPSJodHRwOi8vbnMuYWRvYmUuY29tL3hhcC8xLjAvbW0vIiB4bWxuczpzdEV2dD0iaHR0cDovL25zLmFkb2JlLmNvbS94YXAvMS4wL3NUeXBlL1Jlc291cmNlRXZlbnQjIiB4bWxuczpkYz0iaHR0cDovL3B1cmwub3JnL2RjL2VsZW1lbnRzLzEuMS8iIHhtbG5zOnBob3Rvc2hvcD0iaHR0cDovL25zLmFkb2JlLmNvbS9waG90b3Nob3AvMS4wLyIgeG1wOkNyZWF0b3JUb29sPSJBZG9iZSBQaG90b3Nob3AgMjEuMCAoTWFjaW50b3NoKSIgeG1wOkNyZWF0ZURhdGU9IjIwMjAtMDEtMTVUMTI6Mzc6NDArMDE6MDAiIHhtcDpNb2RpZnlEYXRlPSIyMDIwLTAxLTE1VDEyOjM5OjM2KzAxOjAwIiB4bXA6TWV0YWRhdGFEYXRlPSIyMDIwLTAxLTE1VDEyOjM5OjM2KzAxOjAwIiB4bXBNTTpEb2N1bWVudElEPSJhZG9iZTpkb2NpZDpwaG90b3Nob3A6NDY1ZGJkZWUtMmUzMS1mYTQ1LTlkZTktZmE1MTY0MmZiOTBhIiB4bXBNTTpJbnN0YW5jZUlEPSJ4bXAuaWlkOjJhOTI0NjZhLWVlNzctNDY4MC04NGYyLTVmOGJlMjRkMTE1MiIgeG1wTU06T3JpZ2luYWxEb2N1bWVudElEPSJhZG9iZTpkb2NpZDpwaG90b3Nob3A6ZGJiMjRhYmItMTEzOS1kNDQxLTk5YzAtODc5MmU0MDJlMzViIiBkYzpmb3JtYXQ9ImltYWdlL3BuZyIgcGhvdG9zaG9wOkNvbG9yTW9kZT0iMyIgcGhvdG9zaG9wOklDQ1Byb2ZpbGU9InNSR0IgSUVDNjE5NjYtMi4xIj4gPHhtcE1NOkhpc3Rvcnk+IDxyZGY6U2VxPiA8cmRmOmxpIHN0RXZ0OmFjdGlvbj0iY3JlYXRlZCIgc3RFdnQ6aW5zdGFuY2VJRD0ieG1wLmlpZDo4N2VkZmQ1ZS1kZjM2LTQxMDctOGJiNS05NDAyZWI5MDkzOWEiIHN0RXZ0OndoZW49IjIwMjAtMDEtMTVUMTI6Mzc6NDArMDE6MDAiIHN0RXZ0OnNvZnR3YXJlQWdlbnQ9IkFkb2JlIFBob3Rvc2hvcCAyMS4wIChNYWNpbnRvc2gpIi8+IDxyZGY6bGkgc3RFdnQ6YWN0aW9uPSJjb252ZXJ0ZWQiIHN0RXZ0OnBhcmFtZXRlcnM9ImZyb20gYXBwbGljYXRpb24vdm5kLmFkb2JlLnBob3Rvc2hvcCB0byBpbWFnZS9wbmciLz4gPHJkZjpsaSBzdEV2dDphY3Rpb249InNhdmVkIiBzdEV2dDppbnN0YW5jZUlEPSJ4bXAuaWlkOjJhOTI0NjZhLWVlNzctNDY4MC04NGYyLTVmOGJlMjRkMTE1MiIgc3RFdnQ6d2hlbj0iMjAyMC0wMS0xNVQxMjozOTozNiswMTowMCIgc3RFdnQ6c29mdHdhcmVBZ2VudD0iQWRvYmUgUGhvdG9zaG9wIDIxLjAgKE1hY2ludG9zaCkiIHN0RXZ0OmNoYW5nZWQ9Ii8iLz4gPC9yZGY6U2VxPiA8L3htcE1NOkhpc3Rvcnk+IDwvcmRmOkRlc2NyaXB0aW9uPiA8L3JkZjpSREY+IDwveDp4bXBtZXRhPiA8P3hwYWNrZXQgZW5kPSJyIj8+NiH23AAABD1JREFUSA0FwWtvWwcZAODn2MfOe5zLfJK2ideqYNBQO61CakfHxBAft5/AZ34kEtIk+DRNXD6B0rHSmN5w1rXxSXPxGzv24XmKo6PD3wCgwif4FX6OXWkPFSoAAMBaaPAM/8C3+Bde4j2WJaBADw/wpfQQWylXEXEV4rSq66e4RgvozOezW5lZZWY/xC8wFLaxh2/xPZoSUOJjfIVHyS5ZDIcHz+u6folTnCOxQoFexGiCrdlsNm6a44+IbvAYW1jjHJclevgIX0mfZWbgajwefx/DYZOZz/EWp5hjCQhsYXc0Gi3rqno3mUweZBpFxEBY4gTvStzA76TPM3MnIs5G4/G/I6LJzGd4jTdocIklOuhjG3uZeRLD4b3x/ftn08nkUWbuh3gkvMOrEg+lL5IPIuJiNBq9xnFmvsB/8Qpv8B5zrFCghwFmOM/MJX46Go22p9PpvWQ/0hfC6xIPsCezNBzORRzjB7zEC7zGCS6wwAoFSmxgjgUK9EUMRNzWNHdE3MLjEqPMDCzqun6DGX7EMX7AW7zHFdZoAQsssAL0sYm6ruvn06bZzcydiBiXUuAaIuIEp5jhBA3OcYUVWgC0WOISXZzgBCcR8QZrrKWqk7KLFm1EdHCJM5zhEgus0AIAoMU1rnCJM1xERBct1ikXHTRYYgPXWOAKSyyxAgAAALRYYYEFrrDEJlpcdaQlVgBYA9ZoAQAAAAAtWqyxRgsglR300MUluuihix5KdAAAAAAFOijRQx89nKGLzQ620UM3M1eosIkKGyhRAAAACnTQR2CAKjMX6KLETkfEFsqkm5k1djBEjR0M0EMHBQoUKNBFYBND1PggM/eTLroiBp3gFVZBfz6bHWCIm9jHTdTYRqCHLkr0UWEHe7iJW7gxn83uBgMsgsNS+Lv0MaJpmp16NBpiHxdYAHp4jytco0APAwxxgDsY4UbTNPuAhfDXUjqLiKPM/CTn8/50Ork/Go0XWAL62MQpLrFEgQ1sYQ8HuI3b0+nk05zPI6rqOiIaqVsKXwNuhupOM51tZXowHo/biKgzcxNDnOISSxTYwA7qiPhZZg4mk8lnOZvtRlV1hJUww9MSib+FeJhyGFW1k5mDJ0+ePB4fHLwcjkabeJ+ZU8xxDehHxD52mun0w8nx5B7VdVRVB22IS/yIpjg6OvwUgVvSHzLzc9RJYT4HB+Px07qu/xcRXawyM1HOZrPbx5PJPVBVAmgj4kJ4gq/xx+Lo6PCX6KOLXen3mflb1NhK8wJtqJZYA6CT5n10QgWwioiZ8B3+gq9xWOIMPXRxjj/HML6Rvkz5k8jqLvroJgVgHaxDNUcrrEO0wgvpLf6Eb3CI0+Lo6PAGOuigQAeBD6Vf4y5updxGRyIkViE62EBXOMZ3+CeeYYqL8fj+ssQCAC1anOKd8B8MsRtigALXQuIaBQIlznGMEyyxxBr+D1SF12WEFqNAAAAAAElFTkSuQmCC",
            BorderHeight = 10,
            BorderWidth = 1,
            CornerHeight = 12,
            CornerWidth = 12,
            Coords = new List<ImgCoords>
            {
                new ImgCoords
                {
                    BorderX = -13,
                    BorderY = 0,
                    CornerX = 0,
                    CornerY = 0
                },
                new ImgCoords
                {
                    BorderX = 0,
                    BorderY = -13,
                    CornerX = -13,
                    CornerY = 0
                },
                new ImgCoords
                {
                    BorderX = -13,
                    BorderY = -15,
                    CornerX = -13,
                    CornerY = -13
                },
                new ImgCoords
                {
                    BorderX = -15,
                    BorderY = -13,
                    CornerX = 0,
                    CornerY = -13
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
