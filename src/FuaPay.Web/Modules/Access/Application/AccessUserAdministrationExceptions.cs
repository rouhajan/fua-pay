using FuaPay.Web.Modules.Access.Domain;

namespace FuaPay.Web.Modules.Access.Application;

public sealed class AccessUserNotFoundException :
    InvalidOperationException
{
    public AccessUserNotFoundException(Guid userId)
        : base($"Uživatel '{userId}' nebyl nalezen.")
    {
        UserId = userId;
    }

    public Guid UserId { get; }
}

public sealed class ProtectedCustomerRoleException :
    InvalidOperationException
{
    public ProtectedCustomerRoleException()
        : base("Základní roli zákazníka nelze ručně změnit.")
    {
    }
}

public sealed class LastAdministratorProtectionException :
    InvalidOperationException
{
    public LastAdministratorProtectionException()
        : base("Posledního aktivního administrátora nelze odebrat ani zablokovat.")
    {
    }
}

public sealed class SelfBlockNotAllowedException :
    InvalidOperationException
{
    public SelfBlockNotAllowedException()
        : base("Administrátor nemůže zablokovat vlastní účet.")
    {
    }
}
