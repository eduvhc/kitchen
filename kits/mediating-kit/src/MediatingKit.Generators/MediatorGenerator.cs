using Microsoft.CodeAnalysis;

namespace MediatingKit.Generators;

/// <summary>
/// Writes, for each project that references MediatingKit:
/// <list type="bullet">
///   <item>an exports class, when the project has handlers whose messages are public, so a project referencing it can
///   dispatch to them even if the handlers themselves are internal;</item>
///   <item>a mediator and an interceptor for every <c>AddMediator()</c> call, when the project makes one: one typed
///   field and one type check per handler, with no reflection or lookup per call;</item>
///   <item>the MED diagnostics: missing and duplicate handlers, ambiguous responses, unusable handlers.</item>
/// </list>
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class MediatorGenerator : IIncrementalGenerator
{
    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var types = context.SyntaxProvider
            .CreateSyntaxProvider(static (node, _) => Discovery.IsTypeCandidate(node), Discovery.InspectType)
            .Where(static t => t is not null)
            .Select(static (t, _) => t!)
            .Collect();

        var calls = context.SyntaxProvider
            .CreateSyntaxProvider(static (node, _) => Discovery.IsCallCandidate(node), Discovery.InspectCall)
            .Where(static c => c is not null)
            .Select(static (c, _) => c!)
            .Collect();

        var references = context.CompilationProvider.Select(Discovery.InspectReferences);

        var input = types.Combine(calls).Combine(references);

        context.RegisterSourceOutput(input, static (spc, source) =>
            Emitter.Execute(spc, source.Left.Left, source.Left.Right, source.Right));
    }
}
