using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace SrweCli;

internal static class Program
{
    private const string Usage = """
        srwe-cli — headless runtime window positioning for Windows

        Commands:
          srwe-cli list [--json]
          srwe-cli inspect (--pid N | --name EXE | --hwnd HANDLE) [--wait SECONDS] [--json]
          srwe-cli apply (--pid N | --name EXE | --hwnd HANDLE)
              (--profile FILE | --x N --y N --width N --height N)
              [--borderless] [--client-area] [--exit-size-move] [--wait SECONDS] [--json]

        --client-area interprets x/y/width/height as the exact client rectangle.
        An SRWE XML profile uses its saved outer rectangle and window styles.
        --wait polls for a game window; it does not launch the game.
        Exit codes: 0 success, 2 invalid input, 3 target not found, 4 window operation failed.
        """;

    private static int Main(string[] args)
    {
        NativeWindow.EnablePerMonitorDpiAwareness();
        if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
        {
            Console.WriteLine(Usage);
            return 0;
        }

        try
        {
            var options = Options.Parse(args);
            if (options.Command == "list")
            {
                var windows = ListWindows();
                if (options.Json) Console.WriteLine(JsonSerializer.Serialize(windows));
                else foreach (var window in windows)
                    Console.WriteLine($"{window.ProcessId,7}  {window.Name,-26}  {window.Handle}  {window.Title}");
                return 0;
            }

            var handle = FindWindow(options);
            if (handle == 0)
            {
                Console.Error.WriteLine("No window matched before the timeout. Is the game running in windowed mode?");
                return 3;
            }

            WindowState state;
            if (options.Command == "inspect") state = NativeWindow.Inspect(handle);
            else
            {
                var profile = options.Profile is null ? null : SrweProfile.Load(options.Profile);
                var target = profile is null
                    ? new PixelRect(options.X!.Value, options.Y!.Value, options.Width!.Value, options.Height!.Value)
                    : new PixelRect(profile.X, profile.Y, profile.Width, profile.Height);
                state = NativeWindow.Apply(handle, target, options.ClientArea, options.Borderless,
                    profile?.Style, profile?.ExStyle, options.ExitSizeMove);
            }

            if (options.Json) Console.WriteLine(JsonSerializer.Serialize(new
            {
                handle = $"0x{state.Handle:X}", state.ProcessId, state.Title,
                window = state.Window, client = state.Client,
                style = $"0x{state.Style:X8}", exStyle = $"0x{state.ExStyle:X8}"
            }));
            else
            {
                Console.WriteLine($"PID {state.ProcessId}, HWND 0x{state.Handle:X}: {state.Title}");
                Console.WriteLine($"Window: {Format(state.Window)}");
                Console.WriteLine($"Client: {Format(state.Client)}");
                Console.WriteLine($"Style: 0x{state.Style:X}, ExStyle: 0x{state.ExStyle:X}");
            }
            return 0;
        }
        catch (ArgumentException error)
        {
            Console.Error.WriteLine(error.Message);
            Console.Error.WriteLine("Run srwe-cli help for usage.");
            return 2;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"{error.GetType().Name}: {error.Message}");
            return 4;
        }
    }

    private static string Format(PixelRect rect) => $"{rect.X},{rect.Y} {rect.Width}x{rect.Height}";

    private static nint FindWindow(Options options)
    {
        var timer = Stopwatch.StartNew();
        do
        {
            if (options.Handle is { } explicitHandle)
            {
                if (NativeWindow.Exists(explicitHandle)) return explicitHandle;
            }
            else
            {
                Process[] processes;
                try
                {
                    processes = options.Pid is { } pid
                        ? [Process.GetProcessById(pid)]
                        : Process.GetProcessesByName(Path.GetFileNameWithoutExtension(options.Name!));
                }
                catch (ArgumentException) { processes = []; }

                foreach (var process in processes)
                {
                    using (process)
                    {
                        try
                        {
                            process.Refresh();
                            if (NativeWindow.Exists(process.MainWindowHandle)) return process.MainWindowHandle;
                        }
                        catch (InvalidOperationException) { }
                    }
                }
            }

            if (timer.Elapsed >= TimeSpan.FromSeconds(options.WaitSeconds)) break;
            Thread.Sleep(200);
        } while (true);
        return 0;
    }

    private static List<ProcessWindow> ListWindows()
    {
        var windows = new List<ProcessWindow>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    var handle = process.MainWindowHandle;
                    if (NativeWindow.Exists(handle))
                        windows.Add(new ProcessWindow(process.Id, process.ProcessName, $"0x{handle:X}",
                            NativeWindow.Inspect(handle).Title));
                }
                catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            }
        }
        return windows.OrderBy(window => window.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private sealed record ProcessWindow(int ProcessId, string Name, string Handle, string Title);

    private sealed class Options
    {
        public required string Command { get; init; }
        public int? Pid { get; init; }
        public string? Name { get; init; }
        public nint? Handle { get; init; }
        public int WaitSeconds { get; init; }
        public string? Profile { get; init; }
        public int? X { get; init; }
        public int? Y { get; init; }
        public int? Width { get; init; }
        public int? Height { get; init; }
        public bool Borderless { get; init; }
        public bool ClientArea { get; init; }
        public bool ExitSizeMove { get; init; }
        public bool Json { get; init; }

        public static Options Parse(string[] args)
        {
            var command = args[0];
            if (command is not ("list" or "inspect" or "apply"))
                throw new ArgumentException($"Unknown command: {command}");

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var valueNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "--pid", "--name", "--hwnd", "--wait", "--profile", "--x", "--y", "--width", "--height" };
            var flagNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "--borderless", "--client-area", "--exit-size-move", "--json" };
            for (var i = 1; i < args.Length; i++)
            {
                var key = args[i];
                if (valueNames.Contains(key))
                {
                    if (++i >= args.Length || !values.TryAdd(key, args[i]))
                        throw new ArgumentException($"Missing or duplicate value for {key}.");
                }
                else if (flagNames.Contains(key))
                {
                    if (!flags.Add(key)) throw new ArgumentException($"Duplicate option: {key}");
                }
                else throw new ArgumentException($"Unknown option: {key}");
            }

            int? IntValue(string key)
            {
                if (!values.TryGetValue(key, out var raw)) return null;
                if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
                    throw new ArgumentException($"{key} requires an integer.");
                return number;
            }

            nint? Hwnd()
            {
                if (!values.TryGetValue("--hwnd", out var raw)) return null;
                var hex = raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
                if (!long.TryParse(hex ? raw[2..] : raw, hex ? NumberStyles.HexNumber : NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var number) || number <= 0)
                    throw new ArgumentException("--hwnd requires a positive decimal or 0x-prefixed hexadecimal handle.");
                return (nint)number;
            }

            var pid = IntValue("--pid");
            var name = values.GetValueOrDefault("--name");
            var hwnd = Hwnd();
            var wait = IntValue("--wait") ?? 0;
            var profile = values.GetValueOrDefault("--profile");
            var x = IntValue("--x");
            var y = IntValue("--y");
            var width = IntValue("--width");
            var height = IntValue("--height");
            var borderless = flags.Contains("--borderless");
            var clientArea = flags.Contains("--client-area");
            var exitSizeMove = flags.Contains("--exit-size-move");

            if (wait is < 0 or > 600) throw new ArgumentException("--wait must be between 0 and 600 seconds.");
            if (command == "list")
            {
                if (values.Count != 0 || flags.Any(flag => flag != "--json"))
                    throw new ArgumentException("list accepts only --json.");
            }
            else
            {
                if ((pid.HasValue ? 1 : 0) + (name is not null ? 1 : 0) + (hwnd.HasValue ? 1 : 0) != 1)
                    throw new ArgumentException("Specify exactly one of --pid, --name, or --hwnd.");
                if (pid is <= 0 || name is "")
                    throw new ArgumentException("The target must be a positive PID or non-empty process name.");
            }

            if (command == "inspect")
            {
                if (profile is not null || x.HasValue || y.HasValue || width.HasValue || height.HasValue ||
                    borderless || clientArea || exitSizeMove)
                    throw new ArgumentException("inspect cannot change window settings.");
            }
            else if (command == "apply")
            {
                if (profile is not null)
                {
                    if (x.HasValue || y.HasValue || width.HasValue || height.HasValue || clientArea)
                        throw new ArgumentException("--profile cannot be combined with geometry or --client-area.");
                }
                else if (!x.HasValue || !y.HasValue || width is null or <= 0 || height is null or <= 0)
                    throw new ArgumentException("apply needs --profile or all of --x, --y, --width, --height.");
            }

            return new Options
            {
                Command = command, Pid = pid, Name = name, Handle = hwnd, WaitSeconds = wait,
                Profile = profile, X = x, Y = y, Width = width, Height = height,
                Borderless = borderless, ClientArea = clientArea, ExitSizeMove = exitSizeMove,
                Json = flags.Contains("--json")
            };
        }
    }
}
