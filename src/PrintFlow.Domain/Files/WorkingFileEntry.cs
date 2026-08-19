namespace PrintFlow.Domain.Files;

/// <summary>
/// One file found under a session's <c>Working\</c> area, together with the attempt folder
/// it sits in (Epic 11100 Part 3B §7).
/// </summary>
/// <remarks>
/// <see cref="AttemptFolderName"/> is carried separately rather than left for the caller to
/// slice out of <see cref="File"/>: attributing a leftover file to an attempt is the whole
/// basis on which startup recovery decides whether quarantining it is safe, and the workspace
/// module is the only part of the system allowed to take a path apart (MVP design invariant
/// 12). An empty value means the file sat directly in <c>Working\</c> and belongs to no
/// attempt — which recovery reports rather than guesses about.
/// </remarks>
public sealed record WorkingFileEntry(WorkspaceFileRef File, string AttemptFolderName);
