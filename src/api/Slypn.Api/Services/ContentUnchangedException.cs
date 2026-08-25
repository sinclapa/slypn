namespace Slypn.Api.Services;

/// <summary>
/// The caller asked for a change that would not change anything — submitting a revision
/// byte-identical to the article it replaces.
///
/// Its own type rather than an InvalidOperationException so the endpoint can answer with a
/// distinct status, and the UI can tell "nothing to do here" apart from "that went wrong"
/// without matching on message text.
/// </summary>
public sealed class ContentUnchangedException(string message) : Exception(message);
