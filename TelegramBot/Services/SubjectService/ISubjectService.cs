using TelegramBot.Domain.DTOs;
using TelegramBot.Domain.Entities;

namespace TelegramBot.Services.SubjectService;

public interface ISubjectService
{
    Task<List<GetSubjectDTO>> GetAllSubjects();
    Task<SubjectWithQuestionsDTO> GetSubjectById(int id);
    Task<GetSubjectDTO> CreateSubject(CreateSubjectDTO createSubject);
    Task<GetSubjectDTO> UpdateSubject(UpdateSubjectDTO updateSubject);
    Task<bool> DeleteSubject(int id);
}