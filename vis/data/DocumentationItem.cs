#nullable enable

using System;
using CsvHelper.Configuration.Attributes;

namespace Ritgard.Data;

public record DocumentationItem
{
    [Name("Internal Entity UUID")]
    public Guid Id { get; set; }

    [Name("Entity Type")]
    public string EntityType { get; set; } = string.Empty;

    [Name("Original Platform ID")]
    public string? OriginalPlatformId { get; set; }

    [Name("Short Representation")]
    public string? ShortRepresentation { get; set; }

    [Name("URL")]
    public string? Url { get; set; }

    [Name("Link Text (Mask)")]
    public string? LinkText { get; set; }

    [Name("Link href Text")]
    public string? RelativeLink { get; set; }

    [Name("Link href URL")]
    public string? AbsoluteLink { get; set; }

    [Name("Link Type")]
    public string? LinkType { get; set; }

    [Name("Documentation Source")]
    public string? DocumentationSource { get; set; }

    [Name("Resource Type")]
    public string? ResourceType { get; set; }

    [Name("Resource Availability")]
    public bool? IsAvailable { get; set; }

    [Name("Unavailability Reason")]
    public string? UnavailabilityReason { get; set; }

    [Name("Bytes")]
    public int? ByteLength;

    [Name("Words")]
    public int? WordLength;

    [Name("Tags")]
    public int? TagCount;
}
