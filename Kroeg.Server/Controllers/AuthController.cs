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
using Kroeg.Server.Services;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json.Linq;
using Kroeg.Server.BackgroundTasks;
using System.IO;

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
        private readonly DeliveryService _deliveryService;
        private readonly SignatureVerifier _verifier;

        public AuthController(APContext context, UserManager<APUser> userManager, SignInManager<APUser> signInManager, JwtTokenSettings tokenSettings, EntityFlattener entityFlattener, IEntityStore entityStore, AtomEntryParser entryParser, AtomEntryGenerator entryGenerator, EntityData entityConfiguration, IDataProtectionProvider dataProtectionProvider, IConfigurationRoot configuration, DeliveryService deliveryService, SignatureVerifier verifier)
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
            _deliveryService = deliveryService;
            _verifier = verifier;
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

        public class OAuthActorModel
        {
            public APEntity Actor { get; set; }
            public string ActorID { get; set; }
            public string ResponseType { get; set; }
            public string RedirectUri { get; set; }
            public string State { get; set; }
            public int Expiry { get; set; }
            public string Deny { get; set; }
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

        [HttpGet("logout")]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            return RedirectToAction(nameof(Login));
        }

        [HttpPost("proxy")]
        public async Task<IActionResult> Proxy(string id)
        {
            if (User.FindFirstValue(JwtTokenSettings.ActorClaim) == null) return Unauthorized();

            var entity = await _entityStore.GetEntity(id, true);
            if (entity == null) return NotFound();

            var unflattened = await _entityFlattener.Unflatten(_entityStore, entity);
            return Content(unflattened.Serialize().ToString(), "application/ld+json; profile=\"https://www.w3.org/ns/activitystreams\"");
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
        public async Task<IActionResult> DoRegister(RegisterViewModel model)
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

            if (await _context.Users.CountAsync() == 1)
            {
                await _userManager.AddClaimAsync(apuser, new Claim("admin", "true"));
                await _context.SaveChangesAsync();
            }

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

        [HttpPost("sharedInbox")]
        public async Task<IActionResult> SharedInbox()
        {
            var userId = await _verifier.Verify(Request.Scheme + "://" + Request.Host.ToUriComponent() + Request.Path, HttpContext);
            if (userId == null) return Unauthorized();
            var reader = new StreamReader(Request.Body);
            var data = ASObject.Parse(await reader.ReadToEndAsync());

            if (!_entityConfiguration.IsActivity(data)) return StatusCode(403, "Not an activity?");
            if (!data["actor"].Any((a) => (string)a.Primitive == userId)) return StatusCode(403, "Invalid signature!");

            var temporaryStore = new StagingEntityStore(_entityStore);
            var resultEntity = await _entityFlattener.FlattenAndStore(temporaryStore, data, false);
            temporaryStore.TrimDown((new Uri(new Uri(userId), "/")).ToString());
            await temporaryStore.CommitChanges(); // shouuuuld be safe

            var users = await _deliveryService.GetUsersForSharedInbox(data);

            foreach (var user in users)
            {
                if (user.IsOwner)
                    _context.EventQueue.Add(DeliverToActivityPubTask.Make(new DeliverToActivityPubData { ObjectId = resultEntity.Id, TargetInbox = (string) user.Data["inbox"].First().Primitive }));
            }

            await _context.SaveChangesAsync();

            return StatusCode(202);
        }

        [HttpGet("oauth")]
        public async Task<IActionResult> DoOAuthToken(string id, string response_type, string redirect_uri, string state)
        {
            if (response_type != "token" && response_type != "code") return RedirectPermanent(_appendToUri(redirect_uri, "error=unsupported_response_type"));
            if (User == null || User.FindFirstValue(ClaimTypes.NameIdentifier) == null) return RedirectToAction("Login", new { redirect = Request.Path.Value + Request.QueryString });

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var actor = await _entityStore.GetEntity(id, false);
            var hasAccess = await _context.UserActorPermissions.AnyAsync(a => a.UserId == userId && a.ActorId == id);
            if (!hasAccess || actor == null || !actor.IsOwner)
            {
                if (response_type == "token")
                    if (redirect_uri.Contains("#"))
                        return RedirectPermanent(redirect_uri + "&error=access_denied&state=" + Uri.EscapeDataString(state));
                    else
                        return RedirectPermanent(redirect_uri + "#error=access_denied&state=" + Uri.EscapeDataString(state));
                else
                    return RedirectPermanent(_appendToUri(redirect_uri, "error=access_denied&state=" + Uri.EscapeDataString(state)));
            }

            return View("ChooseActorOAuth", new OAuthActorModel { Actor = actor, ResponseType = response_type, RedirectUri = redirect_uri, State = state, Expiry = (int) _tokenSettings.ExpiryTime.TotalSeconds});
        }

        [HttpPost("oauth"), ValidateAntiForgeryToken]
        public async Task<IActionResult> DoChooseActorOAuth(OAuthActorModel model)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var actor = await _entityStore.GetEntity(model.ActorID, false);
            var hasAccess = await _context.UserActorPermissions.AnyAsync(a => a.UserId == userId && a.ActorId == model.ActorID);
            model.Actor = actor;
            if (!hasAccess || !ModelState.IsValid) return View("ChooseActorOAuth", model);
            var exp = TimeSpan.FromSeconds(model.Expiry);
            if (exp > _tokenSettings.ExpiryTime)
                exp = _tokenSettings.ExpiryTime;

            if (!string.IsNullOrWhiteSpace(model.Deny))
                if (model.ResponseType == "token")
                    if (model.RedirectUri.Contains("#"))
                        return RedirectPermanent(model.RedirectUri + "&error=access_denied&state=" + Uri.EscapeDataString(model.State));
                    else
                        return RedirectPermanent(model.RedirectUri + "#error=access_denied&state=" + Uri.EscapeDataString(model.State));
                else
                    return RedirectPermanent(_appendToUri(model.RedirectUri, "error=access_denied&state=" + Uri.EscapeDataString(model.State)));

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
                    return RedirectPermanent(model.RedirectUri + $"&access_token={encodedJwt}&token_type=bearer&expires_in={(int) exp.TotalSeconds}&state={Uri.EscapeDataString(model.State)}");
                else
                    return RedirectPermanent(model.RedirectUri + $"#access_token={encodedJwt}&token_type=bearer&expires_in={(int) exp.TotalSeconds}&state={Uri.EscapeDataString(model.State)}");
            }
            else if (model.ResponseType == "code")
            {
                encodedJwt = _dataProtector.Protect(encodedJwt);

                return RedirectPermanent(_appendToUri(model.RedirectUri, $"code={Uri.EscapeDataString(encodedJwt)}&state={Uri.EscapeDataString(model.State)}"));
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

        public class JsonError
        {
            public string error { get; set; }
        }

        public class JsonResponse
        {
            public string access_token { get; set; }
            public string token_type { get; set; }
            public int expires_in { get; set; }
        }

        [HttpPost("token")]
        public IActionResult OAuthToken(OAuthTokenModel model)
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

        private class _jwkKeyset
        {
            [JsonProperty("keys")]
            public List<JObject> Keys { get; set; }
        }

        [HttpGet("jwks")]
        public async Task<IActionResult> GetJsonWebKeys(string id)
        {
            var actor = await _entityStore.GetEntity(id, false);
            if (actor == null || !actor.IsOwner) return NotFound();
            var key = await _verifier.GetJWK(actor);
            var deser = key.Key;
            deser.D = null;

            var jo = JObject.FromObject(deser);
            foreach (var ancestor in jo.Properties().ToList())
            {
                if (ancestor.Name.ToLower() != ancestor.Name) ancestor.Remove();
            }


            return Content(JsonConvert.SerializeObject(new _jwkKeyset { Keys = new List<JObject> { jo } }), "application/json");
        }
    }
}
