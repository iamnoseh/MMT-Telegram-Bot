using MediatR;
using Microsoft.Extensions.Logging;
using MMT.Application.Common.Interfaces.Repositories;

namespace MMT.Application.Features.Admin.Queries.GetStatistics;

public class GetStatisticsQueryHandler(
    IUnitOfWork unitOfWork,
    ILogger<GetStatisticsQueryHandler> logger)
    : IRequestHandler<GetStatisticsQuery, StatisticsDto>
{
    public async Task<StatisticsDto> Handle(GetStatisticsQuery request, CancellationToken ct)
    {
        var allUsers = await unitOfWork.Users.GetAllAsync(ct);
        var allSubjects = await unitOfWork.Subjects.GetAllAsync(ct);
        
        var totalUsers = allUsers.Count;
        var today = DateTime.UtcNow.Date;
        var activeUsersToday = allUsers.Count(u => 
            u.UpdatedAt.HasValue && u.UpdatedAt.Value.Date == today);
        
        var totalCorrectAnswers = 0; 
        var totalTestsSolved = 0; 
        
        var subjectStats = allSubjects.Select(s => new SubjectStatsDto
        {
            SubjectName = s.Name,
            QuestionsCount = s.Questions.Count,
            TestsTaken = 0 
        }).OrderByDescending(s => s.QuestionsCount).Take(5).ToArray();
        
        logger.LogInformation("Admin {AdminChatId} requested statistics", request.AdminChatId);
        
        return new StatisticsDto
        {
            TotalUsers = totalUsers,
            ActiveUsersToday = activeUsersToday,
            TotalQuestions = allSubjects.Sum(s => s.Questions.Count),
            TotalTestsSolved = totalTestsSolved,
            TotalCorrectAnswers = totalCorrectAnswers,
            TotalSubjects = allSubjects.Count,
            TopSubjects = subjectStats
        };
    }
}
