using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace IdentityLab.Mvc.Services;

// Uses the same Razor engine as the web pages, but writes the result to a string instead of the HTTP response.
// It doesn't depend on the current request, so it also works outside a controller.
public sealed class RazorViewRenderer(
    IRazorViewEngine viewEngine,
    ITempDataProvider tempDataProvider,
    IServiceProvider serviceProvider) : IRazorViewRenderer
{
    public async Task<string> RenderAsync<TModel>(string viewPath, TModel model)
    {
        var httpContext = new DefaultHttpContext { RequestServices = serviceProvider };
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());

        var viewResult = viewEngine.GetView(executingFilePath: null, viewPath: viewPath, isMainPage: true);
        if (!viewResult.Success)
        {
            throw new InvalidOperationException($"The view '{viewPath}' was not found.");
        }

        var viewData = new ViewDataDictionary<TModel>(new EmptyModelMetadataProvider(), new ModelStateDictionary())
        {
            Model = model
        };

        await using var writer = new StringWriter();
        var viewContext = new ViewContext(
            actionContext,
            viewResult.View,
            viewData,
            new TempDataDictionary(httpContext, tempDataProvider),
            writer,
            new HtmlHelperOptions());

        await viewResult.View.RenderAsync(viewContext);
        return writer.ToString();
    }
}
