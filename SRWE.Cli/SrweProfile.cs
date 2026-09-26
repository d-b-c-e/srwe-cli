using System.Globalization;
using System.Xml.Linq;

namespace SrweCli;

internal sealed record SrweProfile(int X, int Y, int Width, int Height, nint? Style, nint? ExStyle)
{
    public static SrweProfile Load(string path)
    {
        var document = XDocument.Load(path);
        if (document.Root?.Name.LocalName != "SRWE")
            throw new InvalidDataException("Expected an SRWE XML profile.");

        var window = document.Descendants("Window")
            .FirstOrDefault(element => (string?)element.Attribute("HierID") == "1")
            ?? document.Descendants("Window").FirstOrDefault()
            ?? throw new InvalidDataException("The SRWE profile contains no Window element.");

        int ReadInt(string name) => int.Parse((string?)window.Attribute(name)
            ?? throw new InvalidDataException($"Missing {name} in SRWE profile."), CultureInfo.InvariantCulture);

        nint? ReadHex(string name)
        {
            var value = (string?)window.Attribute(name);
            return value is null ? null : (nint)uint.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        var profile = new SrweProfile(ReadInt("PosX"), ReadInt("PosY"), ReadInt("Width"), ReadInt("Height"),
            ReadHex("Style"), ReadHex("ExStyle"));
        if (profile.Width <= 0 || profile.Height <= 0)
            throw new InvalidDataException("The SRWE profile has a non-positive window size.");
        return profile;
    }
}
