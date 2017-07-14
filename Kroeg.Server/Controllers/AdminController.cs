using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Kroeg.Server.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Kroeg.ActivityStreams;
using Kroeg.Server.Services.EntityStore;
using Kroeg.Server.Tools;
using Kroeg.Server.Salmon;
using System.IdentityModel.Tokens.Jwt;
using Kroeg.Server.Configuration;
using Microsoft.AspNetCore.Http.Authentication;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Http;
using Kroeg.Server.Middleware;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Kroeg.Server.Services;
using Newtonsoft.Json.Linq;
using Kroeg.Server.Middleware.Handlers.ClientToServer;
using Kroeg.Server.Middleware.Handlers.Shared;
using System.IO;

// For more information on enabling MVC for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace Kroeg.Server.Controllers
{
    [Route("admin"), Authorize("admin")]
    public class AdminController : Controller
    {
        private readonly APContext _context;
        private readonly IEntityStore _entityStore;
        private readonly EntityData _entityData;
        private readonly JwtTokenSettings _tokenSettings;
        private readonly SignInManager<APUser> _signInManager;
        private readonly IServiceProvider _provider;
        private readonly IConfigurationRoot _configuration;
        private readonly EntityFlattener _flattener;
        private readonly UserManager<APUser> _userManager;
        private readonly RelevantEntitiesService _relevantEntities;
        private readonly CollectionTools _collectionTools;

        public AdminController(APContext context, IEntityStore entityStore, EntityData entityData, JwtTokenSettings tokenSettings, SignInManager<APUser> signInManager, IServiceProvider provider, IConfigurationRoot configuration, EntityFlattener flattener, UserManager<APUser> userManager, RelevantEntitiesService relevantEntities, CollectionTools collectionTools)
        {
            _context = context;
            _entityStore = entityStore;
            _entityData = entityData;
            _tokenSettings = tokenSettings;
            _signInManager = signInManager;
            _provider = provider;
            _configuration = configuration;
            _flattener = flattener;
            _userManager = userManager;
            _relevantEntities = relevantEntities;
            _collectionTools = collectionTools;
        }

        [HttpGet("")]
        public IActionResult Index() => View();

        [HttpGet("complete")]
        public async Task<IActionResult> Autocomplete(string id)
        {
            return Json(await _context.Entities.Where(a => a.Id.StartsWith(id)).Take(10).Select(a => a.Id).ToListAsync());
        }

        [HttpGet("entity")]
        public async Task<IActionResult> GetEntity(string id)
        {
            var entity = await _entityStore.GetEntity(id, true);
            if (entity == null) return NotFound();

            return Content(entity.Data.Serialize().ToString(), "application/ld+json; profile=\"https://www.w3.org/ns/activitystreams\"");
        }

        [HttpPost("entity")]
        public async Task<IActionResult> PostEntity(string id)
        {
            string data;
            using (var reader = new StreamReader(Request.Body))
                data = await reader.ReadToEndAsync();
            var entity = await _entityStore.GetEntity(id, true);
            if (entity == null) return NotFound();

            entity.Data = ASObject.Parse(data);
            await _entityStore.StoreEntity(entity);
            await _entityStore.CommitChanges();

            return Ok();
        }
    }
}
