using System.Net;
using System.Text.Json;
using FashionStore.Application.Common.Models;
using FashionStore.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace FashionStore.Web.Middleware;

public static class ExceptionHandlingMiddlewareExtensions
{
    public static IApplicationBuilder UseGlobalExceptionHandling(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<ExceptionHandlingMiddleware>();
    }
}

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IWebHostEnvironment _env;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger,
        IWebHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (DomainException ex)
        {
            _logger.LogWarning(ex, "Domain rule violation: {ErrorCode}", ex.ErrorCode);
            await HandleDomainExceptionAsync(context, ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized access attempt");
            await HandleUnauthorizedAsync(context);
        }
        catch (Exception ex)
        {
            var correlationId = context.Items["CorrelationId"]?.ToString() ?? "unknown";
            _logger.LogError(ex, "Unhandled exception occurred. CorrelationId: {CorrelationId}", correlationId);
            await HandleUnknownExceptionAsync(context, ex, correlationId);
        }
    }

    private static Task HandleDomainExceptionAsync(HttpContext context, DomainException ex)
    {
        context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
        context.Response.ContentType = "application/json";

        var error = new ErrorResponse(ex.ErrorCode, ex.Message);
        var json = JsonSerializer.Serialize(error);

        return context.Response.WriteAsync(json);
    }

    private static Task HandleUnauthorizedAsync(HttpContext context)
    {
        context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
        context.Response.ContentType = "application/json";

        var error = new ErrorResponse("UNAUTHORIZED", "Authentication is required to access this resource.");
        var json = JsonSerializer.Serialize(error);

        return context.Response.WriteAsync(json);
    }

    private static Task HandleUnknownExceptionAsync(HttpContext context, Exception ex, string correlationId)
    {
        context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
        context.Response.ContentType = "application/json";

        var message = "An unexpected error occurred. Please try again later.";
        string? details = null;

        var env = context.RequestServices.GetRequiredService<IWebHostEnvironment>();
        if (env.IsDevelopment())
        {
            details = ex.ToString();
        }

        var error = new ErrorResponse("INTERNAL_ERROR", message, correlationId, details);
        var json = JsonSerializer.Serialize(error);

        return context.Response.WriteAsync(json);
    }
}
