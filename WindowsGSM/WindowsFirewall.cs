using System;
using System.Threading.Tasks;

namespace WindowsGSM
{
    // Migration .NET 10 : COM tardif (late-bound, dynamic) au lieu de l'interop type
    // NetFwTypeLib. Evite la <COMReference> (non supportee par 'dotnet build' / MSBuild Core)
    // tout en gardant exactement le meme comportement (pare-feu Windows via HNetCfg.FwMgr).
    class WindowsFirewall
    {
        private readonly string Name;
        private readonly string Path;

        public WindowsFirewall(string name, string path)
        {
            Name = name;
            Path = path;
        }

        private static dynamic CreateFwMgr()
            => Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwMgr"));

        public async Task<bool> IsRuleExist()
        {
            return await Task.Run(() =>
            {
                try
                {
                    dynamic netFwMgr = CreateFwMgr();
                    foreach (dynamic app in netFwMgr.LocalPolicy.CurrentProfile.AuthorizedApplications)
                    {
                        if (((string)app.ProcessImageFileName).ToLower() == Path.ToLower())
                        {
                            return true;
                        }
                    }
                }
                catch
                {
                    return false;
                }

                return false;
            });
        }

        public async Task<bool> AddRule()
        {
            return await Task.Run(() =>
            {
                try
                {
                    dynamic netFwMgr = CreateFwMgr();
                    dynamic app = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwAuthorizedApplication"));
                    app.Name = Name;
                    app.ProcessImageFileName = Path;
                    app.Enabled = true;
                    netFwMgr.LocalPolicy.CurrentProfile.AuthorizedApplications.Add(app);
                    return true;
                }
                catch
                {
                    return false;
                }
            });
        }

        public void RemoveRule()
        {
            try
            {
                dynamic netFwMgr = CreateFwMgr();
                netFwMgr.LocalPolicy.CurrentProfile.AuthorizedApplications.Remove(Path);
            }
            catch
            {
                // ignore
            }
        }

        //Remove the firewall rule by similar path
        public async void RemoveRuleEx()
        {
            await Task.Run(() =>
            {
                try
                {
                    dynamic netFwMgr = CreateFwMgr();
                    foreach (dynamic app in netFwMgr.LocalPolicy.CurrentProfile.AuthorizedApplications)
                    {
                        string filename = ((string)app.ProcessImageFileName).ToLower();
                        if (filename.Contains(Path.ToLower()))
                        {
                            netFwMgr.LocalPolicy.CurrentProfile.AuthorizedApplications.Remove(app.ProcessImageFileName);
                        }
                    }
                }
                catch
                {
                    // ignore
                }
            });
        }
    }
}
