using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using MMT.Application.Common.DTOs;
using MMT.Application.Common.Interfaces.Services;
using UglyToad.PdfPig;

namespace MMT.Application.Services;

public class DocumentParserService : IDocumentParserService
{
    public async Task<List<ParsedQuestion>> ParseDocumentAsync(byte[] fileContent, string fileExtension, CancellationToken ct = default)
    {
        return fileExtension.ToLower() switch
        {
            ".docx" => await ParseDocxAsync(fileContent, ct),
            ".pdf" => await ParsePdfAsync(fileContent, ct),
            ".doc" => throw new NotSupportedException("Формати .doc дастгирӣ намешавад. Лутфан .docx истифода баред."),
            _ => throw new NotSupportedException($"Формати {fileExtension} дастгирӣ намешавад.")
        };
    }

    private async Task<List<ParsedQuestion>> ParseDocxAsync(byte[] fileContent, CancellationToken ct)
    {
        await Task.CompletedTask;
        
        using var stream = new MemoryStream(fileContent);
        using var doc = WordprocessingDocument.Open(stream, false);
        
        var body = doc.MainDocumentPart?.Document.Body;
        if (body == null)
            return new List<ParsedQuestion>();

        var text = body.InnerText;
        return ParseQuestions(text);
    }

    private async Task<List<ParsedQuestion>> ParsePdfAsync(byte[] fileContent, CancellationToken ct)
    {
        await Task.CompletedTask;
        
        using var stream = new MemoryStream(fileContent);
        using var document = PdfDocument.Open(stream);
        
        var sb = new StringBuilder();
        foreach (var page in document.GetPages())
        {
            sb.AppendLine(page.Text);
        }

        return ParseQuestions(sb.ToString());
    }

    private List<ParsedQuestion> ParseQuestions(string text)
    {
        var questions = new List<ParsedQuestion>();
        
        var questionPattern = @"\|\|\s*(.+?)\s*\|\|";
        var questionMatches = Regex.Matches(text, questionPattern, RegexOptions.Singleline);

        foreach (Match questionMatch in questionMatches)
        {
            try
            {
                var questionText = questionMatch.Groups[1].Value.Trim();
                var startIndex = questionMatch.Index + questionMatch.Length;
                var nextQuestionIndex = text.IndexOf("||", startIndex + 1);
                if (nextQuestionIndex == -1) nextQuestionIndex = text.Length;
                
                var optionsText = text.Substring(startIndex, nextQuestionIndex - startIndex);
                
                var parsedQuestion = ParseOptions(questionText, optionsText);
                if (parsedQuestion != null)
                {
                    questions.Add(parsedQuestion);
                }
            }
            catch
            {
                continue;
            }
        }

        return questions;
    }

    private ParsedQuestion? ParseOptions(string questionText, string optionsText)
    {
        var optionPattern = @"([АВСD])\)\s*(.+?)(\s*--)?(?=\s*[АВСD]\)|\s*\|\||$)";
        var optionMatches = Regex.Matches(optionsText, optionPattern, RegexOptions.Singleline);

        if (optionMatches.Count < 4)
            return null;

        var question = new ParsedQuestion { QuestionText = questionText };
        string? correctAnswer = null;

        foreach (Match match in optionMatches)
        {
            var letter = match.Groups[1].Value;
            var answerText = match.Groups[2].Value.Trim();
            var isCorrect = match.Groups[3].Success;

            var latinLetter = letter switch
            {
                "А" => "A",
                "В" => "B",
                "С" => "C",
                "D" => "D",
                _ => letter
            };

            switch (latinLetter)
            {
                case "A":
                    question.OptionA = answerText;
                    break;
                case "B":
                    question.OptionB = answerText;
                    break;
                case "C":
                    question.OptionC = answerText;
                    break;
                case "D":
                    question.OptionD = answerText;
                    break;
            }

            if (isCorrect)
                correctAnswer = latinLetter;
        }

        if (string.IsNullOrEmpty(correctAnswer))
            return null;

        question.CorrectAnswer = correctAnswer;

        if (string.IsNullOrEmpty(question.OptionA) || 
            string.IsNullOrEmpty(question.OptionB) ||
            string.IsNullOrEmpty(question.OptionC) || 
            string.IsNullOrEmpty(question.OptionD))
            return null;

        return question;
    }
}
