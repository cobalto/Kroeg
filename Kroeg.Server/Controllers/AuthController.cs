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
using Microsoft.AspNetCore.DataProtection;
using System.Security.Cryptography;

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
        private readonly IDataProtector _dataProtector;

        public AuthController(APContext context, UserManager<APUser> userManager, SignInManager<APUser> signInManager, JwtTokenSettings tokenSettings, EntityFlattener entityFlattener, IEntityStore entityStore, AtomEntryParser entryParser, AtomEntryGenerator entryGenerator, EntityData entityConfiguration, IDataProtectionProvider dataProtectionProvider)
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
            _dataProtector = dataProtectionProvider.CreateProtector("OAuth tokens");
        }

        public class LoginViewModel
        {
            public string Username { get; set; }
            public string Password { get; set; }
            public string Redirect { get; set; }
        }

        public class ChooseActorModel
        {
            public APUser User { get; set; }
            public List<UserActorPermission> Actors { get; set; }
        }

        public class OAuthActorModel
        {
            public APUser User { get; set; }
            public List<UserActorPermission> Actors { get; set; }
            public string ResponseType { get; set; }
            public string RedirectUri { get; set; }
            public string State { get; set; }
        }

        public class OAuthChosenActorModel
        {
            public string ActorID { get; set; }
            public string ResponseType { get; set; }
            public string RedirectUri { get; set; }
            public string State { get; set; }
        }

        public class ChosenActorModel
        {
            public string ActorID { get; set; }
        }

        [HttpGet("login")]
        public IActionResult Login(string redirect)
        {
            return View(new LoginViewModel { Redirect = redirect });
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
            if (!string.IsNullOrEmpty(model.Redirect)) return RedirectPermanent(model.Redirect);

            var user = await _userManager.FindByNameAsync(model.Username);
            var actors = await _context.UserActorPermissions.Where(a => a.User == user).Include(a => a.Actor).ToListAsync();

            return View("ChooseActor", new ChooseActorModel { User = user, Actors = actors });
        }
        
        private string _appendToUri(string uri, string query)
        {
            var builder = new UriBuilder(uri);
            if (builder.Query?.Length > 1)
                builder.Query = builder.Query.Substring(1) + "&" + query;
            else
                builder.Query = query;
            
            return builder.ToString();
        }

        [HttpGet("oauth")]
        public async Task<IActionResult> DoOAuthToken(string response_type, string redirect_uri, string state)
        {
            if (response_type != "token" && response_type != "code") return RedirectPermanent(_appendToUri(redirect_uri, "error=unsupported_response_type"));
            if (User == null || User.FindFirstValue(ClaimTypes.NameIdentifier) == null) return RedirectToAction("Login", new { redirect = Request.Path.Value + Request.QueryString });

            var user = await _userManager.FindByIdAsync(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var actors = await _context.UserActorPermissions.Where(a => a.User == user).Include(a => a.Actor).ToListAsync();

            return View("ChooseActorOAuth", new OAuthActorModel { User = user, Actors = actors, ResponseType = response_type, RedirectUri = redirect_uri, State = state });
        }

        [HttpPost("oauth"), ValidateAntiForgeryToken]
        public async Task<IActionResult> DoChooseActorOAuth(OAuthChosenActorModel model)
        {
            if (!ModelState.IsValid) return View("ChooseActorOAuth", model);

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

            if (model.ResponseType == "token")
            {
                if (model.RedirectUri.Contains("#"))
                    return RedirectPermanent(model.RedirectUri + $"&access_token={encodedJwt}&token_type=bearer&expires_in={(int) _tokenSettings.ExpiryTime.TotalSeconds}&state={model.State}");
                else
                    return RedirectPermanent(model.RedirectUri + $"#access_token={encodedJwt}&token_type=bearer&expires_in={(int) _tokenSettings.ExpiryTime.TotalSeconds}&state={model.State}");
            }
            else if (model.ResponseType == "code")
            {
                encodedJwt = _dataProtector.Protect(encodedJwt);

                return RedirectPermanent(_appendToUri(model.RedirectUri, $"code={Uri.EscapeDataString(encodedJwt)}&state={model.State}"));
            }

            return StatusCode(500);
        }

        public class OAuthTokenModel
        {
            public string grant_type { get; set; }
            public string code { get; set; }
            public string redirect_uri { get; set; }
            public string client_id { get; set; }
        }

        public class JsonError {
            public string error { get; set; }
        }

        public class JsonResponse
        {
            public string access_token { get; set; }
            public string token_type { get; set; }
            public int expires_in { get; set; }
        }

        [HttpPost("token")]
        public async Task<IActionResult> OAuthToken(OAuthTokenModel model)
        {
            if (model.grant_type != "authorization_code") return Json(new JsonError { error = "invalid_request" });
            try
            {
                var decrypted = _dataProtector.Unprotect(model.code);
                return Json(new JsonResponse {
                    access_token = decrypted,
                    expires_in = (int) _tokenSettings.ExpiryTime.TotalSeconds,
                    token_type = "bearer"
                });
            }
            catch (CryptographicException)
            {
                return Json(new JsonError { error = "invalid_request" });
            }
        }

        private async Task<APEntity> _newCollection(string type, string attributedTo)
        {
            var obj = new ASObject();
            obj["type"].Add(new ASTerm("OrderedCollection"));
            obj["attributedTo"].Add(new ASTerm(attributedTo));
            obj.Replace("id", new ASTerm(_entityConfiguration.UriFor(obj, type, attributedTo)));
            var entity = APEntity.From(obj, true);
            entity.Type = "_" + type;
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

            var obj = new ASObject();
            obj["type"].Add(new ASTerm("Person"));
            obj["preferredUsername"].Add(new ASTerm(user));
            obj["name"].Add(new ASTerm(string.IsNullOrWhiteSpace(model.Name) ? "Unnamed" : model.Name));
            if (!string.IsNullOrWhiteSpace(model.Summary))
                obj["summary"].Add(new ASTerm(model.Summary));

            var id = _entityConfiguration.UriFor(obj);
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
