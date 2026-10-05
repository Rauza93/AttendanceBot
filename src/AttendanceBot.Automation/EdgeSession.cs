using OpenQA.Selenium;
using OpenQA.Selenium.Edge;

namespace AttendanceBot.Automation;

public sealed class EdgeSession : IAsyncDisposable
{
    public IWebDriver Driver { get; }
    public EdgeSession(bool visible, TimeSpan timeout)
    {
        var options = new EdgeOptions();
        if (!visible) options.AddArgument("--headless=new");
        options.AddArgument("--disable-extensions");
        Driver = new EdgeDriver(options);
        Driver.Manage().Timeouts().ImplicitWait = TimeSpan.Zero;
        Driver.Manage().Timeouts().PageLoad = timeout;
    }
    public ValueTask DisposeAsync() { try { Driver.Quit(); } finally { Driver.Dispose(); } return ValueTask.CompletedTask; }
}
