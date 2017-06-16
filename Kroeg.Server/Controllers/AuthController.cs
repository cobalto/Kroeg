using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http;
using System.Xml.Linq;
using Kroeg.ActivityStreams;
using Kroeg.Server.Configuration;
using Kroeg.Server.Models;
using Kroeg.Server.OStatusCompat;
using Kroeg.Server.Services.EntityStore;
using Kroeg.Server.Tools;
using Newtonsoft.Json;

// For more information on enabling MVC for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace Kroeg.Server.Controllers
{
    [Route("/auth")]
    public class AuthController : Controller
    {
        private readonly APContext _context;
        private readonly UserManager<APUser> _userManager;
        private readonly SignInManager<APUser> _signInManager;
        private readonly JwtTokenSettings _tokenSettings;
        private readonly EntityFlattener _entityFlattener;
        private readonly IEntityStore _entityStore;
        private readonly AtomEntryParser _entryParser;
        private readonly AtomEntryGenerator _entryGenerator;
        private readonly EntityData _entityConfiguration;

        public AuthController(APContext context, UserManager<APUser> userManager, SignInManager<APUser> signInManager, JwtTokenSettings tokenSettings, EntityFlattener entityFlattener, IEntityStore entityStore, AtomEntryParser entryParser, AtomEntryGenerator entryGenerator, EntityData entityConfiguration)
        {
            _context = context;
            _userManager = userManager;
            _signInManager = signInManager;
            _tokenSettings = tokenSettings;
            _entityFlattener = entityFlattener;
            _entityStore = entityStore;
            _entryParser = entryParser;
            _entryGenerator = entryGenerator;
            _entityConfiguration = entityConfiguration;
        }

        public class LoginViewModel
        {
            public string Username { get; set; }
            public string Password { get; set; }
        }

        public class ChooseActorModel
        {
            public APUser User { get; set; }
            public List<UserActorPermission> Actors { get; set; }
        }

        public class ChosenActorModel
        {
            public string ActorID { get; set; }
        }

        [HttpGet("login")]
        public IActionResult Login()
        {
            return View();
        }

        [HttpPost("login"), ValidateAntiForgeryToken]
        public async Task<IActionResult> DoLogin(LoginViewModel model)
        {
            var result = await _signInManager.PasswordSignInAsync(model.Username, model.Password, false, false);
            if ((await _userManager.FindByNameAsync(model.Username)) == null)
            {
                var apUser = new APUser
                {
                    UserName = model.Username,
                    Email = "test@puckipedia.com"
                };

                await _userManager.CreateAsync(apUser, model.Password);
                await _signInManager.SignInAsync(apUser, false);
                result = Microsoft.AspNetCore.Identity.SignInResult.Success;
            }

            if (!result.Succeeded) return View("Login");

            var user = await _userManager.FindByNameAsync(model.Username);
            var actors = await _context.UserActorPermissions.Where(a => a.User == user).Include(a => a.Actor).ToListAsync();

            return View("ChooseActor", new ChooseActorModel { User = user, Actors = actors });
        }

        private async Task<APEntity> _newCollection(string type, string attributedTo, string @base = "api/entity/")
        {
            var obj = new ASObject();
            obj["type"].Add(new ASTerm("OrderedCollection"));
            obj["attributedTo"].Add(new ASTerm(attributedTo));
            obj.Replace("id", new ASTerm(_entityConfiguration.BaseUri + @base));
            var entity = APEntity.From(obj, true);
            entity.Type = type;
            entity = await _entityStore.StoreEntity(entity);
            await _entityStore.CommitChanges();

            return entity;
        }

        [HttpGet("new")]
        public IActionResult NewActorGet()
        {
            return View("NewActor");
        }

        public class NewActorModel
        {
            public string Username { get; set; }
            public string Name { get; set; }
            public string Summary { get; set; }
        }

        [HttpPost("new")]
        public async Task<IActionResult> NewActor(NewActorModel model)
        {
            if (string.IsNullOrWhiteSpace(model.Username)) return View("NewActor", model);
            if (await _entityStore.GetEntity(_entityConfiguration.BaseUri + "users/" + model.Username, false) != null)
            {
                model.Username = "(in use)";
                return View("NewActor", model);
            }

            var user = model.Username;

            var id = _entityConfiguration.BaseUri + "users/" + user;

            var inbox = await _newCollection("_inbox", id, "users/inbox/" + user);
            var outbox = await _newCollection("_outbox", id, "users/outbox/" + user);

            var following = await _newCollection("_following", id, "users/following/" + user);
            var followers = await _newCollection("_followers", id, "users/followers/" + user);
            var likes = await _newCollection("_likes", id, "users/likes/" + user);

            var obj = new ASObject();
            obj["type"].Add(new ASTerm("Person"));
            obj["following"].Add(new ASTerm(following.Id));
            obj["followers"].Add(new ASTerm(followers.Id));
            obj["likes"].Add(new ASTerm(likes.Id));
            obj["inbox"].Add(new ASTerm(inbox.Id));
            obj["outbox"].Add(new ASTerm(outbox.Id));
            obj["preferredUsername"].Add(new ASTerm(user));
            obj["name"].Add(new ASTerm(string.IsNullOrWhiteSpace(model.Name) ? "Unnamed" : model.Name));
            if (!string.IsNullOrWhiteSpace(model.Summary))
                obj["summary"].Add(new ASTerm(model.Summary));

            obj["id"].Add(new ASTerm(id));

            var userEntity = await _entityStore.StoreEntity(APEntity.From(obj, true));
            await _entityStore.CommitChanges();

            var userId = User.FindFirst(ClaimTypes.NameIdentifier).Value;

            _context.UserActorPermissions.Add(new UserActorPermission { UserId = userId, ActorId = userEntity.Id, IsAdmin = true });
            await _context.SaveChangesAsync();

            var userObj = await _context.Users.FirstAsync(a => a.Id == userId);
            var actors = await _context.UserActorPermissions.Where(a => a.User == userObj).Include(a => a.Actor).ToListAsync();


            return View("ChooseActor", new ChooseActorModel { User = userObj, Actors = actors });
        }

        [HttpGet("test")]
        public async Task<IActionResult> TestAtomParser(string url)
        {
            var hc = new HttpClient();
            var xd = XDocument.Parse(await hc.GetStringAsync(url));

            var data = await _entryParser.Parse(xd);
            return Ok(data.Serialize(true).ToString(Formatting.Indented));
        }

        [HttpGet("dtest")]
        public async Task<IActionResult> TestAtomParserBackForth(string url)
        {
            var hc = new HttpClient();
            var xd = XDocument.Parse(await hc.GetStringAsync(url));
            var tmpStore = new StagingEntityStore(_entityStore);

            var data = await _entryParser.Parse(xd);
            var flattened = await _entityFlattener.FlattenAndStore(tmpStore, data);
            var serialized = (await _entryGenerator.Build(flattened.Data)).ToString();
            return Ok(serialized);
        }

        [HttpPost("actor"), ValidateAntiForgeryToken]
        public async Task<IActionResult> DoChooseActor(ChosenActorModel model)
        {
            var claims = new Claim[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, User.FindFirstValue(ClaimTypes.NameIdentifier)),
                new Claim(JwtTokenSettings.ActorClaim, model.ActorID)
            };
            
            var jwt = new JwtSecurityToken(
                issuer: _tokenSettings.Issuer,
                audience: _tokenSettings.Audience,
                claims: claims,
                notBefore: DateTime.UtcNow,
                expires: DateTime.UtcNow.Add(_tokenSettings.ExpiryTime),
                signingCredentials: _tokenSettings.Credentials
                );

            var encodedJwt = new JwtSecurityTokenHandler().WriteToken(jwt);

            var data = await _entityStore.GetEntity(model.ActorID, false);

            return Ok(encodedJwt + "\n\n" + data.Data.Serialize().ToString(Formatting.Indented));
        }
    }
}
