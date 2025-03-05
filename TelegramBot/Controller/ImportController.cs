
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TelegramBot.Domain.Entities;

namespace TelegramBot.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ImportController : ControllerBase
    {
        private readonly DataContext _context;

        public ImportController(DataContext context)
        {
            _context = context;
        }


        [HttpPost]
        public async Task<IActionResult> ImportDocx(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("Файл интихоб нашудааст ё холи аст.");

            List<Question> questions;
            try
            {
                using var stream = new MemoryStream();
                await file.CopyToAsync(stream);
                stream.Position = 0;
                questions = ParseQuestionsFromDocx(stream);
            }
            catch (Exception ex)
            {
                return BadRequest($"Хатогӣ дар парс кардани файл: {ex.Message}");
            }

            if (questions == null || !questions.Any())
                return BadRequest("Ҳеҷ саволе дар файл ёфт нашуд.");

            try
            {
                await _context.Questions.AddRangeAsync(questions);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Хатогӣ ҳангоми сабт дар база: {ex.Message}");
            }

            return Ok("Саволҳо бо муваффақият ворид шуданд.");
        }
        
        private List<Question> ParseQuestionsFromDocx(Stream docxStream)
        {
            List<Question> questions = new List<Question>();

            using (WordprocessingDocument wordDoc = WordprocessingDocument.Open(docxStream, false))
            {
                Body body = wordDoc.MainDocumentPart.Document.Body;
                var paragraphs = body.Elements<Paragraph>()
                                     .Select(p => p.InnerText)
                                     .Where(text => !string.IsNullOrWhiteSpace(text))
                                     .ToList();
                
                for (int i = 0; i <= paragraphs.Count - 5; i += 5)
                {
                    string qLine = paragraphs[i].Trim();
                    var parts = qLine.Split(new char[] { '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    string questionText = parts.Length > 1 ? string.Join(" ", parts.Skip(1)) : qLine;

                    string[] variants = new string[4];
                    int correctIndex = -1;
                    for (int j = 0; j < 4; j++)
                    {
                        string line = paragraphs[i + j + 1].Trim();
                        int idx = line.IndexOf(")");
                        if (idx >= 0)
                        {
                            line = line.Substring(idx + 1).Trim();
                        }
                        if (line.EndsWith("--"))
                        {
                            line = line.Substring(0, line.Length - 2).Trim();
                            correctIndex = j;
                        }
                        variants[j] = line;
                    }

                    if (correctIndex == -1)
                        throw new Exception("Ҷавоби дуруст барои савол: " + questionText + " ёфт нашуд.");

                    Option option = new Option
                    {
                        FirstVariant = variants[0],
                        SecondVariant = variants[1],
                        ThirdVariant = variants[2],
                        FourthVariant = variants[3],
                        Answer = variants[correctIndex]
                    };

                    Question question = new Question
                    {
                        QuestionText = questionText,
                        Option = option
                    };

                    questions.Add(question);
                }
            }

            return questions;
        }
    }
}
