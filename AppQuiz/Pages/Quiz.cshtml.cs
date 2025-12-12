using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using AppQuiz.Data;
using AppQuiz.Models;
using System.Collections.Generic;
using System.Linq;
using System;
using Microsoft.Extensions.Logging;

namespace AppQuiz.Pages
{
    public class QuizModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<QuizModel> _logger;

        [BindProperty(SupportsGet = true)]
        public int CurrentQuestionIndex { get; set; } = 0;

        [BindProperty(SupportsGet = true)]
        public int Score { get; set; } = 0;

        [BindProperty]
        public string UserAnswer { get; set; }

        [BindProperty(SupportsGet = true)]
        public bool ShowResult { get; set; } = false;

        [BindProperty(SupportsGet = true)]
        public bool IsComplete { get; set; } = false;

        public List<Question> QuizQuestions { get; set; } = new List<Question>();
        public Question CurrentQuestion { get; set; }
        public string TopicName { get; set; } = string.Empty;
        public int TotalQuestions { get; set; }
        public bool IsCorrect { get; set; }
        public string CorrectAnswer { get; set; } = string.Empty;

        public QuizModel(ApplicationDbContext context, ILogger<QuizModel> logger)
        {
            _context = context;
            _logger = logger;
            QuizQuestions = new List<Question>(); // Инициализация списка вопросов
        }

        private T GetFromTempData<T>(string key, T defaultValue)
        {
            try
            {
                if (TempData[key] != null)
                {
                    if (typeof(T) == typeof(int) && int.TryParse(TempData[key].ToString(), out int intValue))
                        return (T)(object)intValue;

                    if (typeof(T) == typeof(string))
                        return (T)(object)TempData[key].ToString();

                    return (T)TempData[key];
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"Ошибка при получении значения из TempData для ключа {key}");
            }

            return defaultValue;
        }

        public IActionResult OnGet()
        {
            try
            {
                _logger.LogInformation("Начало загрузки страницы Quiz");

                // Проверка авторизации пользователя
                if (User.Identity?.IsAuthenticated != true)
                {
                    _logger.LogWarning("Пользователь не авторизован, перенаправление на страницу входа");
                    TempData["ErrorMessage"] = "Пожалуйста, войдите в систему для прохождения викторины.";
                    return RedirectToPage("/Login");
                }

                // Если это начало викторины, формируем список вопросов
                if (CurrentQuestionIndex == 0 && !IsComplete)
                {
                    _logger.LogInformation("Начало новой викторины");

                    // Получение данных из TempData
                    var selectedTopic = GetFromTempData<string>("SelectedTopic", null);
                    var questionCountStr = GetFromTempData<string>("QuestionCount", null);

                    if (string.IsNullOrEmpty(selectedTopic) || string.IsNullOrEmpty(questionCountStr))
                    {
                        _logger.LogWarning("Отсутствуют данные о выбранной теме или количестве вопросов");
                        TempData["ErrorMessage"] = "Не удалось загрузить параметры викторины. Пожалуйста, выберите тему заново.";
                        return RedirectToPage("/Index");
                    }

                    if (!int.TryParse(questionCountStr, out int questionCount))
                    {
                        _logger.LogError($"Некорректное значение количества вопросов: {questionCountStr}");
                        TempData["ErrorMessage"] = "Некорректное количество вопросов. Пожалуйста, выберите тему заново.";
                        return RedirectToPage("/Index");
                    }

                    // Загрузка вопросов из базы данных
                    var allQuestions = _context.Questions
                        .Where(q => q.Topic == selectedTopic)
                        .ToList();

                    _logger.LogInformation($"Найдено вопросов по теме {selectedTopic}: {allQuestions.Count}");

                    if (allQuestions.Count < questionCount)
                    {
                        _logger.LogError($"Недостаточно вопросов по теме {selectedTopic}. Доступно: {allQuestions.Count}, требуется: {questionCount}");
                        TempData["ErrorMessage"] = $"Недостаточно вопросов по теме {GetTopicName(selectedTopic)}. Доступно только {allQuestions.Count}.";
                        return RedirectToPage("/Index");
                    }

                    // Выбираем случайные вопросы
                    var random = new Random();
                    QuizQuestions = allQuestions
                        .OrderBy(q => random.Next())
                        .Take(questionCount)
                        .ToList();

                    _logger.LogInformation($"Сформирован список из {QuizQuestions.Count} случайных вопросов");

                    // Сохраняем данные в сессии
                    HttpContext.Session.SetString("QuizQuestions", System.Text.Json.JsonSerializer.Serialize(QuizQuestions));
                    HttpContext.Session.SetString("SelectedTopic", selectedTopic);
                    HttpContext.Session.SetString("QuestionCount", questionCount.ToString());
                    HttpContext.Session.SetInt32("Score", 0);
                }
                else
                {
                    // Восстанавливаем данные из сессии
                    var quizQuestionsJson = HttpContext.Session.GetString("QuizQuestions");
                    if (string.IsNullOrEmpty(quizQuestionsJson))
                    {
                        _logger.LogWarning("Викторина не найдена в сессии");
                        TempData["ErrorMessage"] = "Ваша викторина была прервана. Пожалуйста, начните заново.";
                        return RedirectToPage("/Index");
                    }

                    QuizQuestions = System.Text.Json.JsonSerializer.Deserialize<List<Question>>(quizQuestionsJson);
                    var savedScore = HttpContext.Session.GetInt32("Score");
                    Score = savedScore ?? 0;
                }

                if (IsComplete || CurrentQuestionIndex >= QuizQuestions.Count)
                {
                    // Викторина завершена
                    IsComplete = true;
                    TotalQuestions = QuizQuestions.Count;
                    TopicName = GetTopicName(HttpContext.Session.GetString("SelectedTopic") ?? "unknown");
                    _logger.LogInformation($"Викторина завершена. Счет: {Score} из {TotalQuestions}");
                    return Page();
                }

                // Устанавливаем текущий вопрос
                CurrentQuestion = QuizQuestions[CurrentQuestionIndex];
                TopicName = GetTopicName(HttpContext.Session.GetString("SelectedTopic") ?? "unknown");
                TotalQuestions = QuizQuestions.Count;

                _logger.LogInformation($"Загружен вопрос #{CurrentQuestionIndex + 1}: {CurrentQuestion?.Text?.Substring(0, Math.Min(50, CurrentQuestion?.Text?.Length ?? 0))}...");
                return Page();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при загрузке страницы Quiz: {ex.Message}");
                TempData["ErrorMessage"] = $"Произошла ошибка при загрузке викторины: {ex.Message}";
                return RedirectToPage("/Error");
            }
        }

