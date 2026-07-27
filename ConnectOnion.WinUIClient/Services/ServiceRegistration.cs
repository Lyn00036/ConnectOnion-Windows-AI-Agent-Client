using System;
using System.Net.Http;
using ConnectOnion.WinUIClient.Data;
using ConnectOnion.WinUIClient.Services.Attachments;
using ConnectOnion.WinUIClient.Services.Notifications;
using ConnectOnion.WinUIClient.Services.Runtime;
using ConnectOnion.WinUIClient.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace ConnectOnion.WinUIClient.Services;

/// <summary>
/// The application's composition root. Every process-wide service and every
/// view model is registered here, then resolved from the
/// <see cref="System.IServiceProvider"/> the Generic Host builds
/// (see <c>App</c>).
///
/// Declaration order is irrelevant — unlike the old hand-rolled locator, the
/// container resolves the dependency graph on demand — but the lifetime is not:
/// the services below are the app's shared singletons, and the view models are
/// transient so each page gets a fresh one.
///
/// A page's <c>Cleanup</c> disposes its subscriptions; a new navigation resolves
/// a new instance, matching the previous <c>new XyzViewModel()</c> per page.
/// </summary>
public static class ServiceRegistration
{
    /// <summary>
    /// Name of the resilient HttpClient every agent HTTP call flows through.
    /// </summary>
    private const string AgentHttpClient = "agent";

    public static IServiceCollection AddAppServices(
        this IServiceCollection services)
    {
        // ---- Shared infrastructure ----
        //
        // Every agent HTTP call funnels through one shared HttpClient:
        // - direct /info probes
        // - relay lookup through EndpointResolver
        // - agent-image downloads
        //
        // Giving the client a Polly resilience handler makes all of those
        // operations retry transient failures with jittered exponential
        // backoff and bounds each attempt.
        //
        // Deliberately no circuit breaker: presence probes must keep trying
        // a flaky agent rather than latch it offline until a breaker
        // half-opens, and callers already impose their own overall timeouts.
        services
            .AddHttpClient(AgentHttpClient)
            .AddResilienceHandler(
                "agent-http",
                pipeline =>
                {
                    pipeline
                        .AddRetry(
                            new HttpRetryStrategyOptions
                            {
                                MaxRetryAttempts = 2,
                                Delay =
                                    TimeSpan.FromMilliseconds(300),
                                BackoffType =
                                    DelayBackoffType.Exponential,
                                UseJitter = true,
                            })
                        .AddTimeout(
                            TimeSpan.FromSeconds(10));
                });

        // Keep the app's single shared client, now backed by the resilient
        // handler chain created for the named client above.
        services.AddSingleton<HttpClient>(
            serviceProvider =>
                serviceProvider
                    .GetRequiredService<IHttpClientFactory>()
                    .CreateClient(AgentHttpClient));

        services.AddSingleton<
            IUriLauncher,
            SystemUriLauncher>();

        services.AddSingleton<
            OpenOnionAccountService>();

        services.AddSingleton<
            ConnectionTester>();

        // Agent icon selection, processing, temporary storage, committing,
        // and deletion.
        services.AddSingleton<
            IAgentIconService,
            AgentIconService>();

        // ---- SQLite repositories ----
        services.AddSingleton<AgentRepository>();
        services.AddSingleton<SessionRepository>();
        services.AddSingleton<SidebarStateRepository>();
        services.AddSingleton<PreferencesRepository>();
        services.AddSingleton<ConversationRepository>();
        services.AddSingleton<UsageRepository>();
        services.AddSingleton<WindowPlacementStore>();

        // ---- Keyboard ----
        //
        // Singleton because it is the live binding table the window's key
        // handlers consult on every keystroke, and a rebind made in Settings
        // has to be visible to them immediately.
        services.AddSingleton<KeyboardShortcutService>();

        // ---- Presence and runtime ----
        services.AddSingleton<AgentPresenceService>();

        // AgentSessionManager dependencies are resolved automatically.
        services.AddSingleton<AgentSessionManager>();

        // ---- Notification subsystem ----
        services.AddSingleton<WindowPresenceService>();
        services.AddSingleton<NotificationSettingsStore>();
        services.AddSingleton<WindowsNotificationService>();
        services.AddSingleton<InAppNotificationService>();

        services.AddSingleton<
            INotificationScheduler,
            ThreadingScheduler>();

        // The coordinator takes two INotificationChannel parameters
        // (system and in-app), which the container cannot disambiguate
        // positionally, plus an onError delegate, so it is built manually.
        services.AddSingleton<NotificationCoordinator>(
            serviceProvider =>
                new NotificationCoordinator(
                    systemChannel:
                        serviceProvider
                            .GetRequiredService<
                                WindowsNotificationService>(),

                    inAppChannel:
                        serviceProvider
                            .GetRequiredService<
                                InAppNotificationService>(),

                    presence:
                        serviceProvider
                            .GetRequiredService<
                                WindowPresenceService>(),

                    settings:
                        serviceProvider
                            .GetRequiredService<
                                NotificationSettingsStore>(),

                    scheduler:
                        serviceProvider
                            .GetRequiredService<
                                INotificationScheduler>(),

                    onError:
                        NotificationLog.Warn));

        services.AddSingleton<
            ConversationNavigationService>();

        services.AddSingleton<
            NotificationActivationRouter>();

        // ---- Startup wiring ----
        //
        // These services start when the Generic Host starts.
        services.AddHostedService<
            NotificationStartupService>();

        services.AddHostedService<
            KeyboardShortcutStartupService>();

        services.AddHostedService<
            ImageCacheMaintenanceService>();

        // ---- View models ----
        //
        // Transient: one instance per page or dialog, resolved from the
        // corresponding view constructor.
        services.AddTransient<ChatViewModel>();
        services.AddTransient<AddAgentViewModel>();
        services.AddTransient<SessionsViewModel>();
        services.AddTransient<AgentDetailViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<UsageViewModel>();
        services.AddTransient<KeyboardShortcutsViewModel>();

        return services;
    }
}
