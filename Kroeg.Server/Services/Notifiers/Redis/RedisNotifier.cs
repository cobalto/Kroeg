using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Kroeg.Server.Services.Notifiers.Redis
{
    public class RedisNotifier : INotifier
    {
        private readonly ISubscriber _subscriber;

        public RedisNotifier(RedisNotifierBase notifierBase)
        {
            _subscriber = notifierBase.GetSubscriber();
        }

        private Dictionary<Action<string>, Action<RedisChannel, RedisValue>> _data = new Dictionary<Action<string>, Action<RedisChannel, RedisValue>>();

        public async Task Subscribe(string path, Action<string> toRun)
        {
            _data[toRun] = (chan, val) => toRun(val);
            await _subscriber.SubscribeAsync(path, _data[toRun]);
        }

        public async Task Unsubscribe(string path, Action<string> toRun)
        {
            if (!_data.ContainsKey(toRun)) return;
            await _subscriber.UnsubscribeAsync(path, _data[toRun]);
            _data.Remove(toRun);
        }

        public async Task Notify(string path, string val)
        {
            await _subscriber.PublishAsync(path, val);
        }
    }
}
