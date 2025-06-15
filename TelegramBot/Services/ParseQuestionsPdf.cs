using System.Text;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using TelegramBot.Domain.DTOs;

namespace TelegramBot.Services;

public static class ParseQuestionsPdf
{
    public static List<QuestionDTO> ParseQuestionsFromPdf(Stream pdfStream, int subjectId)
    {
        var questions = new List<QuestionDTO>();

        using (var pdfReader = new PdfReader(pdfStream))
        using (var pdfDocument = new PdfDocument(pdfReader))
        {
            var text = new StringBuilder();
            for (int pageNum = 1; pageNum <= pdfDocument.GetNumberOfPages(); pageNum++)
            {
                var page = pdfDocument.GetPage(pageNum);
                var strategy = new SimpleTextExtractionStrategy();
                var currentText = PdfTextExtractor.GetTextFromPage(page, strategy);
                text.AppendLine(currentText);
            }

            var paragraphs = text.ToString()
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .ToList();

            if (paragraphs.Count < 5)
            {
                throw new Exception("Файл холī аст ё саволҳо нодуруст ворид шудаанд");
            }

            int i = 0;
            while (i < paragraphs.Count)
            {
                if (paragraphs[i].StartsWith("||") && paragraphs[i].EndsWith("||"))
                {
                    string questionText = paragraphs[i].TrimStart('|').TrimEnd('|').Trim();
                    if (i + 4 >= paragraphs.Count)
                    {
                        throw new Exception($"Нокомӣ дар парсер кардани савол: '{questionText}' - камбуди вариантҳо.");
                    }

                    var options = new List<string>();
                    string correctAnswer = null;
                    for (int j = 1; j <= 4; j++)
                    {
                        string optionText = paragraphs[i + j].Trim();
                        int idx = optionText.IndexOf(")");
                        if (idx >= 0)
                        {
                            optionText = optionText.Substring(idx + 1).Trim();
                        }

                        if (optionText.EndsWith("--"))
                        {
                            correctAnswer = optionText.TrimEnd('-').Trim();
                        }

                        options.Add(optionText.Replace("--", "").Trim());
                    }

                    if (string.IsNullOrEmpty(correctAnswer))
                    {
                        throw new Exception($"Ҷавоби дуруст барои савол '{questionText}' ёфт нашуд.");
                    }

                    var questionDto = new QuestionDTO
                    {
                        QuestionText = questionText,
                        SubjectId = subjectId,
                        OptionA = options[0],
                        OptionB = options[1],
                        OptionC = options[2],
                        OptionD = options[3],
                        CorrectAnswer = correctAnswer
                    };

                    questions.Add(questionDto);
                    i += 5;
                }
                else
                {
                    i++;
                }
            }

            if (questions.Count == 0)
            {
                throw new Exception("Дар файл ягон савол бо аломати || ёфт нашуд");
            }
        }

        return questions;
    }
} 