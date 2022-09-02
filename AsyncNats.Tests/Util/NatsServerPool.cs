namespace AsyncNats.Tests.Util
{
    using System;
    using System.Buffers;
    using System.Net;
    using System.Text;
    using EightyDecibel.AsyncNats;
    using EightyDecibel.AsyncNats.Messages;
    using Xunit;

    public class NatsServerPoolTests
    {

        

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
        public void SelectServersInOrder()
        {
            var server1 = "server1.nats.local:4222";
            var server2 = "server2.nats.local:4222";
            var server3 = "server3.nats.local:4222";

            var options = new NatsDefaultOptions()
            {
                Servers = new[] { server1,server2,server3 }
            };

            var pool = new NatsServerPool(options);

            var selectedServer = pool.SelectServer(isRetry: false);
            Assert.Equal("server1.nats.local", selectedServer.Host);

            selectedServer = pool.SelectServer(isRetry: true);
            Assert.Equal("server2.nats.local", selectedServer.Host);

            selectedServer = pool.SelectServer(isRetry: true);
            Assert.Equal("server3.nats.local", selectedServer.Host);

            selectedServer = pool.SelectServer(isRetry: true);
            Assert.Equal("server1.nats.local", selectedServer.Host);//should rotate
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
        public void AllowDiscoveryAndSelectDiscoveredServer()
        {
            var options = new NatsDefaultOptions()
            {
                Servers = new[] { "nats://nats.local.com:4222" },
                ServersOptions=NatsServerPoolFlags.AllowDiscovery
            };

            var pool = new NatsServerPool(options);

            pool.AddDiscoveredServers(new[] { "nats://nats2.local.com:4222" });

            Assert.Equal("nats2.local.com", pool.Servers[1].Host);
            Assert.Equal(4222, pool.Servers[1].Port);

            var selectedServer = pool.SelectServer();
            Assert.Equal("nats.local.com", selectedServer.Host);
            Assert.Equal(4222, selectedServer.Port);

            selectedServer = pool.SelectServer(isRetry:true);
            Assert.Equal("nats2.local.com", selectedServer.Host);
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

            pool.AddDiscoveredServers(new[] { "nats://nats2.local.com:4222" });

            Assert.Single(pool.Servers);
        }
    }
}