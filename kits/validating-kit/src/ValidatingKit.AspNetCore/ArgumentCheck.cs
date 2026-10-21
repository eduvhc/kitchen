using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using ResultingKit;

namespace ValidatingKit.AspNetCore;

/// <summary>Validates one handler argument. Built once per endpoint or action; runs on every request.</summary>
internal abstract class ArgumentCheck(int index, string name)
{
    /// <summary>The argument's position, for minimal API invocation arguments.</summary>
    public int Index { get; } = index;

    /// <summary>The parameter name, for controller action arguments.</summary>
    public string Name { get; } = name;

    public abstract Result Validate(IServiceProvider services, object? argument);

    /// <summary>
    /// Returns a check for each parameter whose type has an <see cref="IValidator{T}"/> registered. Without
    /// <see cref="IServiceProviderIsService"/> every parameter gets one, and a missing validator is skipped per request.
    /// </summary>
    [RequiresDynamicCode("Closes ArgumentCheck<T> over each parameter type. Use Validate<T>() under native AOT.")]
    [RequiresUnreferencedCode("Closes ArgumentCheck<T> over each parameter type. Use Validate<T>() when trimming.")]
    public static ArgumentCheck[] ForValidators(IEnumerable<(int Index, string Name, Type Type)> parameters, IServiceProvider services)
    {
        var isService = services.GetService<IServiceProviderIsService>();
        var checks = new List<ArgumentCheck>();

        foreach (var (index, name, type) in parameters)
        {
            if (type.IsByRef || type.IsPointer || type.ContainsGenericParameters)
            {
                continue;
            }

            if (isService is not null && !isService.IsService(typeof(IValidator<>).MakeGenericType(type)))
            {
                continue;
            }

            checks.Add((ArgumentCheck)Activator.CreateInstance(typeof(ValidatorCheck<>).MakeGenericType(type), index, name)!);
        }

        return [.. checks];
    }

    public static IEnumerable<(int Index, string Name, Type Type)> Describe(MethodInfo method) =>
        method.GetParameters().Select(static p => (p.Position, p.Name ?? "", p.ParameterType));
}

/// <summary>Runs the <see cref="IValidator{T}"/> registered for the argument's type, resolved from the request's services.</summary>
internal sealed class ValidatorCheck<T>(int index, string name) : ArgumentCheck(index, name)
{
    public override Result Validate(IServiceProvider services, object? argument) =>
        services.GetService<IValidator<T>>() switch
        {
            null => Result.Success(),
            Validator<T> validator => validator.Check((T?)argument),
            var validator => validator.Validate((T?)argument),
        };
}

/// <summary>Runs inline rules given to <c>Validate&lt;T&gt;</c>.</summary>
internal sealed class RulesCheck<T>(int index, string name, string code, Func<Validation<T>, Validation<T>> rules)
    : ArgumentCheck(index, name)
{
    public override Result Validate(IServiceProvider services, object? argument) =>
        rules(Validation.That((T?)argument, code, Name)).ToResult();
}
