using System.Threading.Tasks;
using Kroeg.Server.Models;
using Kroeg.Server.Services.EntityStore;
using Microsoft.AspNetCore.Mvc;
using Kroeg.Server.Services.Template;

namespace Kroeg.Server.Controllers
{
    [Route("/render")]
    public class RenderController : Controller
    {
        private readonly IEntityStore _entityStore;
        private readonly TemplateService _templateService;

        public RenderController(IEntityStore entityStore, TemplateService templateService)
        {
            _entityStore = entityStore;
            _templateService = templateService;
        }

        [HttpGet("remote")]
        public async Task<IActionResult> RenderRemote(string url)
        {
            if (url == null)
                return RedirectPermanent("/");

            var entity = await _entityStore.GetEntity(url, true);

            return Content(await _templateService.ParseTemplate("page", _entityStore, entity), "text/html");
        }

        [HttpGet("")]
        public async Task<IActionResult> Render()
        {
            if (!HttpContext.Items.ContainsKey("object"))
                return RedirectPermanent("/");

            var obj = (APEntity) HttpContext.Items["object"];

            return Content(await _templateService.ParseTemplate("page", _entityStore, obj), "text/html");
        }
    }
}
