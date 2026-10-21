using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using ResultingKit;

namespace ValidatingKit.AspNetCore;

/// <summary>
/// Validates controller action arguments: every argument whose type has an <see cref="IValidator{T}"/> registered
/// is checked after model binding, and a failure returns the <c>ValidationProblemDetails</c> that
/// ResultingKit.AspNetCore builds instead of running the action.
/// </summary>
public static class ValidationMvcBuilderExtensions
{
    /// <summary>Adds a global filter that validates action arguments with their registered validators.</summary>
    /// <param name="builder">The builder <c>AddControllers()</c> returned.</param>
    /// <param name="statusCode">Picks the status for a failure. <see langword="null"/> uses 400.</param>
    /// <example><c>builder.Services.AddControllers().WithValidation();</c></example>
    [RequiresDynamicCode("Closes generic validator checks over parameter types.")]
    [RequiresUnreferencedCode("Closes generic validator checks over parameter types.")]
    public static IMvcBuilder WithValidation(this IMvcBuilder builder, Func<Error, int>? statusCode = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.Configure<MvcOptions>(options => options.Filters.Add(new ValidationActionFilter(statusCode)));
        return builder;
    }

    /// <inheritdoc cref="WithValidation(IMvcBuilder, Func{Error, int})"/>
    [RequiresDynamicCode("Closes generic validator checks over parameter types.")]
    [RequiresUnreferencedCode("Closes generic validator checks over parameter types.")]
    public static IMvcCoreBuilder WithValidation(this IMvcCoreBuilder builder, Func<Error, int>? statusCode = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.Configure<MvcOptions>(options => options.Filters.Add(new ValidationActionFilter(statusCode)));
        return builder;
    }
}
