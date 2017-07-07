using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kroeg.Server.Models;
using Microsoft.Extensions.Logging;

namespace Kroeg.Server.BackgroundTasks
{
    public class BackgroundTaskQueuer
    {
        private CancellationTokenSource _cancellationTokenSource;
        private readonly APContext _context;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<BackgroundTaskQueuer> _logger;

        public static BackgroundTaskQueuer Instance { get; private set; }

        public void NotifyUpdated()
        {
            _cancellationTokenSource?.Cancel();
        }

        public BackgroundTaskQueuer(APContext context, IServiceProvider serviceProvider, ILogger<BackgroundTaskQueuer> logger)
        {
            _context = context;
            _serviceProvider = serviceProvider;
            _logger = logger;
            Instance = this;

            _do();
        }

        private Task _whenCanceled()
        {
            var tcs = new TaskCompletionSource<bool>();
            _cancellationTokenSource.Token.Register(s => ((TaskCompletionSource<bool>)s).SetResult(true), tcs);
            return tcs.Task;
        }

        public async void _do()
        {
            while (true)
            {
                // set up the wait
                _logger.LogDebug("Preparing to sleep...");
                _cancellationTokenSource = new CancellationTokenSource();
                var nextAction = await _context.EventQueue.OrderBy(a => a.NextAttempt).FirstOrDefaultAsync();
                if (nextAction == null)
                {
                    _logger.LogDebug("No new action, sleeping until next save");
                    await _whenCanceled();
                }
                else if (nextAction.NextAttempt < DateTime.Now.AddSeconds(2))
                {
                    _logger.LogDebug($"Next action is now; running {nextAction.Action}");
                    await BaseTask.Go(_context, nextAction, _serviceProvider);
                }
                else
                {
                    _logger.LogDebug($"Sleeping until {nextAction.NextAttempt}");
                    try
                    {
                        await Task.Delay((int)(nextAction.NextAttempt - DateTime.Now).TotalMilliseconds, _cancellationTokenSource.Token);
                    }
                    catch (TaskCanceledException) { /* nothing to do */ }
                }
                var tokenSource = _cancellationTokenSource;
                _cancellationTokenSource = null;
                tokenSource.Dispose();

                _logger.LogDebug("Woke up!");
                nextAction = await _context.EventQueue.OrderBy(a => a.NextAttempt).FirstOrDefaultAsync();
                _logger.LogDebug($"Next action: {nextAction?.Action ?? "nothing"}");
                if (nextAction == null) continue;

                // run the action
                await BaseTask.Go(_context, nextAction, _serviceProvider);
            }
        }
    }
}
