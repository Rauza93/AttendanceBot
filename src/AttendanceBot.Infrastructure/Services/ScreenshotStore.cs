using OpenQA.Selenium;

namespace AttendanceBot.Infrastructure.Services;

public sealed class ScreenshotStore
{
    private readonly string _root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AttendanceBot", "Screenshots");
    public string Save(IWebDriver driver, string profileName, string status)
    {
        Directory.CreateDirectory(Path.Combine(_root, DateTime.Now.ToString("yyyy-MM-dd")));
        var safe = string.Concat(profileName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        var path = Path.Combine(_root, DateTime.Now.ToString("yyyy-MM-dd"), $"{DateTime.Now:HHmmss}_{safe}_{status}.png");
        ((ITakesScreenshot)driver).GetScreenshot().SaveAsFile(path); return path;
    }
}
