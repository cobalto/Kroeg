using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Kroeg.Server.BackgroundTasks;
using Kroeg.Server.Configuration;
using Kroeg.Server.Middleware;
using Kroeg.Server.Models;
using Kroeg.Server.OStatusCompat;
using Kroeg.Server.Services;
using Kroeg.Server.Services.EntityStore;
using Kroeg.Server.Tools;
using Kroeg.Server.Services.Notifiers.Redis;
using Kroeg.Server.Services.Notifiers;
using Microsoft.AspNetCore.Http;
using Kroeg.Server.Services.Template;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;

namespace Kroeg.Server
{
    public class Startup
    {
        public Startup(IHostingEnvironment env)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(env.ContentRootPath)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true)
                .AddEnvironmentVariables();
            Configuration = builder.Build();
        }

        public IConfigurationRoot Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            // Add framework services.
            services.AddMvc();

            services.AddDbContext<APContext>(options => options.UseNpgsql(Configuration.GetConnectionString("Default")).EnableSensitiveDataLogging());

            services.AddIdentity<APUser, IdentityRole>()
                .AddEntityFrameworkStores<APContext>()
                .AddDefaultTokenProviders();

            services.AddAuthorization(options =>
            {
                options.AddPolicy("admin", policy => policy.RequireClaim("admin"));
                options.AddPolicy("pass", policy => policy.AddAuthenticationSchemes(IdentityConstants.ApplicationScheme).RequireAuthenticatedUser());
            });

            services.Configure<IdentityOptions>(options =>
            {
                options.Password.RequireDigit = false;
                options.Password.RequiredLength = 0;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireLowercase = false;
            });
            
            var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Configuration.GetSection("Kroeg")["TokenSigningKey"]));
            var tokenSettings = new JwtTokenSettings
            {
                Audience =Configuration.GetSection("Kroeg")["BaseUri"],
                Issuer = Configuration.GetSection("Kroeg")["BaseUri"],
                ExpiryTime = TimeSpan.FromDays(30),
                Credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256),
                ValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = signingKey,

                    ValidateIssuer = true,
                    ValidIssuer = Configuration.GetSection("Kroeg")["BaseUri"],

                    ValidateAudience = true,
                    ValidAudience = Configuration.GetSection("Kroeg")["BaseUri"],

                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                }
            };
            services.AddSingleton(tokenSettings);

            services.AddSingleton(new EntityData(Configuration.GetSection("Kroeg"))
            {
                EntityNames = Configuration.GetSection("EntityNames")
            });

            var redis = Configuration.GetSection("Kroeg").GetValue<string>("Redis", null);
            if (!string.IsNullOrEmpty(redis))
            {
                services.AddSingleton(new RedisNotifierBase(redis));
                services.AddTransient<INotifier>((a) => ActivatorUtilities.CreateInstance<RedisNotifier>(a));
            }
            else
            {
                services.AddSingleton<INotifier>(new LocalNotifier());
            }

            services.AddSingleton<BackgroundTaskQueuer>();

            services.AddSingleton(Configuration);

            services.AddTransient<EntityFlattener>();
            services.AddTransient<CollectionTools>();
            services.AddTransient<DeliveryService>();
            services.AddTransient<RelevantEntitiesService>();
            services.AddTransient<DatabaseEntityStore>();
            services.AddTransient<ActivityService>();
            services.AddTransient<AtomEntryParser>();
            services.AddTransient<AtomEntryGenerator>();
            services.AddTransient<IEntityStore>((provider) =>
            {
                var dbservice = provider.GetRequiredService<DatabaseEntityStore>();
                var flattener = provider.GetRequiredService<EntityFlattener>();
                var httpAccessor = provider.GetService<IHttpContextAccessor>();
                return new RetrievingEntityStore(dbservice, flattener, provider, httpAccessor);
            });
            services.AddTransient<TemplateService>();
            services.AddTransient<SignatureVerifier>();

            services.AddAuthentication(o => {
                o.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                o.DefaultChallengeScheme = IdentityConstants.ApplicationScheme;
                o.DefaultSignInScheme = IdentityConstants.ApplicationScheme;
            })
                .AddJwtBearer((options) => {
                    options.TokenValidationParameters = tokenSettings.ValidationParameters;

                    options.Audience =Configuration.GetSection("Kroeg")["BaseUri"];
                    options.ClaimsIssuer = Configuration.GetSection("Kroeg")["BaseUri"];
                });
            
            services.ConfigureApplicationCookie((options) => {
                options.Cookie.Name = "Kroeg.Auth";
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory)
        {
            loggerFactory.AddConsole(Configuration.GetSection("Logging"));
            loggerFactory.AddDebug();

            app.UseAuthentication();
            app.UseWebSockets();
            app.UseStaticFiles();

            app.UseDeveloperExceptionPage();
            app.UseMiddleware<WebSubMiddleware>();
            app.UseMiddleware<GetEntityMiddleware>();
            app.UseMvc();


            app.ApplicationServices.GetRequiredService<APContext>().Database.Migrate();
            app.ApplicationServices.GetRequiredService<BackgroundTaskQueuer>(); // kickstart background tasks!
        }
    }
}
