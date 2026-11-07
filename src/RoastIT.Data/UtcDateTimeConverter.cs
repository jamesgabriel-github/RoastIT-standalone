using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace RoastIT.Data;

/// <summary>
/// Instants are always UTC <see cref="DateTime"/>. Writes normalise to UTC; reads come back
/// with <see cref="DateTimeKind.Utc"/> (SQLite stores no kind). Plan §6.1.
/// </summary>
public sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