        public IActionResult OnPost()
        {
            try
            {
                _logger.LogInformation("Обработка POST-запроса на странице Quiz");

                // Проверка наличия вопросов в сессии
                var quizQuestionsJson = HttpContext.Session.GetString("QuizQuestions");
                if (string.IsNullOrEmpty(quizQuestionsJson))
                {
                    _logger.LogError("Вопросы не найдены в сессии при POST-запросе");
                    TempData["ErrorMessage"] = "Данные викторины утеряны. Пожалуйста, начните заново.";
                    return RedirectToPage("/Index");
                }

                // Десериализация вопросов
                QuizQuestions = System.Text.Json.JsonSerializer.Deserialize<List<Question>>(quizQuestionsJson);
                if (QuizQuestions == null || QuizQuestions.Count == 0)
                {
                    _logger.LogError("Не удалось десериализовать вопросы из сессии или список пуст");
                    TempData["ErrorMessage"] = "Ошибка загрузки вопросов. Пожалуйста, начните заново.";
                    return RedirectToPage("/Index");
                }

                // Проверка текущего индекса вопроса
                if (CurrentQuestionIndex < 0 || CurrentQuestionIndex >= QuizQuestions.Count)
                {
                    _logger.LogError($"Некорректный индекс вопроса: {CurrentQuestionIndex}, всего вопросов: {QuizQuestions.Count}");
                    TempData["ErrorMessage"] = "Некорректный индекс вопроса. Пожалуйста, начните заново.";
                    return RedirectToPage("/Index");
                }

                // Получение текущего вопроса с проверкой на null
                CurrentQuestion = QuizQuestions[CurrentQuestionIndex];
                if (CurrentQuestion == null)
                {
                    _logger.LogError($"Вопрос с индексом {CurrentQuestionIndex} равен null");
                    TempData["ErrorMessage"] = "Вопрос не найден. Пожалуйста, начните заново.";
                    return RedirectToPage("/Index");
                }

                // Проверка ответа пользователя
                var userAnswer = UserAnswer?.Trim() ?? string.Empty;
                var correctAnswer = CurrentQuestion.Answer?.Trim() ?? string.Empty;

                _logger.LogInformation($"Пользователь ответил: '{userAnswer}', правильный ответ: '{correctAnswer}'");

                // Сравнение ответов без учета регистра
                if (string.Equals(userAnswer, correctAnswer, StringComparison.OrdinalIgnoreCase))
                {
                    Score++;
                    IsCorrect = true;
                    _logger.LogInformation("Ответ правильный");
                }
                else
                {
                    IsCorrect = false;
                    CorrectAnswer = correctAnswer;
                    _logger.LogInformation("Ответ неправильный");
                }

                // Устанавливаем флаг отображения результата
                ShowResult = true;

                // Получение темы из сессии с резервным вариантом
                var selectedTopic = HttpContext.Session.GetString("SelectedTopic") ?? "unknown";

                // Устанавливаем информацию для отображения
                TopicName = GetTopicName(selectedTopic);
                TotalQuestions = QuizQuestions.Count;

                // Сохраняем обновленные данные в сессию
                HttpContext.Session.SetString("QuizQuestions", System.Text.Json.JsonSerializer.Serialize(QuizQuestions));
                HttpContext.Session.SetInt32("Score", Score);
                HttpContext.Session.SetString("SelectedTopic", selectedTopic);

                _logger.LogInformation($"Текущий счет: {Score}, показываем результат: {ShowResult}");
                return Page();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Критическая ошибка при обработке POST-запроса: {ex.Message}");
                TempData["ErrorMessage"] = $"Произошла ошибка при обработке вашего ответа: {ex.Message}";
                return RedirectToPage("/Error");
            }
        }

