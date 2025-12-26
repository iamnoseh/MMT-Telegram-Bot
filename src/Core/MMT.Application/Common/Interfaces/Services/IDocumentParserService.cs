using MMT.Application.Common.DTOs;

namespace MMT.Application.Common.Interfaces.Services;

public interface IDocumentParserService
{
    Task<List<ParsedQuestion>> ParseDocumentAsync(byte[] fileContent, string fileExtension, CancellationToken ct = default);
}
