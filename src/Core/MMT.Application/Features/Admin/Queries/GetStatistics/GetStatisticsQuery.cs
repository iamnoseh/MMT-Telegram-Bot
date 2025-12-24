using MediatR;

namespace MMT.Application.Features.Admin.Queries.GetStatistics;

public record GetStatisticsQuery : IRequest<StatisticsDto>
{
    public long AdminChatId { get; init; }
}

public record StatisticsDto
{
    public int TotalUsers { get; init; }
    public int ActiveUsersToday { get; init; }
    public int TotalQuestions { get; init; }
    public int TotalTestsSolved { get; init; }
    public int TotalCorrectAnswers { get; init; }
    public int TotalSubjects { get; init; }
    public SubjectStatsDto[] TopSubjects { get; init; } = Array.Empty<SubjectStatsDto>();
}

public record SubjectStatsDto
{
    public string SubjectName { get; init; } = string.Empty;
    public int QuestionsCount { get; init; }
    public int TestsTaken { get; init; }
}
