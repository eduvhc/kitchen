using System.Security.Claims;
using EmailService.Common;
using EmailService.Templating;

namespace EmailService.Features.Emails.SendEmail;

public sealed class SendEmailEndpoint : IEndpoint
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapPost("/", HandleAsync)
            .WithName("SendEmail")
            .WithSummary("Queue an email for delivery");

    private static async Task<IResult> HandleAsync(
        SendEmailRequest request,
        SendEmailHandler handler,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        try
        {
            var result = await handler.HandleAsync(request, user.Identity?.Name, ct);

            return result.Deduplicated
                ? Results.Ok(result.Email)
                : Results.Created($"/v1/emails/{result.Email.Id}", result.Email);
        }
        catch (ValidationException ex)
        {
            return Results.Problem(
                title: "Invalid email request",
                detail: ex.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }
        catch (TemplateRenderException ex)
        {
            return Results.Problem(
                title: "Template render failed",
                detail: ex.Message,
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }
    }
}
