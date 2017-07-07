using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Kroeg.Server.Services.Notifiers
{
    public class LocalNotifier : INotifier
    {
        private Dictionary<string, List<Action<string>>> _actions = new Dictionary<string, List<Action<string>>>();

        public async Task Notify(string path, string val)
        {
            if (_actions.ContainsKey(path))
                foreach (var item in _actions[path])
                    item(val);

            await Task.Yield();
        }

        public async Task Subscribe(string path, Action<string> toRun)
        {
            if (!_actions.ContainsKey(path)) _actions[path] = new List<Action<string>>();
            _actions[path].Add(toRun);
            await Task.Yield();
        }

        public async Task Unsubscribe(string path, Action<string> toRun)
        {
            if (!_actions.ContainsKey(path)) return;
            _actions[path].Remove(toRun);
            await Task.Yield();
        }
    }
}
