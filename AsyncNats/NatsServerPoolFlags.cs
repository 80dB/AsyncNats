namespace EightyDecibel.AsyncNats
{
    using System;
    using System.Buffers;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Logging;

    [Flags]
    public enum NatsServerPoolFlags
    {
        None=0,
        AllowDiscovery = 1,
        Randomize = 2
    }

}