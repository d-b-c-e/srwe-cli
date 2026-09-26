using System.Runtime.InteropServices;
using SrweCli;

var handle = CreateWindowEx(0, "STATIC", "srwe-cli smoke window", 0, 20, 20, 200, 120,
    0, 0, 0, 0);
if (handle == 0) throw new InvalidOperationException("Could not create a test window.");

try
{
    var initial = NativeWindow.Inspect(handle);
    if (initial.Client.Width <= 0) throw new Exception("Initial client rectangle is invalid.");

    var target = new PixelRect(-200, 75, 640, 360);
    var applied = NativeWindow.Apply(handle, target, targetClient: true, borderless: true,
        style: null, exStyle: null, exitSizeMove: false);
    if (applied.Client != target)
        throw new Exception($"Requested {target}, received {applied.Client}.");

    var profilePath = Path.Combine(Path.GetTempPath(), $"srwe-cli-test-{Guid.NewGuid():N}.xml");
    try
    {
        File.WriteAllText(profilePath, "<SRWE Version=\"1.0\"><Profile><Window HierID=\"1\" PosX=\"-2562\" PosY=\"-2\" Width=\"7684\" Height=\"1444\" Style=\"14080000\" ExStyle=\"20040A00\" /></Profile></SRWE>");
        var profile = SrweProfile.Load(profilePath);
        if (profile.X != -2562 || profile.Width != 7684 || profile.Style != 0x14080000)
            throw new Exception("SRWE XML profile was parsed incorrectly.");
    }
    finally { File.Delete(profilePath); }

    Console.WriteLine("PASS: Win32 client placement and SRWE profile parsing");
}
finally { _ = DestroyWindow(handle); }

[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
static extern nint CreateWindowEx(uint exStyle, string className, string title, uint style,
    int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);

[DllImport("user32.dll", SetLastError = true)]
static extern bool DestroyWindow(nint handle);
