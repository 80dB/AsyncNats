namespace EightyDecibel.AsyncNats
{
    using System;
    using System.Buffers;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Logging;

    internal class NatsServerPool:INatsServerPool
    {
        public IList<DnsEndPoint> Servers
        {
            get
            {
                if (_options.ServersOptions.HasFlag(NatsServerPoolFlags.AllowDiscovery))
                    return new List<DnsEndPoint>(_servers.Concat(_discoveredServers));
                else
                    return new List<DnsEndPoint>(_servers);
            }
        }

        List<DnsEndPoint> _servers = new List<DnsEndPoint>();
        List<DnsEndPoint> _discoveredServers = new List<DnsEndPoint>();
        Random _random = new Random();
        DnsEndPoint? _lastSelectedDnsEndPoint = null;
        Queue<IPEndPoint> _retryIPEndPointQueue = new Queue<IPEndPoint>();
        INatsOptions _options;
        ILogger<NatsServerPool>? _logger;

        object _sync = new object();

        public NatsServerPool(INatsOptions options)
        {

            _logger=options.LoggerFactory?.CreateLogger<NatsServerPool>();

            var servers = options.Servers;

            if(servers == null) throw new ArgumentNullException(nameof(servers));
            if(servers.Count()==0) throw new ArgumentException("server list cannot be empty",nameof(servers));

            _options = options;

            foreach (var server in servers)
                AddServer(_servers,server);
        }


        public async ValueTask<IPEndPoint> SelectServer(bool isRetry=false)
        {
            DnsEndPoint SelectDnsEndpoint(bool isRetry)
            {
                DnsEndPoint selectedServer;
                lock (_sync)
                {
                    var combinedServers = _servers;
                    if (_options.ServersOptions.HasFlag(NatsServerPoolFlags.AllowDiscovery))
                        combinedServers = _servers.Concat(_discoveredServers).ToList();

                    selectedServer = combinedServers[0];

                    if (combinedServers.Count == 1)
                    {
                        _lastSelectedDnsEndPoint = selectedServer;
                        return selectedServer!;
                    }


                    if (!isRetry)
                        _lastSelectedDnsEndPoint = null;

                    if (_options.ServersOptions.HasFlag(NatsServerPoolFlags.Randomize))
                    {
                        //if possible, randomize but also avoid returning the same selection as previous on retry
                        selectedServer = combinedServers
                            .Where(s => s != _lastSelectedDnsEndPoint)
                            .OrderBy(s => _random.Next())
                            .First();
                    }
                    else if (_lastSelectedDnsEndPoint != null)
                    {
                        //return server list in order
                        if (combinedServers.IndexOf(_lastSelectedDnsEndPoint) + 1 != combinedServers.Count)
                            selectedServer = combinedServers[combinedServers.IndexOf(_lastSelectedDnsEndPoint) + 1];
                    }

                    _lastSelectedDnsEndPoint = selectedServer;
                    return selectedServer!;
                }
            }

            if (isRetry && _retryIPEndPointQueue.Count > 0)
                return _retryIPEndPointQueue.Dequeue();
            else
                _retryIPEndPointQueue.Clear();

            var dnsEndpoint = SelectDnsEndpoint(isRetry);

            if(IPAddress.TryParse(dnsEndpoint.Host, out var ipAddress))
            {
                return new IPEndPoint(ipAddress, dnsEndpoint.Port);
            }

            _logger?.LogTrace("Resolving dns hostname {Host}", dnsEndpoint.Host);
            var resolved = await _options.DnsResolver(dnsEndpoint.Host);

            if(resolved == null)
            {
                //dns failed
                throw new InvalidOperationException("dns resolve returned 0 entries");
            }

            _logger?.LogTrace("Dns hostname {Host} resolve to {Ips}",string.Join<IPAddress>(',',resolved));

            if (resolved.Length > 1)
            {                
                for (var i = 1; i < resolved.Length; i++)
                    _retryIPEndPointQueue.Enqueue(new IPEndPoint(resolved[i], dnsEndpoint.Port));
            }

            return new IPEndPoint(resolved[0], dnsEndpoint.Port);
        }

        public void SetDiscoveredServers(IEnumerable<string> servers)
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