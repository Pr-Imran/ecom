namespace FashionStore.Application.Authorization;

/// <summary>
/// Policies guarding the email administration surface (log search/filter, resend
/// and template preview). Both viewing and resending are treated as a single
/// capability because resending an email is a low-risk, reversible operation that
/// administrators already use for invoices.
/// </summary>
public static class EmailPolicies
{
    public const string EmailsManage = "Emails.Manage";
}
