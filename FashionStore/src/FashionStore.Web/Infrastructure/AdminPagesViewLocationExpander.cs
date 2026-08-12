using Microsoft.AspNetCore.Mvc.Razor;

namespace FashionStore.Web.Infrastructure;

public sealed class AdminPagesViewLocationExpander : IViewLocationExpander
{
    public void PopulateValues(ViewLocationExpanderContext context)
    {
    }

    public IEnumerable<string> ExpandViewLocations(
        ViewLocationExpanderContext context,
        IEnumerable<string> viewLocations)
    {
        if (string.Equals(context.ControllerName, "AdminPages", StringComparison.Ordinal))
        {
            foreach (var location in viewLocations)
            {
                yield return location.Replace("{1}", "Admin", StringComparison.Ordinal);
            }
        }

        foreach (var location in viewLocations)
        {
            yield return location;
        }
    }
}
