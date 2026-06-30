using System;
using System.Net;

namespace WindowsGSM.Functions
{
    /// <summary>
    /// WebClient with timeout: the standard WebClient does not expose a timeout, so a request
    /// to an unreachable API/repository can hang indefinitely (and block install/update).
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
