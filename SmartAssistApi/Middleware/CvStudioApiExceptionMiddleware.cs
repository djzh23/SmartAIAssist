using Microsoft.AspNetCore.Mvc;
using Npgsql;
using CvStudio.Application.Exceptions;
using SmartAssistApi.Services;

namespace SmartAssistApi.Middleware;

/// <summary>Maps CV.Studio domain exceptions to RFC 7807 problem responses for <c>/api/cv-studio</c> routes.</summary>
public sealed class CvStudioApiExceptionMiddleware(RequestDelegate next, ILogger<CvStudioApiExceptionMiddleware> logger, IWebHostEnvironment environment)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (NotFoundException ex)
        {
            await WriteProblemAsync(context, StatusCodes.Status404NotFound, "Not Found", ex.Message);
        }
        catch (UnprocessableEntityException ex)
        {
            await WriteProblemAsync(context, StatusCodes.Status422UnprocessableEntity, "Validation Failed", string.Join("; ", ex.Errors));
        }
        catch (CvStudioPdfQuotaExceededException ex)
        {
            await WriteProblemAsync(
                context,
                StatusCodes.Status429TooManyRequests,
                "PDF-Export-Limit",
                ex.Message);
        }
        catch (NpgsqlException ex)
        {
            logger.LogError(ex, "CV.Studio database error for {Path}", context.Request.Path);
            await WriteProblemAsync(
                context,
                StatusCodes.Status503ServiceUnavailable,
                "Database Unavailable",
                "Database is unavailable or not initialized. Check PostgreSQL connection and CV.Studio migrations.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "CV.Studio unhandled exception for {Path}", context.Request.Path);
            var detail = environment.IsDevelopment()
                ? $"An unexpected error occurred. {ex.GetType().Name}: {ex.Message}"
                : "An unexpected error occurred.";
            await WriteProblemAsync(context, StatusCodes.Status500InternalServerError, "Server Error", detail);
        }
    }

    private static async Task WriteProblemAsync(HttpContext context, int statusCode, string title, string detail)
    {
        if (context.Response.HasStarted)
            throw new InvalidOperationException("Response has already started.");

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = context.Request.Path.Value,
        };

        await context.Response.WriteAsJsonAsync(problem);
    }
}
