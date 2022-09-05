using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

namespace EightyDecibel.AsyncNats
{
    public interface INatsServerPool
    {
        IList<DnsEndPoint> Servers { get; }
        ValueTask<IPEndPoint> SelectServer(bool isRetry = false);

        void SetDiscoveredServers(IEnumerable<string> servers);

    }
}