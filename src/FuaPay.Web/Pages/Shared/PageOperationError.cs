using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Access.Domain;
using FuaPay.Web.Modules.Credits.Application;
using FuaPay.Web.Modules.Credits.Domain;
using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Modules.Jobs.Domain;
using FuaPay.Web.Modules.Payments.Application;
using FuaPay.Web.Modules.Payments.Domain;
using FuaPay.Web.Modules.ServiceUnits.Application;
using FuaPay.Web.Modules.ServiceUnits.Domain;

using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FuaPay.Web.Pages.Shared;

public static class PageOperationError
{
    public static bool IsExpected(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception is
            AccessUserNotFoundException or
            ProtectedCustomerRoleException or
            LastAdministratorProtectionException or
            SelfBlockNotAllowedException or
            AccessUserBlockedException or
            AccessUserConcurrencyException or
            AccessIdentityConcurrencyException or
            ExternalIdentityAlreadyAssignedException or
            ExternalIdentityProviderAlreadyAssignedException or
            DuplicateAccessRoleException or
            AccessRoleNotAssignedException or
            CreditAccountNotFoundException or
            CreditAccountConcurrencyException or
            CreditAdjustmentCommandAlreadyExistsException or
            CreditAdjustmentCommandConflictException or
            CreditAdjustmentAmountNotAllowedException or
            CreditAdjustmentReasonNotAllowedException or
            InsufficientCreditException or
            DuplicateCreditOperationException or
            JobNotFoundException or
            JobAccessDeniedException or
            JobPaymentAccessDeniedException or
            JobPaymentInProgressException or
            JobConcurrencyException or
            JobSettlementReferenceAlreadyUsedException or
            JobNumberAlreadyUsedException or
            JobServiceUnitUnavailableException or
            JobServiceUnitAccessDeniedException or
            JobCustomerUnavailableException or
            JobPriceNotAllowedException or
            ServiceTypeMismatchException or
            InvalidJobStateTransitionException or
            JobSettlementRequiredException or
            JobSettlementNotAllowedException or
            JobSettlementConflictException or
            JobCannotBeCancelledAfterSettlementException or
            PaymentNotFoundException or
            PaymentAccessDeniedException or
            PaymentConcurrencyException or
            PaymentCreationRequestAlreadyExistsException or
            PaymentCreationRequestConflictException or
            PaymentAmountNotAllowedException or
            PaymentProviderUnavailableException or
            BlockingJobPaymentAlreadyExistsException or
            InvalidPaymentStateTransitionException or
            ServiceUnitNotFoundException or
            ServiceUnitCodeAlreadyUsedException or
            ServiceUnitConcurrencyException or
            RequesterAssignmentNotFoundException or
            RequesterAssignmentConcurrencyException or
            RequesterRoleRequiredException or
            InactiveServiceUnitException or
            RequesterAlreadyAssignedException;
    }

    public static void Add(
        PageModel pageModel,
        Exception exception,
        string operation,
        string fallbackMessage)
    {
        ArgumentNullException.ThrowIfNull(pageModel);
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(fallbackMessage);

        var loggerFactory = pageModel.HttpContext.RequestServices
            .GetRequiredService<ILoggerFactory>();

        var logger = loggerFactory.CreateLogger(
            pageModel.GetType().FullName ?? pageModel.GetType().Name);

        logger.LogWarning(
            "Aplikační operace {Operation} byla odmítnuta. " +
            "Typ: {ExceptionType}. Trace ID: {TraceIdentifier}.",
            operation,
            exception.GetType().FullName,
            pageModel.HttpContext.TraceIdentifier);

        pageModel.ModelState.AddModelError(
            string.Empty,
            GetSafeMessage(exception) ?? fallbackMessage);
    }

    private static string? GetSafeMessage(Exception exception)
    {
        return exception switch
        {
            ProtectedCustomerRoleException =>
                "Základní roli zákazníka nelze odebrat.",
            LastAdministratorProtectionException =>
                "Poslední aktivní administrátor musí zůstat zachován.",
            SelfBlockNotAllowedException =>
                "Vlastní administrátorský účet nelze zablokovat.",
            DuplicateAccessRoleException =>
                "Uživatel už tuto roli má.",
            AccessRoleNotAssignedException =>
                "Uživatel tuto aktivní roli nemá.",
            AccessUserNotFoundException =>
                "Uživatel už není dostupný.",
            AccessUserBlockedException =>
                "Uživatelský účet je zablokovaný.",
            ExternalIdentityAlreadyAssignedException =>
                "Tato Entra identita už je propojena s jiným účtem FUA Pay.",
            ExternalIdentityProviderAlreadyAssignedException =>
                "Účet už má propojenou jinou identitu tohoto Entra tenantu.",
            RequesterRoleRequiredException =>
                "Pracoviště lze přiřadit pouze uživateli s aktivní rolí zadavatele.",
            ServiceUnitCodeAlreadyUsedException =>
                "Zadaný kód už používá jiné pracoviště.",
            RequesterAlreadyAssignedException =>
                "Uživatel už je k tomuto pracovišti přiřazen.",
            RequesterAssignmentNotFoundException =>
                "Aktivní přiřazení pracoviště už neexistuje.",
            InactiveServiceUnitException =>
                "Neaktivní pracoviště nelze použít.",
            JobServiceUnitUnavailableException =>
                "Vybrané pracoviště není dostupné.",
            JobServiceUnitAccessDeniedException =>
                "K vybranému pracovišti nemáte oprávnění.",
            JobCustomerUnavailableException =>
                "Vybraný zákazník není dostupný.",
            ServiceTypeMismatchException =>
                "Typ služby neodpovídá vybranému pracovišti.",
            InsufficientCreditException =>
                "Na účtu není dostatek kreditu.",
            CreditAdjustmentCommandConflictException =>
                "Tento příkaz korekce byl už použit s jinými daty. Obnovte stránku.",
            CreditAdjustmentAmountNotAllowedException =>
                "Částka korekce je mimo povolený rozsah.",
            CreditAdjustmentReasonNotAllowedException =>
                "Důvod korekce není platný.",
            PaymentCreationRequestConflictException =>
                "Tento požadavek na dobití byl už použit s jinými daty. Obnovte stránku.",
            PaymentAmountNotAllowedException =>
                "Částka platby je mimo povolený rozsah.",
            PaymentProviderUnavailableException =>
                "Platební služba nyní není dostupná.",
            JobPriceNotAllowedException =>
                "Cena zakázky je mimo povolený rozsah.",
            BlockingJobPaymentAlreadyExistsException or
            JobPaymentInProgressException =>
                "Pro zakázku už existuje otevřený platební pokus.",
            JobPaymentAccessDeniedException or PaymentAccessDeniedException =>
                "Tuto platební operaci nelze provést.",
            AccessUserConcurrencyException or
            AccessIdentityConcurrencyException or
            CreditAccountConcurrencyException or
            CreditAdjustmentCommandAlreadyExistsException or
            JobConcurrencyException or
            PaymentConcurrencyException or
            PaymentCreationRequestAlreadyExistsException or
            ServiceUnitConcurrencyException or
            RequesterAssignmentConcurrencyException =>
                "Záznam byl mezitím změněn. Obnovte stránku a zkuste to znovu.",
            _ => null
        };
    }
}
