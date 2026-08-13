namespace FashionStore.Application.Authorization;

/// <summary>
/// Policies guarding the website settings surface. <c>Settings.Manage</c> allows
/// an authenticated administrator to read and update ordinary settings; protected
/// settings (currency, timezone, maintenance mode) additionally require the
/// SuperAdmin role and are enforced inside the settings service.
/// </summary>
public static class SettingsPolicies
{
    public const string SettingsManage = "Settings.Manage";
}
