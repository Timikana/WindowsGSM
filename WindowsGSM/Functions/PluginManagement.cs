using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WindowsGSM.Functions
{
    public class PluginMetadata
    {
        public bool IsLoaded;
        public string GameImage, AuthorImage, FullName, FileName, Error;
        public Plugin Plugin;
        public Type Type;
    }

    class PluginManagement
    {
        public const string DefaultUserImage = "pack://application:,,,/Images/Plugins/User.png";
        public const string DefaultPluginImage = "pack://application:,,,/Images/WindowsGSM.png";

        public PluginManagement()
        {
            Directory.CreateDirectory(ServerPath.GetPlugins());
            SeedDefaultPlugins();
        }

        /// <summary>
        /// Copie les plugins embarques par defaut (dossier "default_plugins" livre a cote de l'exe)
        /// vers plugins\ s'ils n'y sont pas deja. Un marqueur (configs\.default_plugins_seeded) note
        /// ceux deja semes une fois, afin qu'une suppression volontaire par l'utilisateur soit respectee
        /// (on ne re-seme pas un plugin deja seme). Idempotent et sans ecrasement.
        /// </summary>
        public static void SeedDefaultPlugins()
        {
            try
            {
                string defaultDir = Path.Combine(MainWindow.WGSM_PATH, "default_plugins");
                if (!Directory.Exists(defaultDir)) { return; }

                string marker = ServerPath.Get(ServerPath.FolderName.Configs, ".default_plugins_seeded");
                var seeded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (File.Exists(marker))
                {
                    foreach (var line in File.ReadAllLines(marker))
                    {
                        var n = line.Trim();
                        if (n.Length > 0) { seeded.Add(n); }
                    }
                }

                bool changed = false;
                foreach (var srcFolder in Directory.GetDirectories(defaultDir, "*.cs", SearchOption.TopDirectoryOnly))
                {
                    string name = Path.GetFileName(srcFolder);
                    if (seeded.Contains(name)) { continue; }            // deja seme une fois -> respecte une suppression

                    string dst = ServerPath.GetPlugins(name);
                    if (!Directory.Exists(dst))
                    {
                        CopyDirectory(srcFolder, dst);
                        AppLog.Info("Plugins", $"Plugin par defaut seme : {name}");
                    }
                    seeded.Add(name);
                    changed = true;
                }

                if (changed)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(marker));
                    File.WriteAllLines(marker, seeded);
                }
            }
            catch (Exception e)
            {
                AppLog.Warn("Plugins", "SeedDefaultPlugins a echoue : " + e.Message);
            }
        }

        private static void CopyDirectory(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var file in Directory.GetFiles(src))
            {
                File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), overwrite: false);
            }
            foreach (var dir in Directory.GetDirectories(src))
            {
                CopyDirectory(dir, Path.Combine(dst, Path.GetFileName(dir)));
            }
        }

        // P3-6 : les ~150 MetadataReference (TPA framework .NET 10 + WindowsGSM + Newtonsoft) sont IDENTIQUES
        // pour tous les plugins. Avant : reconstruites (lecture métadonnées de ~150 DLL) pour CHAQUE plugin
        // -> ~150×N lectures disque au démarrage. Maintenant : construites UNE fois, partagées (thread-safe).
        private static List<MetadataReference> _sharedReferences;
        private static readonly object _refLock = new object();
        private static List<MetadataReference> GetSharedReferences()
        {
            if (_sharedReferences != null) { return _sharedReferences; }
            lock (_refLock)
            {
                if (_sharedReferences != null) { return _sharedReferences; }
                var references = new List<MetadataReference>();
                var seenRefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                void AddRef(string asmPath)
                {
                    if (!string.IsNullOrEmpty(asmPath) && File.Exists(asmPath) && seenRefs.Add(Path.GetFileName(asmPath)))
                    {
                        try { references.Add(MetadataReference.CreateFromFile(asmPath)); } catch { }
                    }
                }
                var tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? string.Empty;
                foreach (var asmPath in tpa.Split(Path.PathSeparator)) { AddRef(asmPath); }
                AddRef(Assembly.GetEntryAssembly()?.Location);
                AddRef(typeof(Newtonsoft.Json.JsonConvert).Assembly.Location); // Newtonsoft.Json réel (NuGet), plus d'extraction bin/
                _sharedReferences = references;
                return _sharedReferences;
            }
        }

        public async Task<List<PluginMetadata>> LoadPlugins(bool shouldAwait = true)
        {
            var plugins = new List<PluginMetadata>();
            foreach (var pluginFolder in Directory.GetDirectories(ServerPath.GetPlugins(), "*.cs", SearchOption.TopDirectoryOnly).ToList())
            {
                var pluginFile = Path.Combine(pluginFolder, Path.GetFileName(pluginFolder));
                if (File.Exists(pluginFile))
                {
                    var plugin = await LoadPlugin(pluginFile, shouldAwait);
                    if (plugin != null)
                    {
                        plugins.Add(plugin);
                    }
                }
            }
            
            return plugins;
        }

        public async Task<PluginMetadata> LoadPlugin(string path, bool shouldAwait = true)
        {
            var pluginMetadata = new PluginMetadata
            {
                FileName = Path.GetFileName(path)
            };

            // Migration .NET 10 : compilation des plugins via Roslyn (Microsoft.CodeAnalysis)
            // au lieu de CodeDom (CSharpCodeProvider), inexistant hors .NET Framework.
            string source = File.ReadAllText(path);
            var syntaxTree = CSharpSyntaxTree.ParseText(source);

            // P3-6 : références identiques pour tous les plugins -> construites 1× et mises en cache (gros gain démarrage)
            var references = GetSharedReferences();

            var compilation = CSharpCompilation.Create(
                $"Plugin_{Path.GetFileNameWithoutExtension(path)}_{Guid.NewGuid():N}",
                new[] { syntaxTree },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            Assembly compiledAssembly;
            using (var ms = new MemoryStream())
            {
                var emitResult = shouldAwait ? await Task.Run(() => compilation.Emit(ms)) : compilation.Emit(ms);
                if (!emitResult.Success)
                {
                    var sb = new StringBuilder();
                    foreach (var err in emitResult.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
                    {
                        var lineSpan = err.Location.GetLineSpan();
                        sb.Append($"{err.GetMessage()}\nLine: {lineSpan.StartLinePosition.Line + 1} - Column: {lineSpan.StartLinePosition.Character + 1}\n\n");
                    }
                    pluginMetadata.Error = sb.ToString();
                    Console.WriteLine(pluginMetadata.Error);
                    return pluginMetadata;
                }
                ms.Seek(0, SeekOrigin.Begin);
                compiledAssembly = Assembly.Load(ms.ToArray());
            }

            try
            {
                pluginMetadata.Type = compiledAssembly.GetType($"WindowsGSM.Plugins.{Path.GetFileNameWithoutExtension(path)}");
                var plugin = GetPluginClass(pluginMetadata);
                pluginMetadata.FullName = $"{plugin.FullName} [{pluginMetadata.FileName}]";
                pluginMetadata.Plugin = plugin.Plugin;
                try
                {
                    string gameImage = ServerPath.GetPlugins(pluginMetadata.FileName, $"{Path.GetFileNameWithoutExtension(pluginMetadata.FileName)}.png");
                    ImageSource image = new BitmapImage(new Uri(gameImage));
                    pluginMetadata.GameImage = gameImage;
                }
                catch
                {
                    pluginMetadata.GameImage = DefaultPluginImage;
                }
                try
                {
                    string authorImage = ServerPath.GetPlugins(pluginMetadata.FileName, "author.png");
                    ImageSource image = new BitmapImage(new Uri(authorImage));
                    pluginMetadata.AuthorImage = authorImage;
                }
                catch
                {
                    pluginMetadata.AuthorImage = DefaultUserImage;
                }
                pluginMetadata.IsLoaded = true;
            }
            catch (Exception e)
            {
                pluginMetadata.Error = e.Message;
                Console.WriteLine(pluginMetadata.Error);
                pluginMetadata.IsLoaded = false;
            }

            return pluginMetadata;
        }

        public static BitmapSource GetDefaultUserBitmapSource()
        {
            using (var stream = System.Windows.Application.GetResourceStream(new Uri(DefaultUserImage)).Stream)
            {
                return BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            }
        }

        public static BitmapSource GetDefaultPluginBitmapSource()
        {
            using (var stream = System.Windows.Application.GetResourceStream(new Uri(DefaultPluginImage)).Stream)
            {
                return BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            }
        }

        public static dynamic GetPluginClass(PluginMetadata plugin, ServerConfig serverConfig = null) => Activator.CreateInstance(plugin.Type, serverConfig);
    }
}
