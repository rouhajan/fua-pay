using FuaPay.Web.Modules.Credits.Domain;
using FuaPay.Web.Modules.Jobs.Application;
using FuaPay.Web.Pages.Shared;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FuaPay.Web.Tests.Pages;

public sealed class PageOperationErrorTests
{
    [Fact]
    public void IsExpected_KnownApplicationException_ReturnsTrue()
    {
        Assert.True(
            PageOperationError.IsExpected(
                new JobNotFoundException(Guid.NewGuid())));
    }

    [Fact]
    public void IsExpected_RawInvalidOperationException_ReturnsFalse()
    {
        Assert.False(
            PageOperationError.IsExpected(
                new InvalidOperationException("internal detail")));
    }

    [Fact]
    public void IsExpected_RawArgumentException_ReturnsFalse()
    {
        Assert.False(
            PageOperationError.IsExpected(
                new ArgumentException("programmer bug")));
    }

    [Fact]
    public void Add_UsesSafeMessageAndLogsOriginalException()
    {
        var loggerFactory = new RecordingLoggerFactory();
        using var services = new ServiceCollection()
            .AddSingleton<ILoggerFactory>(loggerFactory)
            .BuildServiceProvider();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = services,
            TraceIdentifier = "trace-123"
        };

        var pageModel = new TestPageModel
        {
            PageContext = new PageContext
            {
                HttpContext = httpContext
            }
        };

        var exception = new InsufficientCreditException();

        PageOperationError.Add(
            pageModel,
            exception,
            "credit.pay",
            "Citlivý interní text se nesmí zobrazit.");

        var error = Assert.Single(
            pageModel.ModelState[string.Empty]!.Errors);

        Assert.Equal(
            "Na účtu není dostatek kreditu.",
            error.ErrorMessage);
        Assert.DoesNotContain(exception.Message, error.ErrorMessage);
        Assert.Null(loggerFactory.Logger.Exception);
        Assert.Contains(
            nameof(InsufficientCreditException),
            loggerFactory.Logger.Message);
        Assert.Contains(
            "credit.pay",
            loggerFactory.Logger.Message);
        Assert.Contains(
            "trace-123",
            loggerFactory.Logger.Message);
    }

    [Fact]
    public void Add_ArgumentExceptionUsesFallbackWithoutLeakingMessage()
    {
        var loggerFactory = new RecordingLoggerFactory();
        using var services = new ServiceCollection()
            .AddSingleton<ILoggerFactory>(loggerFactory)
            .BuildServiceProvider();

        var pageModel = new TestPageModel
        {
            PageContext = new PageContext
            {
                HttpContext = new DefaultHttpContext
                {
                    RequestServices = services
                }
            }
        };

        var exception = new ArgumentException(
            "ConnectionStrings__FuaPay=secret");

        PageOperationError.Add(
            pageModel,
            exception,
            "input.validation",
            "Zadané údaje nejsou platné.");

        var error = Assert.Single(
            pageModel.ModelState[string.Empty]!.Errors);

        Assert.Equal(
            "Zadané údaje nejsou platné.",
            error.ErrorMessage);
        Assert.DoesNotContain("secret", error.ErrorMessage);
        Assert.Null(loggerFactory.Logger.Exception);
        Assert.DoesNotContain("secret", loggerFactory.Logger.Message);
        Assert.Contains(
            nameof(ArgumentException),
            loggerFactory.Logger.Message);
    }

    private sealed class TestPageModel : PageModel
    {
    }

    private sealed class RecordingLoggerFactory : ILoggerFactory
    {
        public RecordingLogger Logger { get; } = new();

        public void AddProvider(ILoggerProvider provider)
        {
            ArgumentNullException.ThrowIfNull(provider);
        }

        public ILogger CreateLogger(string categoryName)
        {
            Assert.False(string.IsNullOrWhiteSpace(categoryName));
            return Logger;
        }

        public void Dispose()
        {
        }
    }

    private sealed class RecordingLogger : ILogger
    {
        public Exception? Exception { get; private set; }

        public string Message { get; private set; } = string.Empty;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return NullScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Assert.Equal(LogLevel.Warning, logLevel);
            ArgumentNullException.ThrowIfNull(formatter);

            Exception = exception;
            Message = formatter(state, exception);
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
