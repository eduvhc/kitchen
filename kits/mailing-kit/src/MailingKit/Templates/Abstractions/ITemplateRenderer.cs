namespace MailingKit.Templates;

public interface ITemplateRenderer
{
    string Render(string templateText, IDictionary<string, object?> model);
}
