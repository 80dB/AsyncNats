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

        public List<NatsHost> Servers => new List<NatsHost>(_servers.Concat(_discoveredServers));

        List<NatsHost> _servers = new List<NatsHost>();
        List<NatsHost> _discoveredServers = new List<NatsHost>();
        Random _random = new Random();
        NatsHost? _lastSelectedServer = null;
        INatsOptions _options;

        object _sync = new object();

        public NatsServerPool(INatsOptions options)
        {

#pragma warning disable CS0618 // Type or member is obsolete
            //allow legacy server indication
            if (options.Server != null && options.Servers==null)
            {
                options.Servers = new string[] { $"{options.Server.Address}:{options.Server.Port}" };
            }
#pragma warning restore CS0618 // Type or member is obsolete

            var servers = options.Servers;

            if(servers == null) throw new ArgumentNullException(nameof(servers));
            if(servers.Count()==0) throw new ArgumentException("server list cannot be empty",nameof(servers));

            _options = options;

            foreach (var server in servers)
                AddServer(_servers,server);
        }

        public NatsHost SelectServer(bool isRetry=false)
        {
            lock(_sync)
            {
                var combinedServers = _servers;
                if(_options.ServersOptions.HasFlag(NatsServerPoolFlags.AllowDiscovery))
                    combinedServers = _servers.Concat(_discoveredServers).ToList();

                var selectedServer = combinedServers[0];

                if (combinedServers.Count > 1)
                {
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
                    else
                    {
                        if(_lastSelectedServer != null)
                        {
                            //return server list in order
                            if (combinedServers.IndexOf(_lastSelectedServer) + 1 >= combinedServers.Count)
                                selectedServer = combinedServers[0];
                            else
                                selectedServer = combinedServers[combinedServers.IndexOf(_lastSelectedServer) + 1];
                        }
                    }
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
                _discoveredServers = new List<NatsHost>();

                foreach (var server in servers.OrderBy(s=>Guid.NewGuid())) //randomize discovered
                    AddServer(_discoveredServers, server);
            }
            
        }

        private NatsHost ParseAndNormalizeServerEntry(string server)
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

            return new NatsHost(stringHost,port);
            
        }

        private void AddServer(List<NatsHost> list, string server)
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

        internal class NatsHost:IEquatable<NatsHost>
        {
            public string Host { get; set; }
            public int Port { get; set; }

            public NatsHost(string host, int port)            {
                Host = host;
                Port = port;
            }

            public bool Equals(NatsHost other)
            {
                return other.Host == this.Host && other.Port == this.Port;
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(Host, Port);
            }

            public override string ToString()
            {
                return $"{Host}:{Port}";
            }
        }
    }
}