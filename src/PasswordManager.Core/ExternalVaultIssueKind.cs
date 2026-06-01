namespace PasswordManager.Core;

public enum ExternalVaultIssueKind
{
    Groups = 0,
    CustomFields,
    Attachments,
    History,
    Icons,
    AutoType,
    UnsupportedMetadata,
    UnsupportedTimestamps,
    UnsupportedFormatOrKdf
}
