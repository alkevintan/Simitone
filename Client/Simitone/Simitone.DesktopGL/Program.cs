using FSO.Client;
using FSO.Common;
using Simitone.Windows.GameLocator;
using StbImageSharp;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace Simitone.DesktopGL
{
    /// <summary>
    /// OpenGL entry point for Simitone. Mirrors Simitone.Windows/Program.cs without the
    /// pieces that pin it to Windows: WinForms dialogs, the DirectX renderer, the
    /// MonogameLinker assembly shuffle and the registry-based game locator.
    /// </summary>
    public static class Program
    {
        static void Main(string[] args)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            Directory.SetCurrentDirectory(baseDir);
            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;

            ILocator gameLocator = Directory.Exists("/Users") ? new MacOSLocator() : new LinuxLocator();
            var path = gameLocator.FindTheSims1();

            FSOEnvironment.Enable3D = false;
            bool aa = false;
            bool jit = false;

            FSOEnvironment.Args = string.Join(" ", args);

            foreach (var arg in args)
            {
                if (arg.Length == 0 || arg[0] != '-') continue;
                var cmd = arg.Substring(1);
                if (cmd.StartsWith("lang"))
                {
                    GlobalSettings.Default.LanguageCode = byte.Parse(cmd.Substring(4));
                }
                else if (cmd.StartsWith("hz"))
                {
                    GlobalSettings.Default.TargetRefreshRate = int.Parse(cmd.Substring(2));
                }
                else
                {
                    switch (cmd)
                    {
                        case "3d":
                            FSOEnvironment.Enable3D = true;
                            break;
                        case "aa":
                            aa = true;
                            break;
                        case "jit":
                            jit = true;
                            break;
                        case "touch":
                            FSOEnvironment.SoftwareKeyboard = true;
                            break;
                        case "nosound":
                            FSOEnvironment.NoSound = true;
                            break;
                        case string s when s.StartsWith("path"): //The Sims path
                            path = s.Length > 4 ? s.Substring(4).Trim('"').Replace('\\', '/') + "/" : path;
                            break;
                    }
                }
            }

            FSO.Files.ImageLoaderHelpers.BitmapFunction = BitmapReader;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            FSOEnvironment.SoftwareDepth = false;
            FSOEnvironment.UseMRT = true;

            //The locators always hand back a path, so check it actually points at an install
            //rather than letting content loading fail somewhere less obvious.
            if (path == null || !File.Exists(Path.Combine(path, "GameData", "Behavior.iff")))
            {
                var looked = path == null ? "(none)" : Path.GetFullPath(path);
                Console.Error.WriteLine($"No The Sims 1 installation found at \"{looked}\".");
                Console.Error.WriteLine("Expected GameData/Behavior.iff inside it.");
                Console.Error.WriteLine("Point at one with: -path\"/path/to/The Sims\"");
                return;
            }

            FSOEnvironment.ContentDir = "Content/";
            FSOEnvironment.GFXContentDir = "Content/OGL/";
            FSOEnvironment.UserDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Simitone/").Replace('\\', '/');
            Directory.CreateDirectory(FSOEnvironment.UserDir);
            FSOEnvironment.Linux = true; //non-Windows; gates the Windows-only updater paths
            FSOEnvironment.DirectX = false;
            FSOEnvironment.GameThread = Thread.CurrentThread;
            if (GlobalSettings.Default.LanguageCode == 0) GlobalSettings.Default.LanguageCode = 1;
            FSO.Files.Formats.IFF.Chunks.STR.DefaultLangCode = (FSO.Files.Formats.IFF.Chunks.STRLangCode)GlobalSettings.Default.LanguageCode;

            GlobalSettings.Default.StartupPath = path;
            GlobalSettings.Default.TS1HybridEnable = true;
            GlobalSettings.Default.TS1HybridPath = path;
            GlobalSettings.Default.ClientVersion = "0";
            GlobalSettings.Default.LightingMode = 3;
            GlobalSettings.Default.AntiAlias = aa ? 1 : 0;
            GlobalSettings.Default.ComplexShaders = true;
            GlobalSettings.Default.EnableTransitions = true;

            var assemblies = new FSO.SimAntics.JIT.Runtime.AssemblyStore();
            if (jit) assemblies.InitAOT();
            FSO.SimAntics.Engine.VMTranslator.INSTANCE = new FSO.SimAntics.JIT.Runtime.VMAOTTranslator(assemblies);

            var start = new GameStartProxy();
            start.Start();
        }

        private static Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            //The SimAntics JIT emits its output under this name and expects to find it loaded.
            if (args.Name.StartsWith("FSO.Scripts"))
                return AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(x => x.FullName == args.Name);
            return null;
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Console.Error.WriteLine(e.ExceptionObject is OutOfMemoryException
                ? "Out of memory! Simitone needs to close."
                : "A fatal error occurred:");
            Console.Error.WriteLine(e.ExceptionObject.ToString());
        }

        /// <summary>
        /// Decodes an image into the RGBA byte order MonoGame's SurfaceFormat.Color expects.
        /// The Windows head lands on the same order the long way round — it swaps R and B via
        /// a colour matrix, then reads the bitmap back as Format32bppArgb, which is BGRA in
        /// memory, so the two swaps cancel. StbImageSharp just gives us RGBA directly.
        /// </summary>
        public static Tuple<byte[], int, int> BitmapReader(Stream str)
        {
            var image = ImageResult.FromStream(str, ColorComponents.RedGreenBlueAlpha);
            if (image == null) return null;
            return new Tuple<byte[], int, int>(image.Data, image.Width, image.Height);
        }
    }
}
