using System.Threading.Tasks;
using Kroeg.Server.Models;
using Kroeg.Server.Services.EntityStore;
using Microsoft.AspNetCore.Mvc;

namespace Kroeg.Server.Controllers
{
    [Route("/render")]
    public class RenderController : Controller
    {
        private readonly IEntityStore _entityStore;

        public RenderController(IEntityStore entityStore)
        {
            _entityStore = entityStore;
        }

        [HttpGet("remote")]
        public async Task<IActionResult> RenderRemote(string url)
        {
            if (url == null)
                return RedirectPermanent("/");

            var entity = await _entityStore.GetEntity(url, true);

            return View("Generic", entity);
        }

        [HttpGet("")]
        public IActionResult Render()
        {
            if (!HttpContext.Items.ContainsKey("object"))
                return RedirectPermanent("/");

            var obj = (APEntity) HttpContext.Items["object"];

            return View("Generic", obj);
        }
    }
}
