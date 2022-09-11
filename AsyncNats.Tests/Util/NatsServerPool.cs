namespace AsyncNats.Tests.Util
{
    using System;
    using System.Buffers;
    using System.Net;
    using System.Text;
    using System.Threading.Tasks;
    using EightyDecibel.AsyncNats;
    using EightyDecibel.AsyncNats.Messages;
    using Xunit;

    public class NatsServerPoolTests
    {
        static class TestResolver
        {
            public static async Task<IPAddress[]> ResolveSingle(string host)
            {
                switch (host)
                {
                    case "server1.nats.local":return new[] { IPAddress.Parse("192.168.0.1") };
                    case "server2.nats.local": return new[] { IPAddress.Parse("192.168.0.2") };
                    case "server3.nats.local": return new[] { IPAddress.Parse("192.168.0.3") };
                    default: return new[] { IPAddress.Parse("192.168.0.1") };
                }
            }

            public static async Task<IPAddress[]> ResolveMultiple(string host)
            {
                switch (host)
                {
                    case "server1.nats.local": return new[] { IPAddress.Parse("192.168.0.1"), IPAddress.Parse("192.168.0.2"), IPAddress.Parse("192.168.0.3") };
                    case "server2.nats.local": return new[] { IPAddress.Parse("192.168.1.1"), IPAddress.Parse("192.168.1.2"), IPAddress.Parse("192.168.1.3") };
                    case "server3.nats.local": return new[] { IPAddress.Parse("192.168.2.1"), IPAddress.Parse("192.168.1.2"), IPAddress.Parse("192.168.1.3") };

                    default: return new[] { IPAddress.Parse("192.168.0.1"), IPAddress.Parse("192.168.0.2"), IPAddress.Parse("192.168.0.3") };
                }
            }


        }
        

        [Fact]
        public void AcceptSingleServer()
        {
            var options = new NatsDefaultOptions()
            {
                Servers = new[] {"127.0.0.1:4222"}
            };

            var pool = new NatsServerPool(options);

            Assert.Equal("127.0.0.1", pool.Servers[0].Host);
            Assert.Equal(4222, pool.Servers[0].Port);
        }

        [Fact]
        public void AcceptMultipleServers()
        {
            var options = new NatsDefaultOptions()
            {
                Servers = new[] { "127.0.0.1:4222","127.0.0.2:4222" }
            };

            var pool = new NatsServerPool(options);

            Assert.Equal(2, pool.Servers.Count);           

            Assert.Equal("127.0.0.1", pool.Servers[0].Host); 
            Assert.Equal(4222, pool.Servers[0].Port);

            Assert.Equal("127.0.0.2", pool.Servers[1].Host);            
            Assert.Equal(4222, pool.Servers[1].Port);

        }

        [Fact]
        public void AcceptDnsHostname()
        {
            var options = new NatsDefaultOptions()
            {
                Servers = new[] { "nats.local.com:4222" }
            };

            var pool = new NatsServerPool(options);
            Assert.Equal("nats.local.com", pool.Servers[0].Host);
            Assert.Equal(4222, pool.Servers[0].Port);
        }

        [Fact]
        public void AcceptDnsHostnameWithScheme()
        {
            var options = new NatsDefaultOptions()
            {
                Servers = new[] { "nats://nats.local.com:4222" }
            };

            var pool = new NatsServerPool(options);

            Assert.Equal("nats.local.com", pool.Servers[0].Host);
            Assert.Equal(4222, pool.Servers[0].Port);
        }

        [Fact]
        public void AddsDefaultPortIfMissing()
        {
            var options = new NatsDefaultOptions()
            {
                Servers = new[] { "127.0.0.1" }
            };

            var pool = new NatsServerPool(options);

            Assert.Equal("127.0.0.1", pool.Servers[0].Host);
            Assert.Equal(4222, pool.Servers[0].Port);
        }

        [Fact]
        public void ThrowsOnInvalidHostName()
        {
            var options = new NatsDefaultOptions()
            {
                Servers = new[] { "inv@lid.com:4222" }
            };
            Assert.Throws<FormatException>(() => new NatsServerPool(options));

            options = new NatsDefaultOptions()
            {
                Servers = new[] { "valid.nats.com:" }
            };
            Assert.Throws<FormatException>(() => new NatsServerPool(options));

            options = new NatsDefaultOptions()
            {
                Servers = new[] { "valid.nats.com:not_an_int" }
            };
            Assert.Throws<FormatException>(() => new NatsServerPool(options));

            options = new NatsDefaultOptions()
            {
                Servers = new[] { "extra:valid.nats.com:4222" }
            };
            Assert.Throws<FormatException>(() => new NatsServerPool(options));
        }

        [Fact]
        public async Task SelectServersInOrder()
        {
            var server1 = "server1.nats.local:4222";
            var server2 = "server2.nats.local:4222";
            var server3 = "server3.nats.local:4222";

            var options = new NatsDefaultOptions()
            {
                Servers = new[] { server1,server2,server3 },
                DnsResolver=TestResolver.ResolveSingle
            };

            var pool = new NatsServerPool(options);

            var selectedServer = await pool.SelectServer(isRetry: false);
            Assert.Equal(new IPEndPoint(IPAddress.Parse("192.168.0.1"),4222), selectedServer);

            selectedServer = await pool.SelectServer(isRetry: true);
            Assert.Equal(new IPEndPoint(IPAddress.Parse("192.168.0.2"), 4222), selectedServer);

            selectedServer = await pool.SelectServer(isRetry: true);
            Assert.Equal(new IPEndPoint(IPAddress.Parse("192.168.0.3"), 4222), selectedServer);

            selectedServer = await pool.SelectServer(isRetry: true);
            Assert.Equal(new IPEndPoint(IPAddress.Parse("192.168.0.1"), 4222), selectedServer);//should rotate
        }

        [Fact]
        public async Task RotateMultipleIpsForSingleDnsEntry()
        {
            var server1 = "server1.nats.local:4222";
            var server2 = "server2.nats.local:4222";

            var options = new NatsDefaultOptions()
            {
                Servers = new[] { server1, server2 },
                DnsResolver = TestResolver.ResolveMultiple
            };

            var pool = new NatsServerPool(options);

            var selectedServer = await pool.SelectServer(isRetry: false);
            Assert.Equal(new IPEndPoint(IPAddress.Parse("192.168.0.1"), 4222), selectedServer);

            selectedServer = await pool.SelectServer(isRetry: true);
            Assert.Equal(new IPEndPoint(IPAddress.Parse("192.168.0.2"), 4222), selectedServer);

            selectedServer = await pool.SelectServer(isRetry: true);
            Assert.Equal(new IPEndPoint(IPAddress.Parse("192.168.0.3"), 4222), selectedServer);

            selectedServer = await pool.SelectServer(isRetry: true);
            Assert.Equal(new IPEndPoint(IPAddress.Parse("192.168.1.1"), 4222), selectedServer);

            selectedServer = await pool.SelectServer(isRetry: true);
            Assert.Equal(new IPEndPoint(IPAddress.Parse("192.168.1.2"), 4222), selectedServer);

            selectedServer = await pool.SelectServer(isRetry: true);
            Assert.Equal(new IPEndPoint(IPAddress.Parse("192.168.1.3"), 4222), selectedServer);

            selectedServer = await pool.SelectServer(isRetry: true);
            Assert.Equal(new IPEndPoint(IPAddress.Parse("192.168.0.1"), 4222), selectedServer);
        }

        [Fact]
        public void DoNotRepeatSelectionOnRetry()
        {
            var server1 = "server1.nats.local:4222";
            var server2 = "server2.nats.local:4222";
            var server3 = "server3.nats.local:4222";

            var options = new NatsDefaultOptions()
            {
                Servers = new[] { server1, server2, server3 },
                ServersOptions=NatsServerPoolFlags.Randomize
            };

            var pool = new NatsServerPool(options);

            for (var i = 0; i < 10; i++)
            {
                var first = pool.SelectServer(isRetry: false);
                var second = pool.SelectServer(isRetry: true);

                Assert.NotEqual(first, second);
            }            
        }

        [Fact]
        public async Task AllowDiscoveryAndSelectDiscoveredServer()
        {
            var options = new NatsDefaultOptions()
            {
                Servers = new[] { "nats://server1.nats.local:4222" },
                ServersOptions=NatsServerPoolFlags.AllowDiscovery,
                DnsResolver=TestResolver.ResolveSingle
            };

            var pool = new NatsServerPool(options);

            pool.SetDiscoveredServers(new[] { "nats://server2.nats.local:4222" });

            Assert.Equal("server2.nats.local", pool.Servers[1].Host);
            Assert.Equal(4222, pool.Servers[1].Port);

            var selectedServer = await pool.SelectServer();
            Assert.Equal(new IPEndPoint(IPAddress.Parse("192.168.0.1"), 4222), selectedServer);
            Assert.Equal(4222, selectedServer.Port);

            selectedServer = await pool.SelectServer(isRetry:true);
            Assert.Equal(new IPEndPoint(IPAddress.Parse("192.168.0.2"), 4222), selectedServer);
            Assert.Equal(4222, selectedServer.Port);
        }

        [Fact]
        public void DoNotAddIfDiscoveryDisallowed()
        {
            var options = new NatsDefaultOptions()
            {
                Servers = new[] { "nats://nats.local.com:4222" },
                ServersOptions = NatsServerPoolFlags.None
            };

            var pool = new NatsServerPool(options);

            pool.SetDiscoveredServers(new[] { "nats://nats2.local.com:4222" });

            Assert.Single(pool.Servers);
        }
    }
}