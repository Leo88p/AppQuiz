using AppQuiz.Data;
using AppQuiz.Models;
using AppQuiz.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AppQuiz.Pages
{
    public class QuizModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<QuizModel> _logger;
        private readonly OllamaService _ollamaService;

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

        [BindProperty(SupportsGet = true)]
        public string CorrectAnswer { get; set; } = string.Empty;

        [BindProperty(SupportsGet = true)]
        public double SimilarityScore { get; set; }

        public List<Question> QuizQuestions { get; set; } = new List<Question>();
        public Question CurrentQuestion { get; set; }
        public string TopicName { get; set; } = string.Empty;
        public int TotalQuestions { get; set; }
        public bool IsCorrect { get; set; }

        [BindProperty]
        public string SelectedEmbeddingModel { get; set; }

        // Список доступных моделей для выбора
        public List<SelectListItem> AvailableEmbeddingModels { get; } = new List<SelectListItem>
        {
            new SelectListItem { Value = "nomic-embed-text", Text = "Nomic Embed Text" },
            new SelectListItem { Value = "all-minilm", Text = "All MiniLM" },
            new SelectListItem { Value = "mxbai-embed-large", Text = "MXBAI Embed Large" }
        };


        public QuizModel(ApplicationDbContext context, ILogger<QuizModel> logger, OllamaService ollamaService)
        {
            _context = context;
            _logger = logger;
            _ollamaService = ollamaService;
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

                // Получаем данные из сессии - это основной источник правды
                var quizQuestionsJson = HttpContext.Session.GetString("QuizQuestions");
                var selectedTopic = HttpContext.Session.GetString("SelectedTopic");
                var questionCountStr = HttpContext.Session.GetString("QuestionCount");
                var savedScore = HttpContext.Session.GetInt32("Score");
                var savedModel = HttpContext.Session.GetString("SelectedEmbeddingModel");
                if (!string.IsNullOrEmpty(savedModel) && AvailableEmbeddingModels.Any(m => m.Value == savedModel))
                {
                    SelectedEmbeddingModel = savedModel;
                }

                if (string.IsNullOrEmpty(quizQuestionsJson) || string.IsNullOrEmpty(selectedTopic) ||
                    string.IsNullOrEmpty(questionCountStr) || !savedScore.HasValue)
                {
                    // Если данных в сессии нет, и это начало викторины
                    if (CurrentQuestionIndex == 0 && !IsComplete)
                    {
                        _logger.LogInformation("Начало новой викторины");

                        // Получение данных из TempData (только для начала викторины)
                        selectedTopic = GetFromTempData<string>("SelectedTopic", null);
                        questionCountStr = GetFromTempData<string>("QuestionCount", null);

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

                        // Сохраняем данные в сессии - это критически важно
                        HttpContext.Session.SetString("QuizQuestions", System.Text.Json.JsonSerializer.Serialize(QuizQuestions));
                        HttpContext.Session.SetString("SelectedTopic", selectedTopic);
                        HttpContext.Session.SetString("QuestionCount", questionCount.ToString());
                        HttpContext.Session.SetInt32("Score", 0);
                        Score = 0;
                    }
                    else
                    {
                        // Если данных в сессии нет, и это не начало викторины - данные утеряны
                        _logger.LogWarning("Данные викторины не найдены в сессии");
                        TempData["ErrorMessage"] = "Ваша викторина была прервана. Пожалуйста, начните заново.";
                        return RedirectToPage("/Index");
                    }
                }
                else
                {
                    // Восстанавливаем данные из сессии
                    QuizQuestions = System.Text.Json.JsonSerializer.Deserialize<List<Question>>(quizQuestionsJson);
                    Score = savedScore.Value;
                    TopicName = GetTopicName(selectedTopic);
                }

                // Проверяем, завершена ли викторина
                if (IsComplete || CurrentQuestionIndex >= (QuizQuestions?.Count ?? 0))
                {
                    // Викторина завершена
                    IsComplete = true;
                    TotalQuestions = QuizQuestions?.Count ?? 0;
                    TopicName = GetTopicName(selectedTopic ?? "unknown");
                    _logger.LogInformation($"Викторина завершена. Счет: {Score} из {TotalQuestions}");
                    return Page();
                }

                // Устанавливаем текущий вопрос
                if (QuizQuestions != null && CurrentQuestionIndex < QuizQuestions.Count)
                {
                    CurrentQuestion = QuizQuestions[CurrentQuestionIndex];
                }

                TotalQuestions = QuizQuestions?.Count ?? 0;
                TopicName = GetTopicName(selectedTopic ?? "unknown");

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

        public async Task<IActionResult> OnPost()
        {
            try
            {
                _logger.LogInformation("Обработка POST-запроса на странице Quiz");

                // Восстанавливаем данные из сессии
                var quizQuestionsJson = HttpContext.Session.GetString("QuizQuestions");
                if (string.IsNullOrEmpty(quizQuestionsJson))
                {
                    _logger.LogError("Вопросы не найдены в сессии при POST-запросе");
                    TempData["ErrorMessage"] = "Данные викторины утеряны. Пожалуйста, начните заново.";
                    return RedirectToPage("/Index");
                }

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

                // Получаем текущий счет из сессии
                var savedScore = HttpContext.Session.GetInt32("Score") ?? 0;
                Score = savedScore;

                // Получаем текущий вопрос
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
                _logger.LogInformation($"Выбранная модель эмбеддингов: {SelectedEmbeddingModel}");

                try
                {
                    // Теперь всегда вычисляем сходство, независимо от правильности ответа
                    SimilarityScore = await _ollamaService.GetAnswerSimilarityAsync(
                        userAnswer,
                        correctAnswer,
                        SelectedEmbeddingModel
                    );
                    _logger.LogInformation($"Сходство ответов с моделью {SelectedEmbeddingModel}: {SimilarityScore:P2}");

                    // Определяем правильность на основе сходства (порог 0.75)
                    IsCorrect = SimilarityScore >= 0.75;

                    // Проверяем, был ли этот вопрос уже правильно отвечен
                    var questionKey = $"Question_{CurrentQuestionIndex}_AnsweredCorrectly";
                    var wasAlreadyCorrect = HttpContext.Session.GetInt32(questionKey) == 1;

                    if (IsCorrect && !wasAlreadyCorrect)
                    {
                        Score++;
                        HttpContext.Session.SetInt32(questionKey, 1); // Отмечаем вопрос как правильно отвеченный
                        _logger.LogInformation($"Ответ правильный (сходство {SimilarityScore:P2} выше порога 75%). Счет увеличен до {Score}.");
                    }
                    else if (IsCorrect && wasAlreadyCorrect)
                    {
                        _logger.LogInformation($"Ответ правильный, но уже был засчитан ранее. Счет остался {Score}.");
                    }
                    else
                    {
                        _logger.LogInformation($"Ответ неправильный (сходство {SimilarityScore:P2} ниже порога 75%). Счет остался {Score}.");
                    }
                    HttpContext.Session.SetString("SelectedEmbeddingModel", SelectedEmbeddingModel);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Ошибка при работе с Ollama, используем традиционную проверку");
                    // При ошибке Ollama используем традиционную проверку
                    IsCorrect = string.Equals(userAnswer, correctAnswer, StringComparison.OrdinalIgnoreCase);
                    SimilarityScore = IsCorrect ? 1.0 : 0.0;

                    // Проверяем, был ли этот вопрос уже правильно отвечен
                    var questionKey = $"Question_{CurrentQuestionIndex}_AnsweredCorrectly";
                    var wasAlreadyCorrect = HttpContext.Session.GetInt32(questionKey) == 1;

                    if (IsCorrect && !wasAlreadyCorrect)
                    {
                        Score++;
                        HttpContext.Session.SetInt32(questionKey, 1); // Отмечаем вопрос как правильно отвеченный
                        _logger.LogInformation($"Ответ правильный (традиционная проверка). Счет увеличен до {Score}.");
                    }
                    else if (IsCorrect && wasAlreadyCorrect)
                    {
                        _logger.LogInformation($"Ответ правильный (традиционная проверка), но уже был засчитан ранее. Счет остался {Score}.");
                    }
                    else
                    {
                        _logger.LogInformation($"Ответ неправильный (традиционная проверка). Счет остался {Score}.");
                    }
                }

                // Сохраняем обновленный счет в сессии
                HttpContext.Session.SetInt32("Score", Score);

                // ВСЕГДА сохраняем правильный ответ для отображения
                CorrectAnswer = correctAnswer;

                // Получение темы из сессии
                var selectedTopic = HttpContext.Session.GetString("SelectedTopic") ?? "unknown";
                TopicName = GetTopicName(selectedTopic);
                TotalQuestions = QuizQuestions.Count;

                // Устанавливаем флаг отображения результата
                ShowResult = true;

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
        public IActionResult OnPostRetryQuestion()
        {
            try
            {
                _logger.LogInformation($"Пользователь хочет повторить вопрос #{CurrentQuestionIndex + 1}");

                // Восстанавливаем данные из сессии
                var quizQuestionsJson = HttpContext.Session.GetString("QuizQuestions");
                if (string.IsNullOrEmpty(quizQuestionsJson))
                {
                    _logger.LogError("Вопросы не найдены в сессии при попытке повтора вопроса");
                    TempData["ErrorMessage"] = "Данные викторины утеряны. Пожалуйста, начните заново.";
                    return RedirectToPage("/Index");
                }

                // Важно: сохраняем ВСЕ необходимые данные в сессию перед перенаправлением
                var selectedTopic = HttpContext.Session.GetString("SelectedTopic") ?? "unknown";
                var questionCount = HttpContext.Session.GetString("QuestionCount") ?? "0";

                // Восстанавливаем текущий счет из сессии
                var savedScore = HttpContext.Session.GetInt32("Score");
                if (savedScore.HasValue)
                {
                    HttpContext.Session.SetInt32("Score", savedScore.Value);
                }

                // Перенаправляем с параметрами, но данные хранятся в сессии
                return RedirectToPage(new
                {
                    currentQuestionIndex = CurrentQuestionIndex,
                    showResult = false,
                    isComplete = false
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при повторе вопроса: {ex.Message}");
                TempData["ErrorMessage"] = $"Произошла ошибка при повторе вопроса: {ex.Message}";
                return RedirectToPage("/Error");
            }
        }
    }
}