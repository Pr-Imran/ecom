using System.Text.Encodings.Web;
using FashionStore.Application.Email;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;

namespace FashionStore.Infrastructure.Emails;

/// <summary>
/// Renders an email Razor view from <c>Views/Emails</c> into a fully formatted
/// HTML string wrapped in the responsive layout. Uses a synthesized
/// <see cref="ActionContext"/> (no HTTP request required) so rendering works both
/// in request pipelines and inside Hangfire background jobs.
/// </summary>
public sealed class RazorEmailTemplateRenderer : IEmailTemplateRenderer
{
    private const string TemplateRoot = "~/Views/Emails/";

    private readonly IRazorViewEngine _viewEngine;
    private readonly ITempDataProvider _tempDataProvider;
    private readonly IServiceProvider _serviceProvider;

    public RazorEmailTemplateRenderer(
        IRazorViewEngine viewEngine,
        ITempDataProvider tempDataProvider,
        IServiceProvider serviceProvider)
    {
        _viewEngine = viewEngine;
        _tempDataProvider = tempDataProvider;
        _serviceProvider = serviceProvider;
    }

    public async Task<string> RenderAsync<TModel>(string templateName, TModel model, CancellationToken cancellationToken = default)
        where TModel : EmailTemplateModel
    {
        cancellationToken.ThrowIfCancellationRequested();

        var actionContext = GetActionContext();
        var view = FindView(actionContext, templateName);
        var viewData = new ViewDataDictionary<TModel>(new EmptyModelMetadataProvider(), new ModelStateDictionary())
        {
            Model = model
        };

        await using var output = new StringWriter();
        var viewContext = new ViewContext(
            actionContext,
            view,
            viewData,
            new TempDataDictionary(actionContext.HttpContext, _tempDataProvider),
            output,
            new HtmlHelperOptions());

        await view.RenderAsync(viewContext);
        return output.ToString();
    }

    private IView FindView(ActionContext actionContext, string templateName)
    {
        var viewName = templateName.StartsWith("~/", StringComparison.Ordinal)
            ? templateName
            : $"{TemplateRoot}{templateName}.cshtml";

        var result = _viewEngine.GetView(executingFilePath: null, viewPath: viewName, isMainPage: true);
        if (result.Success)
        {
            return result.View;
        }

        var searched = string.Join(", ", result.SearchedLocations);
        throw new InvalidOperationException($"Email template '{templateName}' was not found. Searched: {searched}");
    }

    private ActionContext GetActionContext()
    {
        var httpContext = new DefaultHttpContext
        {
            RequestServices = _serviceProvider,
            Response = { Body = Stream.Null }
        };

        return new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
    }
}
