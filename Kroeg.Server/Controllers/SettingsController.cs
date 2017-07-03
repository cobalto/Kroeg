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

// For more information on enabling MVC for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace Kroeg.Server.Controllers
{
    [Route("settings")]
    public class SettingsController : Controller
    {
        public class BaseModel
        {
            public APUser User { get; set; }
            public List<UserActorPermission> Actors { get; set; }
        }

        public class NewActorModel
        {
            public BaseModel Menu { get; set; }

            public string Username { get; set; }
            public string Name { get; set; }
            public string Summary { get; set; }
        }

        public class EditActorModel
        {
            public BaseModel Menu { get; set; }
            public List<UserActorPermission> OtherPeople { get; set; }

            public UserActorPermission Actor { get; set; }
            public string OtherIsAdmin { get; set; }
            public string AuthorizeOther { get; set; }
        }

        private readonly APContext _context;
        private readonly IEntityStore _entityStore;
        private readonly EntityData _entityData;
        private readonly JwtTokenSettings _tokenSettings;
        private readonly SignInManager<APUser> _signInManager;
        private readonly IServiceProvider _provider;
        private readonly IConfigurationRoot _configuration;

        public SettingsController(APContext context, IEntityStore entityStore, EntityData entityData, JwtTokenSettings tokenSettings, SignInManager<APUser> signInManager, IServiceProvider provider, IConfigurationRoot configuration)
        {
            _context = context;
            _entityStore = entityStore;
            _entityData = entityData;
            _tokenSettings = tokenSettings;
            _signInManager = signInManager;
            _provider = provider;
            _configuration = configuration;
        }

        private async Task<BaseModel> _getUserInfo()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = await _context.Users.FirstOrDefaultAsync(a => a.Id == userId);
            var actors = await _context.UserActorPermissions.Where(a => a.User == user).Include(a => a.Actor).ToListAsync();

            return new BaseModel { User = user, Actors = actors };
        }

        [Authorize]
        public async Task<IActionResult> Index()
        {
            var data = await _getUserInfo();
            if (data.Actors.Count == 0) return View("NewActor", new NewActorModel() { Menu = data });
            return View("ShowActor", new EditActorModel { Menu = data, OtherPeople = await _context.UserActorPermissions.Where(a => a.ActorId == data.Actors[0].ActorId).ToListAsync(), Actor = data.Actors[0] });
        }

        [Authorize, HttpGet("new")]
        public async Task<IActionResult> NewActor()
        {
            return View("NewActor", new NewActorModel { Menu = await _getUserInfo() });
        }

        [HttpGet("auth")]
        public async Task<IActionResult> RedeemAuth(string token)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            SecurityToken validatedToken;
            var claims = tokenHandler.ValidateToken(token, _tokenSettings.ValidationParameters, out validatedToken);
            if (claims == null || validatedToken.ValidTo < DateTime.UtcNow) return Unauthorized();

            var userId = claims.FindFirstValue(ClaimTypes.NameIdentifier);
            var user = await _context.Users.FirstOrDefaultAsync(a => a.Id == userId);
            await _signInManager.SignInAsync(user, false);
            return RedirectToActionPermanent("Index");
        }

        [Authorize, HttpPost("auth")]
        public IActionResult CreateAuth()
        {
            var claims = new Claim[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, User.FindFirstValue(ClaimTypes.NameIdentifier))
            };

            var jwt = new JwtSecurityToken(
                issuer: _tokenSettings.Issuer,
                audience: _tokenSettings.Audience,
                claims: claims,
                notBefore: DateTime.UtcNow,
                expires: DateTime.UtcNow.Add(TimeSpan.FromMinutes(5)),
                signingCredentials: _tokenSettings.Credentials
                );

            var encodedJwt = new JwtSecurityTokenHandler().WriteToken(jwt);

            return Ok(Url.Action("RedeemAuth", new { token = encodedJwt }));

        }

        [Authorize, HttpGet("actor")]
        public async Task<IActionResult> Edit(string id)
        {
            var data = await _getUserInfo();
            var actor = data.Actors.FirstOrDefault(a => a.ActorId == id);
            if (actor == null) return await Index();

            return View("ShowActor", new EditActorModel { Menu = data, OtherPeople = await _context.UserActorPermissions.Where(a => a.ActorId == actor.ActorId).ToListAsync(), Actor = actor });

        }

        private async Task<APEntity> _newCollection(string type, string attributedTo)
        {
            var obj = new ASObject();
            obj["type"].Add(new ASTerm("OrderedCollection"));
            obj["attributedTo"].Add(new ASTerm(attributedTo));
            obj.Replace("id", new ASTerm(await _entityData.FindUnusedID(_entityStore, obj, type, attributedTo)));
            var entity = APEntity.From(obj, true);
            entity.Type = "_" + type;
            entity = await _entityStore.StoreEntity(entity);
            await _entityStore.CommitChanges();

            return entity;
        }

        [Authorize, HttpPost("uploadMedia")]
        public async Task<IActionResult> UploadMedia()
        {
            var @object = Request.Form["object"];
            var file = Request.Form.Files["file"];

            var handler = ActivatorUtilities.CreateInstance<GetEntityMiddleware.GetEntityHandler>(_provider, User);
            var obj = ASObject.Parse(@object);
            var mainObj = obj;
            if (obj["object"].Any())
            {
                mainObj = obj["object"].Single().SubObject;
            }

            var uploadPath = _configuration.GetSection("Kroeg")["FileUploadPath"];
            var uploadUri = _configuration.GetSection("Kroeg")["FileUploadUrl"];

            var extension = file.FileName.Split('.').Last().Replace('/', '_');

            var fileName = Guid.NewGuid().ToString() + "." + extension;

            var str = System.IO.File.OpenWrite(uploadPath + fileName);
            await file.CopyToAsync(str);
            str.Dispose();

            mainObj.Replace("url", new ASTerm(uploadUri + fileName));

            try
            {
                obj = await handler.Post(HttpContext, (string)HttpContext.Items["fullPath"], obj);
            }
            catch (UnauthorizedAccessException e)
            {
                return StatusCode(403, e);
            }
            catch (InvalidOperationException e)
            {
                return StatusCode(401, e);
            }

            if (obj == null)
                return NotFound();
            return Content(obj.Serialize().ToString(), "application/ld+json; profile=\"https://www.w3.org/ns/activitystreams\"");
       }

        [Authorize, HttpPost("new")]
        public async Task<IActionResult> MakeNewActor(NewActorModel model)
        {
            var data = await _getUserInfo();

            if (string.IsNullOrWhiteSpace(model.Username)) return View("NewActor", model);
            var user = model.Username;

            var obj = new ASObject();
            obj["type"].Add(new ASTerm("Person"));
            obj["preferredUsername"].Add(new ASTerm(user));
            obj["name"].Add(new ASTerm(string.IsNullOrWhiteSpace(model.Name) ? "Unnamed" : model.Name));
            if (!string.IsNullOrWhiteSpace(model.Summary))
                obj["summary"].Add(new ASTerm(model.Summary));

            var id = await _entityData.UriFor(_entityStore, obj);
            obj["id"].Add(new ASTerm(id));

            var inbox = await _newCollection("inbox", id);
            var outbox = await _newCollection("outbox", id);
            var following = await _newCollection("following", id);
            var followers = await _newCollection("followers", id);
            var likes = await _newCollection("likes", id);

            obj["following"].Add(new ASTerm(following.Id));
            obj["followers"].Add(new ASTerm(followers.Id));
            obj["likes"].Add(new ASTerm(likes.Id));
            obj["inbox"].Add(new ASTerm(inbox.Id));
            obj["outbox"].Add(new ASTerm(outbox.Id));


            var userEntity = await _entityStore.StoreEntity(APEntity.From(obj, true));
            await _entityStore.CommitChanges();

            var userId = User.FindFirst(ClaimTypes.NameIdentifier).Value;

            _context.UserActorPermissions.Add(new UserActorPermission { UserId = userId, ActorId = userEntity.Id, IsAdmin = true });

            var key = new SalmonKey();
            var salmon = MagicKey.Generate();
            key.EntityId = userEntity.Id;
            key.PrivateKey = salmon.PrivateKey;

            _context.SalmonKeys.Add(key);
            await _context.SaveChangesAsync();

            return RedirectToAction("Edit", new { id = id });
        }
    }
}
