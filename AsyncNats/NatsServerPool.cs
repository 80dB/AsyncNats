namespace EightyDecibel.AsyncNats
{
    using System;
    using System.Buffers;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Logging;

    internal class NatsServerPool
    {

        public List<DnsEndPoint> Servers => new List<DnsEndPoint>(_servers.Concat(_discoveredServers));

        List<DnsEndPoint> _servers = new List<DnsEndPoint>();
        List<DnsEndPoint> _discoveredServers = new List<DnsEndPoint>();
        Random _random = new Random();
        DnsEndPoint? _lastSelectedServer = null;
        INatsOptions _options;

        object _sync = new object();

        public NatsServerPool(INatsOptions options)
        {

            var servers = options.Servers;

            if(servers == null) throw new ArgumentNullException(nameof(servers));
            if(servers.Count()==0) throw new ArgumentException("server list cannot be empty",nameof(servers));

            _options = options;

            foreach (var server in servers)
                AddServer(_servers,server);
        }

        public DnsEndPoint SelectServer(bool isRetry=false)
        {
            lock(_sync)
            {
                var combinedServers = _servers;
                if(_options.ServersOptions.HasFlag(NatsServerPoolFlags.AllowDiscovery))
                    combinedServers = _servers.Concat(_discoveredServers).ToList();

                var selectedServer = combinedServers[0];

                if (combinedServers.Count == 1)
                {
                    _lastSelectedServer = selectedServer;
                    return selectedServer!;
                }


                if (!isRetry)
                    _lastSelectedServer = null;

                if (_options.ServersOptions.HasFlag(NatsServerPoolFlags.Randomize))
                {
                    //if possible, randomize but also avoid returning the same selection as previous on retry
                    selectedServer = combinedServers
                        .Where(s => s != _lastSelectedServer)
                        .OrderBy(s => _random.Next())
                        .First();
                }
                else if(_lastSelectedServer != null)
                {
                    //return server list in order
                    if (combinedServers.IndexOf(_lastSelectedServer) +1 != combinedServers.Count)
                        selectedServer = combinedServers[combinedServers.IndexOf(_lastSelectedServer) + 1];
                }

                _lastSelectedServer = selectedServer;
                return selectedServer!;
            }            
        }

        public void AddDiscoveredServers(IEnumerable<string> servers)
        {
            if (servers == null || servers.Count()==0) return;

            if (!_options.ServersOptions.HasFlag(NatsServerPoolFlags.AllowDiscovery))
                return;

            lock (_sync)
            {
                _discoveredServers = new List<DnsEndPoint>();

                foreach (var server in servers.OrderBy(s=>Guid.NewGuid())) //randomize discovered
                    AddServer(_discoveredServers, server);
            }
            
        }

        private DnsEndPoint ParseAndNormalizeServerEntry(string server)
        {
            var schemaIndex = server.IndexOf("://");
            if (schemaIndex > 0)
            {
                server = server.Substring(schemaIndex + 3);
            }

            if (!server.Contains(':'))
            {
                server = $"{server}:4222"; //default nats port
            }

            var split = server.Split(':');

            if (split.Length != 2)
                throw new FormatException($"invalid server string {server}");

            var stringHost = split[0];
            var stringPort = split[1];

            if(!int.TryParse(stringPort, out int port))
                throw new FormatException($"invalid server string {server}");

            var checkHostNameResult = Uri.CheckHostName(stringHost);
            if(checkHostNameResult==UriHostNameType.Unknown)
                throw new FormatException($"invalid server string {server}");

            return new DnsEndPoint(stringHost,port);
            
        }

        private void AddServer(List<DnsEndPoint> list, string server)
        {
            lock (_sync)
            {
                var parsed = ParseAndNormalizeServerEntry(server);

                if (!list.Contains(parsed))
                {
                    list.Add(parsed);
                }
            }
        }               

        
    }
}