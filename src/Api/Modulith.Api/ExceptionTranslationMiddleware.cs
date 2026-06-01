using Modulith.BuildingBlocks.Application;
using Modulith.BuildingBlocks.Domain;

namespace Modulith.Api;

/// <summary>
/// Maps domain/application exceptions to HTTP problem responses so handlers can stay
/// free of transport concerns (Result-pattern friendly boundary).
/// </summary>
public sealed class ExceptionTranslationMiddleware
{
    private readonly RequestDelegate _next;

    public ExceptionTranslationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (InvalidCommandException ex)
        {
            await WriteProblem(context, StatusCodes.Status400BadRequest, "Validation failed", string.Join("; ", ex.Errors));
        }
        catch (BusinessRuleValidationException ex)
        {
            await WriteProblem(context, StatusCodes.Status422UnprocessableEntity, "Business rule violated", ex.Message);
        }
        catch (EntityNotFoundException ex)
        {
            await WriteProblem(context, StatusCodes.Status404NotFound, "Not found", ex.Message);
        }
    }

    private static Task WriteProblem(HttpContext context, int statusCode, string title, string detail)
    {
        context.Response.StatusCode = statusCode;
        return Results.Problem(detail: detail, title: title, statusCode: statusCode).ExecuteAsync(context);
    }
}
