using System;
using System.Net;

namespace WindowsGSM.Functions
{
    /// <summary>
    /// WebClient avec timeout : le WebClient standard n'expose pas de timeout, donc une requête
    /// vers une API/un dépôt injoignable peut pendre indéfiniment (et bloquer install/MAJ).
    /// </summary>
    public class TimeoutWebClient : WebClient
    {
        private const int TimeoutMs = 30000;

        protected override WebRequest GetWebRequest(Uri address)
        {
            var request = base.GetWebRequest(address);
            if (request != null) { request.Timeout = TimeoutMs; }
            return request;
        }
    }
}
