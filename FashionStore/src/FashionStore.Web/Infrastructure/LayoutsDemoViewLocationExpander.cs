using Microsoft.AspNetCore.Mvc.Razor;

namespace FashionStore.Web.Infrastructure;

/// <summary>
/// Maps the <c>LayoutsDemo</c> controller to the <c>Demo</c> view folder so the
/// shared layout demos stay grouped together under Views/Demo.
/// </summary>
public sealed class LayoutsDemoViewLocationExpander : IViewLocationExpander
{
    public void PopulateValues(ViewLocationExpanderContext context)
    {
    }

    public IEnumerable<string> ExpandViewLocations(
        ViewLocationExpanderContext context,
        IEnumerable<string> viewLocations)
    {
        if (string.Equals(context.ControllerName, "LayoutsDemo", StringComparison.Ordinal))
        {
            foreach (var location in viewLocations)
            {
                yield return location.Replace("{1}", "Demo", StringComparison.Ordinal);
            }
        }

        foreach (var location in viewLocations)
        {
            yield return location;
        }
    }
}
