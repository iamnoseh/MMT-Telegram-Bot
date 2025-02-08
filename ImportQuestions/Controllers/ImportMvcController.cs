using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using data;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using TelegramBot.Domain.Entities;

namespace TelegramBot.API.Controllers;

public class ImportMvcController : Controller
{
    private readonly DataContext _context;

    public ImportMvcController(DataContext context)
    {
        _context = context;
    }

    // Саҳифаи асосӣ, ки мавзуи импорти файл нишон дода мешавад.
    public IActionResult Index()
    {
        return View();
    }

    // Акшн барои гирифтани файл тавассути боргузорӣ ва парс кардан
    [HttpPost]
    public async Task<IActionResult> Upload(IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            TempData["Message"] = "❌ Файл интихоб нашудааст!";
            return RedirectToAction("Index");
        }

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
            TempData["Message"] = $"❌ Хатогӣ: {ex.Message}";
            return RedirectToAction("Index");
        }

        if (questions == null || !questions.Any())
        {
            TempData["Message"] = "⚠️ Ҳеҷ саволе дар файл ёфт нашуд!";
            return RedirectToAction("Index");
        }

        try
        {
            await _context.Questions.AddRangeAsync(questions);
            await _context.SaveChangesAsync();
            TempData["Message"] = "✅ Саволҳо бо муваффақият ворид шуданд!";
        }
        catch (Exception ex)
        {
            TempData["Message"] = $"❌ Хатогӣ ҳангоми сабт: {ex.Message}";
        }

        return RedirectToAction("Index");
    }
    private List<Question> ParseQuestionsFromDocx(Stream docxStream)
    {
        List<Question> questions = new List<Question>();

        using (WordprocessingDocument wordDoc = WordprocessingDocument.Open(docxStream, false))
        {
            // Гирифтани ҳамаи параграфҳо ва бартараф кардани сатрҳои холӣ
            var paragraphs = wordDoc.MainDocumentPart.Document.Body
                               .Elements<Paragraph>()
                               .Select(p => p.InnerText)
                               .Where(text => !string.IsNullOrWhiteSpace(text))
                               .ToList();

            int index = 0;
            while (index < paragraphs.Count)
            {
                // Агар сатр танҳо рақам бошад (нумерация), онро гиранд
                if (int.TryParse(paragraphs[index].Trim(), out _))
                {
                    index++;
                    continue;
                }

                // Ҷамоварии сатрҳои савол: ҷамъ мекунем ҳамаи сатрҳо то вақт ки сатр ба формати вариант (option) нарасад
                string questionText = "";
                while (index < paragraphs.Count && !IsOptionLine(paragraphs[index]))
                {
                    questionText += paragraphs[index].Trim() + " ";
                    index++;
                }
                questionText = questionText.Trim();

                // Агар дар мавриди савол камтар аз 4 сатр барои вариантҳо ёфт шавад, аз рӯи формат, методро пок карда (ё exception барорад)
                if (index + 4 > paragraphs.Count)
                    break;  // ё метавонем exception пардохт

                // Парс кардани 4 вариант
                string[] optionLines = new string[4];
                int correctIndex = -1;
                for (int i = 0; i < 4; i++)
                {
                    string line = paragraphs[index].Trim();
                    index++;

                    // Гузоштан аз пешисоз (prefix) монанди "А)" ё "В)" ва ғайра
                    int prefixIndex = line.IndexOf(")");
                    if (prefixIndex >= 0)
                    {
                        line = line.Substring(prefixIndex + 1).Trim();
                    }

                    // Агар хатт бо "--" тамом шавад, ин вариант ҷавоби дуруст мебошад
                    if (line.EndsWith("--"))
                    {
                        line = line.Substring(0, line.Length - 2).Trim();
                        correctIndex = i;
                    }
                    optionLines[i] = line;
                }

                if (correctIndex == -1)
                    throw new Exception("Ҷавоби дуруст ёфт нашуд барои савол: " + questionText);

                Option option = new Option
                {
                    FirstVariant = optionLines[0],
                    SecondVariant = optionLines[1],
                    ThirdVariant = optionLines[2],
                    FourthVariant = optionLines[3],
                    Answer = optionLines[correctIndex]
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

    /// <summary>
    /// Проверка, ки оё сатр ба формати вариант аст. Мисол: бояд бо "А)" ё "В)" ё "С)" ё "D)" оғоз шавад.
    /// </summary>
    private bool IsOptionLine(string line)
    {
        line = line.Trim();
        return line.StartsWith("А)") || line.StartsWith("A)") ||
               line.StartsWith("В)") || line.StartsWith("B)") ||
               line.StartsWith("С)") || line.StartsWith("C)") ||
               line.StartsWith("D)") || line.StartsWith("Д)");
    }
}

