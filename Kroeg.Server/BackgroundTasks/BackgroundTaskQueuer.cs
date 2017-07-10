using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kroeg.Server.Models;
using Microsoft.Extensions.Logging;
using Kroeg.Server.Services;

namespace Kroeg.Server.BackgroundTasks
{
    public class BackgroundTaskQueuer
    {
        private CancellationTokenSource _cancellationTokenSource;
        private readonly APContext _context;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<BackgroundTaskQueuer> _logger;
        private readonly INotifier _notifier;

        public static string BackgroundTaskPath = "backgroundtask:new";

        public BackgroundTaskQueuer(APContext context, IServiceProvider serviceProvider, ILogger<BackgroundTaskQueuer> logger, INotifier notifier)
        {
            _context = context;
            _serviceProvider = serviceProvider;
            _logger = logger;
            _notifier = notifier;

            _notifier.Subscribe(BackgroundTaskPath, (a) => _cancellationTokenSource?.Cancel());

            _do();
        }

        private Task _whenCanceled()
        {
            var tcs = new TaskCompletionSource<bool>();
            _cancellationTokenSource.Token.Register(s => ((TaskCompletionSource<bool>)s).SetResult(true), tcs);
            return tcs.Task;
        }

        private async Task<int> _getSleepTime(DateTime? after = null)
        {
            var av = after ?? DateTime.MinValue;
            var nextAction = await _context.EventQueue.OrderBy(a => a.NextAttempt).Where(a => a.NextAttempt > av).FirstOrDefaultAsync();
            if (nextAction == null) return -1;
            var retval = (int)(nextAction.NextAttempt - DateTime.Now).TotalMilliseconds;
            return Math.Max(retval, 0);
        }

        public async void _do()
        {
            await Task.Delay(2000);

            DateTime after = DateTime.MinValue;
            while (true)
            {
                // set up the wait
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = new CancellationTokenSource();
                var sleepTime = await _getSleepTime(after);
                if (sleepTime != 0)
                {
                    try
                    {
                        await Task.Delay(sleepTime, _cancellationTokenSource.Token);
                    }
                    catch (TaskCanceledException) { }
                }
                var nextAction = await _context.EventQueue.OrderBy(a => a.NextAttempt).Where(a => a.NextAttempt > after).FirstOrDefaultAsync();
                _logger.LogDebug($"Next action: {nextAction?.Action ?? "nothing"}");
                if (nextAction == null) continue;

                if (await _notifier.Synchronize(BackgroundTaskPath + ":" + nextAction.Id + nextAction.NextAttempt.ToString()))
                {
                    // we won!
                    await BaseTask.Go(_context, nextAction, _serviceProvider);
                }
                else
                {
                    after = nextAction.NextAttempt;
                }
            }
        }
    }
}
