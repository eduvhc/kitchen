namespace MailingKit.Templating;

public interface ITemplateRenderer
{
    string Render(string template, IDictionary<string, object?> model);
}
