using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using System;
using System.Threading.Tasks;
using Kroeg.Server.Models;

namespace Kroeg.Server.BackgroundTasks
{
    public abstract class BaseTask
    {
        protected EventQueueItem EventQueueItem;

        protected BaseTask(EventQueueItem item)
        {
            EventQueueItem = item;
        }

        public static async Task Go(APContext context, EventQueueItem item, IServiceProvider provider)
        {
            var type = Type.GetType("Kroeg.Server.BackgroundTasks." + item.Action);

            var resolved = (BaseTask)ActivatorUtilities.CreateInstance(provider, type, item);

            try
            {
                await resolved.Go();
                context.EventQueue.Remove(item);
            }
            catch(Exception)
            {
                // failed
                item.AttemptCount++;
                item.NextAttempt = resolved.NextTry(item.AttemptCount);
            }

            await context.SaveChangesAsync();
        }

        public virtual DateTime NextTry(int fails)
        {
            return DateTime.Now.AddMinutes(fails * fails * 3);
        }

        public abstract Task Go();
    }

    public abstract class BaseTask<T, TR> : BaseTask where TR : BaseTask<T, TR>
    {
        protected T Data;

        public static EventQueueItem Make(T data)
        {
            var type = typeof(TR).FullName.Replace("Kroeg.Server.BackgroundTasks.", "");

            return new EventQueueItem
            {
                Action = type,
                Added = DateTime.Now,
                AttemptCount = 0,
                Data = JsonConvert.SerializeObject(data),
                NextAttempt = DateTime.Now
            };
        }

        protected BaseTask(EventQueueItem item)
            : base(item)
        {
            Data = JsonConvert.DeserializeObject<T>(EventQueueItem.Data);
        }
    }
}
