namespace MailingKit.Templating;

public interface ITemplateRenderer
{
    string Render(string templateText, IDictionary<string, object?> model);
}
