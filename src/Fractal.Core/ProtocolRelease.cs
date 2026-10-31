namespace Fractal.Core;
public static class ProtocolRelease
{
    public static string Version => typeof(ProtocolRelease).Assembly.GetName().Version!.ToString(3);
}
