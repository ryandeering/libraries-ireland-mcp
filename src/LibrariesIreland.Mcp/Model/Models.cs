namespace LibrariesIreland.Mcp.Model;

public enum Scope
{
    HomeLibrary,
    Nationwide,
}

public enum MaterialType
{
    Any,
    Books,
    AudioBooks,
    SoundRecordings,
    MusicScores,
    Video,
    Journals,
}

public enum ContentKind
{
    Any,
    NonFiction,
    Fiction,
}

public enum Audience
{
    Any,
    Adult,
    Youth,
    Children,
}

public sealed record Holding(
    string Branch,
    string? Authority,
    string? Collection,
    string? CallNumber,
    string Status,
    string? DueDate,
    string? Barcode,
    bool Available);

public sealed record BookSummary(
    string Brn,
    string Title,
    string? Author,
    string? Published,
    string? Summary,
    int TotalCopies,
    int AvailableCopies,
    int HoldingAuthorities,
    string AvailabilityNote,
    string CatalogueUrl);

public sealed record BookDetail(
    string Brn,
    string Title,
    string? Author,
    string? Published,
    string? Isbn,
    string? Summary,
    string? CoverImageUrl,
    int TotalCopies,
    int AvailableCopies,
    IReadOnlyList<Holding> Holdings,
    string CatalogueUrl,
    string HowToBorrow);

public sealed record SearchResponse(
    string Query,
    string ScopeDescription,
    int? TotalMatches,
    IReadOnlyList<BookSummary> Results,
    string? Note = null);

public sealed record AvailabilityReport(
    string Brn,
    string Title,
    string Verdict,
    bool OnShelfAtHomeBranch,
    bool OnShelfInHomeLibrary,
    bool HeldByHomeLibrary,
    IReadOnlyList<Holding> HomeLibraryCopies,
    IReadOnlyList<Holding> OtherCopies,
    IReadOnlyList<string> OtherAuthorities,
    string HowToBorrow,
    string CatalogueUrl,
    string? Note = null);

public sealed record Branch(
    string Irn,
    string Name,
    string? Authority = null,
    string? Address = null,
    string? Phone = null,
    string? Email = null,
    IReadOnlyList<string>? OpeningHours = null);

public sealed record BranchSearchResponse(
    IReadOnlyList<Branch> Matches,
    int TotalBranches,
    string? Note = null);

public sealed record HomeLibrary(
    string? Authority,
    string? AuthorityName,
    string? BranchIrn,
    string? BranchName,
    bool IsConfigured,
    string Note,
    IReadOnlyList<string>? KnownAuthorities = null);
