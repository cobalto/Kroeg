using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Kroeg.Server.Services
{
    public interface INotifier
    {
        Task Subscribe(string path, Action<string> toRun);
        Task Unsubscribe(string path, Action<string> toRun);
        Task Notify(string path, string val);
    }
}