        public IActionResult OnPostNextQuestion()
        {
            try
            {
                _logger.LogInformation("Переход к следующему вопросу");

                // Восстанавливаем данные из сессии
                var quizQuestionsJson = HttpContext.Session.GetString("QuizQuestions");
                if (string.IsNullOrEmpty(quizQuestionsJson))
                {
                    _logger.LogError("Вопросы не найдены в сессии при переходе к следующему вопросу");
                    return RedirectToPage("/Index");
                }

                QuizQuestions = System.Text.Json.JsonSerializer.Deserialize<List<Question>>(quizQuestionsJson);

                var savedScore = HttpContext.Session.GetInt32("Score");
                if (savedScore.HasValue)
                {
                    Score = savedScore.Value;
                }

                // Восстанавливаем тему и количество вопросов из TempData или сессии
                var selectedTopic = TempData["SelectedTopic"]?.ToString() ?? HttpContext.Session.GetString("SelectedTopic");
                var questionCount = TempData["QuestionCount"]?.ToString() ?? HttpContext.Session.GetString("QuestionCount");

                if (string.IsNullOrEmpty(selectedTopic) || string.IsNullOrEmpty(questionCount))
                {
                    _logger.LogError("Отсутствуют данные о теме или количестве вопросов");
                    return RedirectToPage("/Index");
                }

                CurrentQuestionIndex++;

                if (CurrentQuestionIndex >= (QuizQuestions?.Count ?? 0))
                {
                    IsComplete = true;
                    _logger.LogInformation("Викторина завершена");
                    return RedirectToPage(new { currentQuestionIndex = CurrentQuestionIndex, score = Score, isComplete = true });
                }

                ShowResult = false;

                // Сохраняем текущее состояние в сессии
                HttpContext.Session.SetString("SelectedTopic", selectedTopic);
                HttpContext.Session.SetString("QuestionCount", questionCount);
                HttpContext.Session.SetInt32("Score", Score);

                // Важно: передаем все необходимые параметры для следующего запроса
                return RedirectToPage(new
                {
                    currentQuestionIndex = CurrentQuestionIndex,
                    score = Score,
                    showResult = false,
                    isComplete = false
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при переходе к следующему вопросу: {ex.Message}");
                return RedirectToPage("/Error");
            }
        }

        public IActionResult OnPostFinishQuiz()
        {
            // Очищаем сессию после завершения викторины
            HttpContext.Session.Remove("QuizQuestions");
            HttpContext.Session.Remove("Score");

            return RedirectToPage("/Index");
        }

        private string GetTopicName(string topicCode)
        {
            return topicCode switch
            {
                "biology" => "Биология",
                "geography" => "География",
                "history" => "История",
                "music" => "Музыкальная литература",
                _ => "Неизвестная тема"
            };
        }
    }
}