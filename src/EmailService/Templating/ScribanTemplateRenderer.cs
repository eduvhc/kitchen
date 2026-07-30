using EmailService.Templating.Abstractions;
using Scriban.Runtime;
using Scriban;
using System.Collections.Concurrent;

namespace EmailService.Templating;

public class ScribanTemplateRenderer : ITemplateRenderer
{
    private readonly ConcurrentDictionary<string, Template> _cache = new();

    public string Render(string template, IDictionary<string, object?> model)
    {
        var parsed = _cache.GetOrAdd(template, static source =>
        {
            var candidate = Template.Parse(source);
            if (candidate.HasErrors)
            {
                throw new TemplateRenderException(string.Join("; ", candidate.Messages.Select(m => m.ToString())));
            }

            return candidate;
        });

        var scriptObject = new ScriptObject();
        foreach (var (key, value) in model)
        {
            scriptObject[key] = value;
        }

        var context = new TemplateContext
        {
            StrictVariables = false,
            MemberRenamer = member => member.Name,
        };
        context.PushGlobal(scriptObject);

        try
        {
            return parsed.Render(context);
        }
        catch (Exception ex) when (ex is not TemplateRenderException)
        {
            throw new TemplateRenderException($"Template render failed: {ex.Message}");
        }
    }
}
