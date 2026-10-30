using Erp.Application.Abstractions;
using Erp.Finance.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Finance.Infrastructure;

public static class Registration
{
    public static IServiceCollection AddFinanceFoundation(this IServiceCollection services)
    {
        services.AddSingleton(new ModuleDescriptor("Finance", "finance", true));
        services.AddSingleton<IJournalSessions, JournalSessions>();
        services.AddSingleton<IJournalQueries, JournalQueries>();
        services.AddSingleton<JournalCommands>();
        services.AddSingleton<ReverseJournalCommand>();
        services.AddSingleton<CorrectJournalCommand>();
        services.AddSingleton<IPeriodStore, FiscalPeriodStore>();
        services.AddSingleton<FiscalPeriodQueries>();
        services.AddSingleton<CloseFiscalPeriodCommand>();
        services.AddSingleton<CreateFiscalPeriodCommand>();
        services.AddSingleton<IAccountSetupStore, AccountSetupStore>();
        services.AddSingleton<AccountSetupQueries>();
        services.AddSingleton<CreateAccountCommand>();
        services.AddSingleton<IAccountingBookSetupStore, AccountingBookSetupStore>();
        services.AddSingleton<AccountingBookSetupQueries>();
        services.AddSingleton<CreateAccountingBookCommand>();
        services.AddSingleton<IGeneralLedgerReader, GeneralLedgerReader>();
        services.AddSingleton<GeneralLedgerQuery>();
        services.AddSingleton<ITrialBalanceReader, TrialBalanceReader>();
        services.AddSingleton<ITrialBalanceQuery, TrialBalanceQuery>();
        services.AddSingleton<IIntegrationEventHandler, JournalPostedTraceHandler>();
        services.AddSingleton<IIntegrationEventHandler, JournalReversedTraceHandler>();
        services.AddSingleton<IIntegrationEventHandler, JournalCorrectedTraceHandler>();
        services.AddSingleton<IIntegrationEventHandler, FiscalPeriodClosedTraceHandler>();
        services.AddSingleton<IIntegrationEventHandler, FiscalPeriodCreatedTraceHandler>();
        services.AddSingleton<IIntegrationEventHandler, AccountCreatedTraceHandler>();
        services.AddSingleton<IIntegrationEventHandler, AccountingBookCreatedTraceHandler>();
        services.AddSingleton<FinanceDatabaseReadiness>();
        services.AddHostedService<FinanceRuntimeGuard>();
        services.AddHealthChecks().AddCheck<FinanceDatabaseReadiness>("finance-postgres", tags: ["ready"]);
        return services;
    }
}
