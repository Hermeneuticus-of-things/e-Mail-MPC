namespace EMailMPC.Core.Tests.Storage;

public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "This test requires Windows DPAPI.";
        }
    }
}
