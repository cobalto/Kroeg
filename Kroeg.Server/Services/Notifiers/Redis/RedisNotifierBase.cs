using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StackExchange.Redis;

namespace Kroeg.Server.Services.Notifiers.Redis
{
    public class RedisNotifierBase
    {
        private readonly ConnectionMultiplexer _multiplexer;

        public RedisNotifierBase(string path)
        {
            _multiplexer = ConnectionMultiplexer.Connect(path);
        }

        public ISubscriber GetSubscriber() => _multiplexer.GetSubscriber();
    }
}
