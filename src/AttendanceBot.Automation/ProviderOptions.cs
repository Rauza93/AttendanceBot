namespace AttendanceBot.Automation;

public sealed class ProviderOptions
{
    public string LoginUrl { get; set; } = "";
    public string AttendanceUrl { get; set; } = "";
    public string StatusSelector { get; set; } = "";
    public string LoginUsernameSelector { get; set; } = "";
    public string LoginPasswordSelector { get; set; } = "";
    public string ClockInSelector { get; set; } = "";
    public string ClockOutSelector { get; set; } = "";
    public string SuccessSelector { get; set; } = "";
}
