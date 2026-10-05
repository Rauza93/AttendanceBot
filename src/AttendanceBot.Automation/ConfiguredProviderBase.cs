using AttendanceBot.Core.Interfaces;
using AttendanceBot.Core.Models;
using OpenQA.Selenium;

namespace AttendanceBot.Automation;

public abstract class ConfiguredProviderBase(ProviderOptions options) : IAttendanceProvider
{
    public abstract ProviderKind Kind { get; }
    protected abstract bool PrerequisitesAvailable(ProviderContext context, out string message);
    public Task<ProviderResult> CheckPrerequisitesAsync(ProviderContext c, CancellationToken ct) => Task.FromResult(PrerequisitesAvailable(c, out var m) ? new ProviderResult(RunStatus.Pending, m) : new ProviderResult(RunStatus.Blocked, m, "PREREQUISITES"));
    public async Task<ProviderResult> LoginAsync(ProviderContext c, CancellationToken ct) => await ExecuteAsync(c, ct, async (d) => { if (string.IsNullOrWhiteSpace(options.LoginUrl) || string.IsNullOrWhiteSpace(options.LoginUsernameSelector) || string.IsNullOrWhiteSpace(options.LoginPasswordSelector)) return Manual(); d.Navigate().GoToUrl(options.LoginUrl); return new ProviderResult(RunStatus.Pending, "Login page opened; selectors must be configured by the operator."); });
    public Task<ProviderResult> CheckAttendanceStatusAsync(ProviderContext c, CancellationToken ct) => Task.FromResult(options.StatusSelector.Length == 0 ? Manual() : new ProviderResult(RunStatus.Pending, "Status selector configured; implement site-specific status mapping."));
    public Task<ProviderResult> ClockInAsync(ProviderContext c, CancellationToken ct) => Task.FromResult(SubmitNotConfigured(options.ClockInSelector));
    public Task<ProviderResult> ClockOutAsync(ProviderContext c, CancellationToken ct) => Task.FromResult(SubmitNotConfigured(options.ClockOutSelector));
    public Task<ProviderResult> VerifyResultAsync(ProviderContext c, CancellationToken ct) => Task.FromResult(options.SuccessSelector.Length == 0 ? Manual() : new ProviderResult(RunStatus.Pending, "Verification selector configured; implement site-specific result mapping."));
    public Task LogoutAsync(ProviderContext c, CancellationToken ct) => Task.CompletedTask;
    protected static ProviderResult Manual() => new(RunStatus.ManualRequired, "Website workflow/selector configuration is missing; manual interaction is required.", "CONFIGURATION_REQUIRED", null, true);
    private static ProviderResult SubmitNotConfigured(string selector) => selector.Length == 0 ? Manual() : new ProviderResult(RunStatus.ManualRequired, "Submission is intentionally disabled until the real selector and workflow are reviewed and configured.", "SAFE_CONFIGURATION_REQUIRED", null, true);
    private async Task<ProviderResult> ExecuteAsync(ProviderContext c, CancellationToken ct, Func<IWebDriver, Task<ProviderResult>> action) { await using var session = new EdgeSession(c.VisibleBrowser, c.Timeout); try { return await action(session.Driver); } catch (WebDriverException ex) { return new ProviderResult(RunStatus.Failed, "Browser automation failed.", "WEBDRIVER", ex.Message); } }
}
