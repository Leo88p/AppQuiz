using AppQuiz.Data;
using AppQuiz.Models;
using AppQuiz.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Pgvector;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace AppQuiz.Pages
{
    public class QuizModel : PageModel
    {
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

        [BindProperty]
        public string SelectedEmbeddingModel { get; set; } = "nomic-embed-text";

        public List<SelectListItem> AvailableEmbeddingModels { get; } = new List<SelectListItem>
    {
        new SelectListItem { Value = "nomic-embed-text", Text = "Nomic Embed Text" },
        new SelectListItem { Value = "all-minilm", Text = "All MiniLM" },
        new SelectListItem { Value = "mxbai-embed-large", Text = "MXBAI Embed Large" }
    };

        public Question CurrentQuestion { get; set; }
        public string TopicName { get; set; } = string.Empty;
        public int TotalQuestions { get; set; }
        public bool IsCorrect { get; set; }
        public string CorrectAnswer { get; set; } = string.Empty;
        public double SimilarityScore { get; set; }

        // Список ID вопросов для текущей викторины
        private List<int> _questionIds = new List<int>();

        private readonly ApplicationDbContext _context;
        private readonly ILogger<QuizModel> _logger;
        private readonly OllamaService _ollamaService;

        [BindProperty]
        public string SelectedDistanceFunction { get; set; } = "cosine";

        public List<SelectListItem> AvailableDistanceFunctions { get; } = new List<SelectListItem>
        {
            new SelectListItem { Value = "cosine", Text = "Косинусное расстояние" },
            new SelectListItem { Value = "l2", Text = "Евклидово расстояние (L2)" },
            new SelectListItem { Value = "inner_product", Text = "Скалярное произведение" }
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

                if (User.Identity?.IsAuthenticated != true)
                {
                    _logger.LogWarning("Пользователь не авторизован, перенаправление на страницу входа");
                    TempData["ErrorMessage"] = "Пожалуйста, войдите в систему для прохождения викторины.";
                    return RedirectToPage("/Login");
                }

                // Восстанавливаем ID вопросов из сессии
                var questionIdsJson = HttpContext.Session.GetString("QuizQuestionIds");
                if (!string.IsNullOrEmpty(questionIdsJson))
                {
                    _questionIds = JsonSerializer.Deserialize<List<int>>(questionIdsJson);
                    _logger.LogInformation($"Восстановлено {_questionIds.Count} вопросов из сессии");
                }

                // Если это начало викторины, формируем новый список вопросов
                if (CurrentQuestionIndex == 0 && !IsComplete && _questionIds.Count == 0)
                {
                    _logger.LogInformation("Начало новой викторины");

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
                    _questionIds = allQuestions
                        .OrderBy(q => random.Next())
                        .Take(questionCount)
                        .Select(q => q.Id)
                        .ToList();

                    _logger.LogInformation($"Сформирован список из {_questionIds.Count} случайных вопросов");

                    // Сохраняем только ID вопросов в сессию
                    HttpContext.Session.SetString("QuizQuestionIds", JsonSerializer.Serialize(_questionIds));
                    HttpContext.Session.SetString("SelectedTopic", selectedTopic);
                    HttpContext.Session.SetInt32("Score", 0);
                    Score = 0;
                }

                // Восстанавливаем счет из сессии
                var savedScore = HttpContext.Session.GetInt32("Score");
                if (savedScore.HasValue)
                {
                    Score = savedScore.Value;
                }

                // Восстанавливаем выбранную модель из сессии
                var savedModel = HttpContext.Session.GetString("SelectedEmbeddingModel");
                if (!string.IsNullOrEmpty(savedModel) && AvailableEmbeddingModels.Any(m => m.Value == savedModel))
                {
                    SelectedEmbeddingModel = savedModel;
                }
                var savedDistanceFunction = HttpContext.Session.GetString("SelectedDistanceFunction");
                if (!string.IsNullOrEmpty(savedDistanceFunction) && AvailableDistanceFunctions.Any(m => m.Value == savedDistanceFunction))
                {
                    SelectedDistanceFunction = savedDistanceFunction;
                }

                if (IsComplete || CurrentQuestionIndex >= _questionIds.Count)
                {
                    IsComplete = true;
                    TotalQuestions = _questionIds.Count;
                    TopicName = GetTopicName(HttpContext.Session.GetString("SelectedTopic") ?? "unknown");
                    _logger.LogInformation($"Викторина завершена. Счет: {Score} из {TotalQuestions}");
                    return Page();
                }

                // Получаем текущий вопрос из базы данных по ID
                var currentQuestionId = _questionIds[CurrentQuestionIndex];
                CurrentQuestion = _context.Questions.FirstOrDefault(q => q.Id == currentQuestionId);

                if (CurrentQuestion == null)
                {
                    _logger.LogError($"Вопрос с ID {currentQuestionId} не найден в базе данных");
                    TempData["ErrorMessage"] = "Вопрос не найден. Пожалуйста, начните заново.";
                    return RedirectToPage("/Index");
                }

                TopicName = GetTopicName(HttpContext.Session.GetString("SelectedTopic") ?? "unknown");
                TotalQuestions = _questionIds.Count;

                _logger.LogInformation($"Загружен вопрос #{CurrentQuestionIndex + 1}: {CurrentQuestion.Text.Substring(0, Math.Min(50, CurrentQuestion.Text.Length))}...");
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

                // Восстанавливаем ID вопросов из сессии
                var questionIdsJson = HttpContext.Session.GetString("QuizQuestionIds");
                if (string.IsNullOrEmpty(questionIdsJson))
                {
                    _logger.LogError("ID вопросов не найдены в сессии при POST-запросе");
                    TempData["ErrorMessage"] = "Данные викторины утеряны. Пожалуйста, начните заново.";
                    return RedirectToPage("/Index");
                }

                // УДАЛЯЕМ СТАРЫЙ КОД, КОТОРЫЙ ДЕСЕРИАЛИЗУЕТ ВОПРОСЫ
                // Это был источник ошибки:
                // QuizQuestions = System.Text.Json.JsonSerializer.Deserialize<List<Question>>(quizQuestionsJson);

                _questionIds = JsonSerializer.Deserialize<List<int>>(questionIdsJson);
                if (_questionIds == null || _questionIds.Count == 0)
                {
                    _logger.LogError("Не удалось десериализовать ID вопросов из сессии или список пуст");
                    TempData["ErrorMessage"] = "Ошибка загрузки вопросов. Пожалуйста, начните заново.";
                    return RedirectToPage("/Index");
                }

                // Проверка текущего индекса вопроса
                if (CurrentQuestionIndex < 0 || CurrentQuestionIndex >= _questionIds.Count)
                {
                    _logger.LogError($"Некорректный индекс вопроса: {CurrentQuestionIndex}, всего вопросов: {_questionIds.Count}");
                    TempData["ErrorMessage"] = "Некорректный индекс вопроса. Пожалуйста, начните заново.";
                    return RedirectToPage("/Index");
                }

                // Получаем текущий счет из сессии
                var savedScore = HttpContext.Session.GetInt32("Score") ?? 0;
                Score = savedScore;

                // Получаем текущий вопрос из базы данных по ID
                var currentQuestionId = _questionIds[CurrentQuestionIndex];
                CurrentQuestion = await _context.Questions
                    .FirstOrDefaultAsync(q => q.Id == currentQuestionId);

                if (CurrentQuestion == null)
                {
                    _logger.LogError($"Вопрос с ID {currentQuestionId} не найден в базе данных");
                    TempData["ErrorMessage"] = "Вопрос не найден. Пожалуйста, начните заново.";
                    return RedirectToPage("/Index");
                }

                // Проверка ответа пользователя
                var userAnswer = UserAnswer?.Trim() ?? string.Empty;
                var correctAnswer = CurrentQuestion.Answer?.Trim() ?? string.Empty;

                _logger.LogInformation($"Пользователь ответил: '{userAnswer}', правильный ответ: '{correctAnswer}'");
                _logger.LogInformation($"Выбранная модель эмбеддингов: {SelectedEmbeddingModel}, функция расстояния: {SelectedDistanceFunction}");

                try
                {
                    // Получаем эмбеддинг пользователя на лету
                    var userEmbedding = await _ollamaService.GetEmbeddingAsync(userAnswer, SelectedEmbeddingModel);

                    if (userEmbedding.Length == 0)
                    {
                        throw new Exception("Не удалось получить эмбеддинг для ответа пользователя");
                    }

                    // Получаем предварительно сохраненный эмбеддинг правильного ответа из базы данных
                    Vector? storedCorrectEmbedding = null;
                    switch (SelectedEmbeddingModel)
                    {
                        case "nomic-embed-text":
                            storedCorrectEmbedding = CurrentQuestion.NomicEmbedTextEmbedding;
                            break;
                        case "all-minilm":
                            storedCorrectEmbedding = CurrentQuestion.AllMiniLMEmbedding;
                            break;
                        case "mxbai-embed-large":
                            storedCorrectEmbedding = CurrentQuestion.MxbaiEmbedLargeEmbedding;
                            break;
                        default:
                            storedCorrectEmbedding = CurrentQuestion.NomicEmbedTextEmbedding;
                            break;
                    }

                    if (storedCorrectEmbedding == null || storedCorrectEmbedding.ToArray().Length == 0)
                    {
                        // Если эмбеддинг не сохранен в базе, генерируем его на лету (резервный вариант)
                        _logger.LogWarning($"Предварительно рассчитанный эмбеддинг для вопроса {CurrentQuestion.Id} не найден, генерируем на лету");
                        var correctEmbedding = await _ollamaService.GetEmbeddingAsync(correctAnswer, SelectedEmbeddingModel);
                        storedCorrectEmbedding = new Vector(correctEmbedding);
                    }

                    var correctEmbeddingArray = storedCorrectEmbedding.ToArray();

                    _logger.LogInformation($"Используется предварительно рассчитанный эмбеддинг для правильного ответа");

                    // Вычисляем расстояние/сходство в зависимости от выбранной функции
                    double similarityScore = 0.0;
                    double distance = 0.0;

                    switch (SelectedDistanceFunction.ToLower())
                    {
                        case "cosine":
                            similarityScore = OllamaService.CalculateCosineSimilarity(userEmbedding, correctEmbeddingArray);
                            _logger.LogInformation($"Косинусное сходство: {similarityScore:P2}");
                            break;

                        case "l2":
                            distance = OllamaService.CalculateL2Distance(userEmbedding, correctEmbeddingArray);
                            similarityScore = 1.0 - distance; // Нормализуем для отображения
                            _logger.LogInformation($"Евклидово расстояние: {distance:F4}, нормализованное сходство: {similarityScore:P2}");
                            break;

                        case "inner_product":
                            similarityScore = OllamaService.CalculateInnerProduct(userEmbedding, correctEmbeddingArray);
                            // Нормализуем скалярное произведение для отображения в процентах
                            double maxPossible = Math.Sqrt(
                                userEmbedding.Select(x => x * x).Sum() *
                                correctEmbeddingArray.Select(x => x * x).Sum()
                            );
                            similarityScore = maxPossible > 0 ? similarityScore / maxPossible : 0.0;
                            _logger.LogInformation($"Скалярное произведение: {similarityScore:P2} (нормализованное)");
                            break;

                        default:
                            similarityScore = OllamaService.CalculateCosineSimilarity(userEmbedding, correctEmbeddingArray);
                            _logger.LogInformation($"Косинусное сходство (по умолчанию): {similarityScore:P2}");
                            break;
                    }

                    SimilarityScore = similarityScore;

                    // Определяем правильность ответа с адаптивными порогами
                    bool isThresholdMet;
                    switch (SelectedDistanceFunction.ToLower())
                    {
                        case "l2":
                            // Для L2: чем меньше расстояние, тем лучше. Используем порог расстояния 0.5
                            isThresholdMet = distance <= 0.5;
                            break;

                        case "inner_product":
                            // Для скалярного произведения используем нормализованный порог
                            isThresholdMet = similarityScore >= 0.7;
                            break;

                        default: // cosine
                            isThresholdMet = similarityScore >= 0.75;
                            break;
                    }

                    IsCorrect = isThresholdMet;

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
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, $"Ошибка при работе с эмбеддингами и моделью {SelectedEmbeddingModel}");
                    // Резервный вариант - традиционная проверка
                    IsCorrect = string.Equals(userAnswer, correctAnswer, StringComparison.OrdinalIgnoreCase);
                    SimilarityScore = IsCorrect ? 1.0 : 0.0;

                    if (IsCorrect)
                    {
                        // Проверяем, был ли этот вопрос уже правильно отвечен
                        var questionKey = $"Question_{CurrentQuestionIndex}_AnsweredCorrectly";
                        var wasAlreadyCorrect = HttpContext.Session.GetInt32(questionKey) == 1;

                        if (!wasAlreadyCorrect)
                        {
                            Score++;
                            HttpContext.Session.SetInt32(questionKey, 1);
                            _logger.LogInformation($"Ответ правильный (традиционная проверка). Счет увеличен до {Score}.");
                        }
                        else
                        {
                            _logger.LogInformation($"Ответ правильный (традиционная проверка), но уже был засчитан ранее. Счет остался {Score}.");
                        }
                    }
                    else
                    {
                        _logger.LogInformation($"Ответ неправильный (традиционная проверка). Счет остался {Score}.");
                    }
                }

                // Сохраняем обновленный счет в сессии
                HttpContext.Session.SetInt32("Score", Score);
                HttpContext.Session.SetString("SelectedEmbeddingModel", SelectedEmbeddingModel);
                HttpContext.Session.SetString("SelectedDistanceFunction", SelectedDistanceFunction);

                // ВСЕГДА сохраняем правильный ответ для отображения
                CorrectAnswer = correctAnswer;

                // Получение темы из сессии
                var selectedTopic = HttpContext.Session.GetString("SelectedTopic") ?? "unknown";
                TopicName = GetTopicName(selectedTopic);
                TotalQuestions = _questionIds.Count;

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
                _logger.LogInformation($"Переход к следующему вопросу. Текущий индекс: {CurrentQuestionIndex}");

                // Восстанавливаем ID вопросов из сессии
                var questionIdsJson = HttpContext.Session.GetString("QuizQuestionIds");
                if (string.IsNullOrEmpty(questionIdsJson))
                {
                    _logger.LogError("ID вопросов не найдены в сессии при переходе к следующему вопросу");
                    TempData["ErrorMessage"] = "Данные викторины утеряны. Пожалуйста, начните заново.";
                    return RedirectToPage("/Index");
                }

                _questionIds = JsonSerializer.Deserialize<List<int>>(questionIdsJson);
                if (_questionIds == null || _questionIds.Count == 0)
                {
                    _logger.LogError("Не удалось десериализовать ID вопросов из сессии или список пуст");
                    TempData["ErrorMessage"] = "Ошибка загрузки вопросов. Пожалуйста, начните заново.";
                    return RedirectToPage("/Index");
                }

                // Восстанавливаем счет из сессии
                var savedScore = HttpContext.Session.GetInt32("Score");
                if (savedScore.HasValue)
                {
                    Score = savedScore.Value;
                }

                // Восстанавливаем тему из сессии
                var selectedTopic = HttpContext.Session.GetString("SelectedTopic") ?? "unknown";

                // Восстанавливаем выбранную модель из сессии
                var savedModel = HttpContext.Session.GetString("SelectedEmbeddingModel");
                if (!string.IsNullOrEmpty(savedModel) && AvailableEmbeddingModels.Any(m => m.Value == savedModel))
                {
                    SelectedEmbeddingModel = savedModel;
                }

                CurrentQuestionIndex++;

                // Проверяем, завершена ли викторина
                if (CurrentQuestionIndex >= _questionIds.Count)
                {
                    IsComplete = true;
                    _logger.LogInformation($"Викторина завершена. Финальный счет: {Score} из {_questionIds.Count}");

                    // Сохраняем финальный счет в сессии
                    HttpContext.Session.SetInt32("Score", Score);

                    return RedirectToPage(new
                    {
                        currentQuestionIndex = CurrentQuestionIndex,
                        score = Score,
                        isComplete = true,
                        showResult = false
                    });
                }

                // Сбрасываем флаг отображения результата
                ShowResult = false;

                // Сохраняем текущее состояние в сессии
                HttpContext.Session.SetString("QuizQuestionIds", JsonSerializer.Serialize(_questionIds));
                HttpContext.Session.SetString("SelectedTopic", selectedTopic);
                HttpContext.Session.SetInt32("Score", Score);
                HttpContext.Session.SetString("SelectedEmbeddingModel", SelectedEmbeddingModel);

                _logger.LogInformation($"Переход к вопросу #{CurrentQuestionIndex + 1} из {_questionIds.Count}. Текущий счет: {Score}");

                // Перенаправляем к следующему вопросу
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
                TempData["ErrorMessage"] = $"Произошла ошибка при переходе к следующему вопросу: {ex.Message}";
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

                // Восстанавливаем ID вопросов из сессии
                var questionIdsJson = HttpContext.Session.GetString("QuizQuestionIds");
                if (string.IsNullOrEmpty(questionIdsJson))
                {
                    _logger.LogError("ID вопросов не найдены в сессии при попытке повтора вопроса");
                    TempData["ErrorMessage"] = "Данные викторины утеряны. Пожалуйста, начните заново.";
                    return RedirectToPage("/Index");
                }

                _questionIds = JsonSerializer.Deserialize<List<int>>(questionIdsJson);
                if (_questionIds == null || _questionIds.Count == 0)
                {
                    _logger.LogError("Не удалось десериализовать ID вопросов из сессии или список пуст при повторе вопроса");
                    TempData["ErrorMessage"] = "Ошибка загрузки вопросов. Пожалуйста, начните заново.";
                    return RedirectToPage("/Index");
                }

                // Проверка текущего индекса вопроса
                if (CurrentQuestionIndex < 0 || CurrentQuestionIndex >= _questionIds.Count)
                {
                    _logger.LogError($"Некорректный индекс вопроса при повторе: {CurrentQuestionIndex}, всего вопросов: {_questionIds.Count}");
                    TempData["ErrorMessage"] = "Некорректный индекс вопроса. Пожалуйста, начните заново.";
                    return RedirectToPage("/Index");
                }

                // Восстанавливаем текущий счет из сессии
                var savedScore = HttpContext.Session.GetInt32("Score");
                if (savedScore.HasValue)
                {
                    Score = savedScore.Value;
                }

                // Восстанавливаем выбранную модель из сессии
                var savedModel = HttpContext.Session.GetString("SelectedEmbeddingModel");
                if (!string.IsNullOrEmpty(savedModel) && AvailableEmbeddingModels.Any(m => m.Value == savedModel))
                {
                    SelectedEmbeddingModel = savedModel;
                }

                // Восстанавливаем тему из сессии
                var selectedTopic = HttpContext.Session.GetString("SelectedTopic") ?? "unknown";

                // Получаем текущий вопрос из базы данных по ID
                var currentQuestionId = _questionIds[CurrentQuestionIndex];
                CurrentQuestion = _context.Questions.FirstOrDefault(q => q.Id == currentQuestionId);

                if (CurrentQuestion == null)
                {
                    _logger.LogError($"Вопрос с ID {currentQuestionId} не найден в базе данных");
                    TempData["ErrorMessage"] = "Вопрос не найден. Пожалуйста, начните заново.";
                    return RedirectToPage("/Index");
                }

                TopicName = GetTopicName(selectedTopic);
                TotalQuestions = _questionIds.Count;

                // Сохраняем обновленное состояние в сессии
                HttpContext.Session.SetInt32("Score", Score);
                HttpContext.Session.SetString("SelectedEmbeddingModel", SelectedEmbeddingModel);

                // ВАЖНО: Делаем перенаправление с правильными параметрами маршрута
                // Это сбрасывает контекст обработчика и гарантирует, что следующий POST вызовет OnPost()
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
                _logger.LogError(ex, $"Ошибка при повторе вопроса: {ex.Message}");
                TempData["ErrorMessage"] = $"Произошла ошибка при повторе вопроса: {ex.Message}";
                return RedirectToPage("/Error");
            }
        }
    }
}