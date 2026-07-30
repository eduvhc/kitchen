namespace EmailService.Templating.Abstractions;

public interface ITemplateRenderer
{
    string Render(string template, IDictionary<string, object?> model);
}
