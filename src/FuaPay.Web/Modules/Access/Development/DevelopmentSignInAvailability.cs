namespace FuaPay.Web.Modules.Access.Development;

public sealed class DevelopmentSignInAvailability
{
    public DevelopmentSignInAvailability(bool isEnabled)
    {
        IsEnabled = isEnabled;
    }

    public bool IsEnabled { get; }
}
