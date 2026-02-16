using Microsoft.Extensions.Logging;

namespace Ritgard;

public static partial class Utils
{
    public static readonly ILoggerFactory LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(b =>
        {
            b.AddProvider(new GodotLoggingProvider());
        }
    );

    private class GodotLoggingProvider : ILoggerProvider
    {
        public void Dispose()
        {
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new GodotLogger()
            {
                CategoryName = categoryName
            };
        }
    }
}
