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
using Microsoft.AspNetCore.Authorization;
using Kroeg.Server.Salmon;
using Microsoft.Extensions.Configuration;

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
        private readonly IConfigurationRoot _configuration;

        public AuthController(APContext context, UserManager<APUser> userManager, SignInManager<APUser> signInManager, JwtTokenSettings tokenSettings, EntityFlattener entityFlattener, IEntityStore entityStore, AtomEntryParser entryParser, AtomEntryGenerator entryGenerator, EntityData entityConfiguration, IDataProtectionProvider dataProtectionProvider, IConfigurationRoot configuration)
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
            _configuration = configuration;
        }

        public class LoginViewModel
        {
            public string Username { get; set; }
            public string Password { get; set; }
            public string Redirect { get; set; }
        }

        public class RegisterViewModel
        {
            public string Username { get; set; }
            public string Password { get; set; }
            public string VerifyPassword { get; set; }
            public string Email { get; set; }
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
            public int Expiry { get; set; }
        }

        public class OAuthChosenActorModel
        {
            public string ActorID { get; set; }
            public string ResponseType { get; set; }
            public string RedirectUri { get; set; }
            public string State { get; set; }
            public int Expiry { get; set; }
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

            if (!result.Succeeded) return View("Login");
            if (!string.IsNullOrEmpty(model.Redirect)) return RedirectPermanent(model.Redirect);

            return RedirectToActionPermanent("Index", "Settings");
        }

        [HttpGet("register")]
        public IActionResult Register()
        {
            if (!_configuration.GetSection("Kroeg").GetValue<bool>("CanRegister")) return NotFound();
            return View(new RegisterViewModel { });
        }

        [HttpPost("register"), ValidateAntiForgeryToken]
        public async Task<IActionResult> DoREgister(RegisterViewModel model)
        {
            if (!_configuration.GetSection("Kroeg").GetValue<bool>("CanRegister")) return NotFound();
            var apuser = new APUser
            {
                UserName = model.Username,
                Email = model.Email
            };

            if (model.Password != model.VerifyPassword)
            {
                ModelState.AddModelError("", "Passwords don't match!");
            }

            if (!ModelState.IsValid) return View("Register", model);

            var result = await _userManager.CreateAsync(apuser, model.Password);
            if (!result.Succeeded)
            {
                ModelState.AddModelError("", result.Errors.First().Description);
            }

            if (!ModelState.IsValid) return View("Register", model);

            await _signInManager.SignInAsync(apuser, false);
            return RedirectToActionPermanent("Index", "Settings");
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

            return View("ChooseActorOAuth", new OAuthActorModel { User = user, Actors = actors, ResponseType = response_type, RedirectUri = redirect_uri, State = state, Expiry = (int) _tokenSettings.ExpiryTime.TotalSeconds});
        }

        [HttpPost("oauth"), ValidateAntiForgeryToken]
        public async Task<IActionResult> DoChooseActorOAuth(OAuthChosenActorModel model)
        {
            if (!ModelState.IsValid) return View("ChooseActorOAuth", model);
            var exp = TimeSpan.FromSeconds(model.Expiry);
            if (exp > _tokenSettings.ExpiryTime)
                exp = _tokenSettings.ExpiryTime;

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
                expires: DateTime.UtcNow.Add(exp),
                signingCredentials: _tokenSettings.Credentials
                );

            var encodedJwt = new JwtSecurityTokenHandler().WriteToken(jwt);

            if (model.ResponseType == "token")
            {
                if (model.RedirectUri.Contains("#"))
                    return RedirectPermanent(model.RedirectUri + $"&access_token={encodedJwt}&token_type=bearer&expires_in={(int) exp.TotalSeconds}&state={model.State}");
                else
                    return RedirectPermanent(model.RedirectUri + $"#access_token={encodedJwt}&token_type=bearer&expires_in={(int) exp.TotalSeconds}&state={model.State}");
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

        [HttpGet("test")]
        public async Task<IActionResult> TestAtomParser(string url)
        {
            var hc = new HttpClient();
            var xd = XDocument.Parse(await hc.GetStringAsync(url));

            var data = await _entryParser.Parse(xd, false, null);
            return Ok(data.Serialize(true).ToString(Formatting.Indented));
        }

        [HttpGet("get")]
        public async Task<IActionResult> GetId(string id)
        {
            var data = await _entityStore.GetEntity(id, true);
            var ddata = await _entityFlattener.Unflatten(_entityStore, data, 10);
            return Ok(ddata.Serialize(true).ToString(Formatting.Indented));
        }

        [HttpGet("getatom")]
        public async Task<IActionResult> GetIdAtom(string id)
        {
            var data = await _entityStore.GetEntity(id, true);
            var ser = await _entryGenerator.Build(data.Data);
            return Ok(ser.ToString(SaveOptions.None));
        }

        [HttpGet("dtest")]
        public async Task<IActionResult> TestAtomParserBackForth(string url)
        {
            var hc = new HttpClient();
            var xd = XDocument.Parse(await hc.GetStringAsync(url));
            var tmpStore = new StagingEntityStore(_entityStore);

            var data = await _entryParser.Parse(xd, false, null);
            var flattened = await _entityFlattener.FlattenAndStore(tmpStore, data);
            var serialized = (await _entryGenerator.Build(flattened.Data)).ToString();
            return Ok(serialized);
        }

        [HttpGet("actor"), Authorize]
        public async Task<IActionResult> ChooseActor()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier).Value;

            var userObj = await _context.Users.FirstAsync(a => a.Id == userId);
            var actors = await _context.UserActorPermissions.Where(a => a.User == userObj).Include(a => a.Actor).ToListAsync();

            return View(new ChooseActorModel { Actors = actors, User = userObj });
        }

        [HttpPost("actor"), ValidateAntiForgeryToken, Authorize]
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
