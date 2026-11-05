using EMailMPC.Core.Accounts;
using EMailMPC.Core.Storage;

namespace EMailMPC.Desktop;

public partial class ComposeWindow
{
    private string? _inReplyTo;
    private IReadOnlyList<string>? _references;

    private void RestoreThreadingHeaders(MailDraftRecord draft)
    {
        _inReplyTo = draft.InReplyTo;
        _references = ParseReferences(draft.References);
    }

    private void RestoreThreadingHeaders(MessageResponseDraft response)
    {
        _inReplyTo = response.InReplyTo;
        _references = response.References;
    }

    private static IReadOnlyList<string>? ParseReferences(string? references) =>
        string.IsNullOrWhiteSpace(references)
            ? null
            : references.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
}
