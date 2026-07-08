using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using WindowsGSM.Functions;

namespace WindowsGSM
{
    public class Program
    {
        [STAThread]
        public static void Main()
        {
            // MahApps removed (WPF-UI migration): no more extraction of MahApps.Metro.dll.

            string roslynBase = Path.Combine(Path.GetDirectoryName(Process.GetCurrentProcess().MainModule.FileName), ServerPath.FolderName.Bin);
            Directory.CreateDirectory(roslynBase);
            if (!Directory.Exists(Path.Combine(roslynBase, "roslyn")))
            {
                string roslynZipPath = Path.Combine(roslynBase, "roslyn.zip");
                if (!File.Exists(roslynZipPath) || new FileInfo(roslynZipPath).Length != 7529158) // Latest roslyn.zip byte size is 7529158
                {
                    File.WriteAllBytes(roslynZipPath, Properties.Resources.roslyn);
                    ZipFile.ExtractToDirectory(roslynZipPath, roslynBase);
                    File.Delete(roslynZipPath);
                }
            }

            // Newtonsoft.Json now comes from NuGet (PackageReference): no more extraction from an embedded resource.
            // The plugins' Roslyn compiler references the loaded assembly directly (see PluginManagement.GetSharedReferences).

            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                var resourceName = Assembly.GetExecutingAssembly().GetName().Name + ".ReferencesEx." + new AssemblyName(args.Name).Name + ".dll";
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
                {
                    if (stream != null)
                    {
                        var assemblyData = new byte[stream.Length];
                        stream.Read(assemblyData, 0, assemblyData.Length);
                        return Assembly.Load(assemblyData);
                    }
                }

                return null;
            };

            App.Main();
        }
    }
}
