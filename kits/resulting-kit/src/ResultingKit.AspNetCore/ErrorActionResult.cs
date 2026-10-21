using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;

namespace ResultingKit.AspNetCore;

/// <summary>
/// An <see cref="IActionResult"/> that writes an <see cref="Error"/> as an RFC 9457 problem, the way
/// <see cref="ControllerBase.Problem(string, string, int?, string, string)"/> does. <c>ToActionResult</c>
/// returns one for every failure.
/// </summary>
/// <remarks>
/// <para>
/// The problem is built when the result executes, by the <see cref="ProblemDetailsFactory"/> registered in
/// <c>HttpContext.RequestServices</c>. MVC's default factory takes <c>title</c> and <c>type</c> from
/// <see cref="ApiBehaviorOptions.ClientErrorMapping"/>, adds a <c>traceId</c>, and runs
/// <c>ProblemDetailsOptions.CustomizeProblemDetails</c>. <c>detail</c> is <see cref="Error.Message"/>, and a
/// <c>code</c> extension (<see cref="ErrorHttpExtensions.CodeExtension"/>) carrying <see cref="Error.Code"/> is
/// added after the factory returns, as <c>ControllerBase.Problem</c> adds its <c>extensions</c>.
/// </para>
/// <para>
/// An error with <see cref="Error.FieldErrors"/> becomes a <see cref="ValidationProblemDetails"/> instead, built by
/// <see cref="ProblemDetailsFactory.CreateValidationProblemDetails"/> as <c>ControllerBase.ValidationProblem</c>
/// does, with the fields under <c>errors</c> and the same <c>detail</c>, <c>code</c> and <c>traceId</c>.
/// </para>
/// <para>
/// It is written through an <see cref="ObjectResult"/>, so content negotiation picks the formatter and
/// <c>application/problem+json</c> is the default content type.
/// </para>
/// <para>
/// In a controller unit test, inspect <see cref="Error"/> and <see cref="StatusCode"/> without executing it.
/// </para>
/// </remarks>
public sealed class ErrorActionResult : ActionResult, IStatusCodeActionResult
{
    /// <summary>Creates a result that writes <paramref name="error"/> with <paramref name="statusCode"/>.</summary>
    /// <param name="error">The error to write.</param>
    /// <param name="statusCode">The response status code.</param>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
    public ErrorActionResult(Error error, int statusCode)
    {
        ArgumentNullException.ThrowIfNull(error);

        Error = error;
        StatusCode = statusCode;
    }

    /// <summary>The error the problem describes.</summary>
    public Error Error { get; }

    /// <summary>The response status code, also the problem's <c>status</c>.</summary>
    public int StatusCode { get; }

    int? IStatusCodeActionResult.StatusCode => StatusCode;

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">No <see cref="ProblemDetailsFactory"/> is registered.</exception>
    public override Task ExecuteResultAsync(ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var factory = context.HttpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
        var problem = Error.FieldErrors is { } fields
            ? factory.CreateValidationProblemDetails(context.HttpContext, ToModelState(fields), StatusCode, detail: Error.Message)
            : factory.CreateProblemDetails(context.HttpContext, StatusCode, detail: Error.Message);
        problem.Extensions[ErrorHttpExtensions.CodeExtension] = Error.Code;

        return new ObjectResult(problem) { StatusCode = problem.Status }.ExecuteResultAsync(context);
    }

    private static ModelStateDictionary ToModelState(IReadOnlyDictionary<string, string[]> fields)
    {
        var modelState = new ModelStateDictionary(int.MaxValue);

        foreach (var (field, messages) in fields)
        {
            foreach (var message in messages)
            {
                modelState.AddModelError(field, message);
            }
        }

        return modelState;
    }
}
