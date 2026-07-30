namespace EmailService.Templating;

public interface ITemplateRenderer
{
    string Render(string template, IDictionary<string, object?> model);
}

public class TemplateRenderException(string message) : Exception(message);
