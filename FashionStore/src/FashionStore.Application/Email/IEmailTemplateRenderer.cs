namespace FashionStore.Application.Email;

/// <summary>
/// Renders an email Razor template (in <c>Views/Emails</c>) to a fully formatted
/// HTML string wrapped in the responsive email layout. Rendering is pure
/// in-memory and safe to call from background jobs.
/// </summary>
public interface IEmailTemplateRenderer
{
    Task<string> RenderAsync<TModel>(string templateName, TModel model, CancellationToken cancellationToken = default)
        where TModel : EmailTemplateModel;
}
