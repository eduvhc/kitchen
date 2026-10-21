using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using ResultingKit;
using ResultingKit.AspNetCore;

namespace ValidatingKit.AspNetCore;

/// <summary>
/// A global MVC action filter that validates every action argument whose type has an <see cref="IValidator{T}"/>,
/// after model binding and before the action runs. Added by <c>WithValidation()</c> on the MVC builder.
/// </summary>
[RequiresDynamicCode("Closes generic validator checks over parameter types.")]
[RequiresUnreferencedCode("Closes generic validator checks over parameter types.")]
internal sealed class ValidationActionFilter(Func<Error, int>? statusCode) : IAsyncActionFilter
{
    private readonly ConditionalWeakTable<ActionDescriptor, ArgumentCheck[]> _checks = [];

    public Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var services = context.HttpContext.RequestServices;
        if (!_checks.TryGetValue(context.ActionDescriptor, out var checks))
        {
            checks = ArgumentCheck.ForValidators(
                context.ActionDescriptor.Parameters.Select(static (p, i) => (i, p.Name, p.ParameterType)),
                services);
            _checks.AddOrUpdate(context.ActionDescriptor, checks);
        }

        foreach (var check in checks)
        {
            context.ActionArguments.TryGetValue(check.Name, out var argument);

            var result = check.Validate(services, argument);
            if (result.IsFailure)
            {
                context.Result = result.ToActionResult(statusCode);
                return Task.CompletedTask;
            }
        }

        return next();
    }
}
