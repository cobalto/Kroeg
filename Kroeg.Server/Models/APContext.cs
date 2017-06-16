using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Kroeg.Server.BackgroundTasks;

namespace Kroeg.Server.Models
{
    public class APContext : IdentityDbContext<APUser>
    {
        public APContext(DbContextOptions options)
            : base(options)
        {
        }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            var newEventQueue = ChangeTracker.HasChanges() && ChangeTracker.Entries<EventQueueItem>().Any(a => a.State == EntityState.Added);

            var returnValue = await base.SaveChangesAsync(cancellationToken);

            if (newEventQueue)
            {
                BackgroundTaskQueuer.Instance.NotifyUpdated();
            }

            return returnValue;
        }

        public DbSet<APEntity> Entities { get; set; }
        public DbSet<CollectionItem> CollectionItems { get; set; }
        public DbSet<UserActorPermission> UserActorPermissions { get; set; }
        public DbSet<EventQueueItem> EventQueue { get; set; }

        public DbSet<SalmonKey> SalmonKeys { get; set; }
        public DbSet<WebsubSubscription> WebsubSubscriptions { get; set; }
    }
}
