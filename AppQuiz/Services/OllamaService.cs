using System.Text;
using System.Text.Json;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AppQuiz.Services
{
    public class OllamaService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<OllamaService> _logger;

        public OllamaService(HttpClient httpClient, IConfiguration configuration, ILogger<OllamaService> logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;

            // Устанавливаем базовый адрес для Ollama API
            var ollamaUrl = _configuration["OLLAMA_API_URL"] ?? "http://localhost:11434";
            _httpClient.BaseAddress = new Uri(ollamaUrl);

            _logger.LogInformation($"Ollama сервис настроен на URL: {_httpClient.BaseAddress}");
        }

        // Список доступных эмбеддинг-моделей
        public static readonly List<string> AvailableEmbeddingModels = new List<string>
        {
            "nomic-embed-text",
            "all-minilm",
            "mxbai-embed-large"
        };

        public async Task<bool> IsAnswerCorrectAsync(string userAnswer, string correctAnswer, string embeddingModel, double similarityThreshold = 0.75)
        {
            try
            {
                _logger.LogInformation($"Проверка ответа с использованием модели {embeddingModel}: '{userAnswer}' против '{correctAnswer}'");

                // Получаем эмбеддинги для обоих ответов с указанной моделью
                var userAnswerEmbedding = await GetEmbeddingAsync(userAnswer, embeddingModel);
                var correctAnswerEmbedding = await GetEmbeddingAsync(correctAnswer, embeddingModel);

                if (userAnswerEmbedding == null || correctAnswerEmbedding == null ||
                    userAnswerEmbedding.Length == 0 || correctAnswerEmbedding.Length == 0)
                {
                    _logger.LogWarning($"Получены пустые эмбеддинги при использовании модели {embeddingModel}. Используем резервную проверку.");
                    return string.Equals(userAnswer.Trim(), correctAnswer.Trim(), StringComparison.OrdinalIgnoreCase);
                }

                // Вычисляем косинусное сходство между эмбеддингами
                var similarity = CalculateCosineSimilarity(userAnswerEmbedding, correctAnswerEmbedding);
                _logger.LogInformation($"Сходство ответов с моделью {embeddingModel}: {similarity:P2}");

                // Если сходство выше порога, считаем ответ правильным
                return similarity >= similarityThreshold;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при проверке ответа через Ollama с моделью {embeddingModel}: {ex.Message}");
                // В случае ошибки возвращаем традиционную проверку
                return string.Equals(userAnswer.Trim(), correctAnswer.Trim(), StringComparison.OrdinalIgnoreCase);
            }
        }

        public async Task<float[]> GetEmbeddingAsync(string text, string model = "nomic-embed-text")
        {
            try
            {
                _logger.LogDebug($"Запрос эмбеддинга для текста: '{text.Substring(0, Math.Min(50, text.Length))}...' с использованием модели: {model}");

                // Правильный формат запроса для получения эмбеддингов в Ollama
                var request = new
                {
                    model = model,
                    prompt = text,
                    options = new
                    {
                        embedding_only = true
                    }
                };

                var requestJson = JsonSerializer.Serialize(request);
                _logger.LogDebug($"Отправка запроса к Ollama: {requestJson}");

                var response = await _httpClient.PostAsJsonAsync("/api/embeddings", request);

                _logger.LogDebug($"Статус ответа от Ollama: {response.StatusCode}");

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"Ошибка Ollama API: {response.StatusCode}. Ответ: {errorContent}");
                    throw new Exception($"Ошибка Ollama API: {response.StatusCode}. {errorContent}");
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogDebug($"Ответ от Ollama: {responseContent.Substring(0, Math.Min(200, responseContent.Length))}...");

                var result = JsonSerializer.Deserialize<EmbeddingResponse>(responseContent);

                if (result?.embedding == null || result.embedding.Length == 0)
                {
                    _logger.LogWarning("Получен пустой эмбеддинг от Ollama");
                    return Array.Empty<float>();
                }

                _logger.LogDebug($"Получен эмбеддинг размером: {result.embedding.Length} с использованием модели: {model}");
                return result.embedding;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при получении эмбеддинга с моделью {model}: {ex.Message}");
                return Array.Empty<float>();
            }
        }

        // Обновленный метод для получения сходства с выбором модели
        public async Task<double> GetAnswerSimilarityAsync(string userAnswer, string correctAnswer, string embeddingModel = "nomic-embed-text")
        {
            try
            {
                _logger.LogInformation($"Вычисление сходства между ответами с использованием модели {embeddingModel}: '{userAnswer}' и '{correctAnswer}'");

                var userAnswerEmbedding = await GetEmbeddingAsync(userAnswer, embeddingModel);
                var correctAnswerEmbedding = await GetEmbeddingAsync(correctAnswer, embeddingModel);

                if (userAnswerEmbedding == null || correctAnswerEmbedding == null ||
                    userAnswerEmbedding.Length == 0 || correctAnswerEmbedding.Length == 0)
                {
                    _logger.LogWarning($"Получены пустые эмбеддинги при вычислении сходства с моделью {embeddingModel}");
                    return 0.0;
                }

                var similarity = CalculateCosineSimilarity(userAnswerEmbedding, correctAnswerEmbedding);
                _logger.LogInformation($"Рассчитанное сходство с моделью {embeddingModel}: {similarity:P2}");

                return similarity;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при вычислении сходства ответов с моделью {embeddingModel}: {ex.Message}");
                return 0.0;
            }
        }

        private float CalculateCosineSimilarity(float[] vec1, float[] vec2)
        {
            if (vec1.Length != vec2.Length || vec1.Length == 0)
            {
                _logger.LogWarning($"Несовместимые векторы: vec1.Length={vec1.Length}, vec2.Length={vec2.Length}");
                return 0f;
            }

            float dotProduct = 0f;
            float norm1 = 0f;
            float norm2 = 0f;

            for (int i = 0; i < vec1.Length; i++)
            {
                dotProduct += vec1[i] * vec2[i];
                norm1 += vec1[i] * vec1[i];
                norm2 += vec2[i] * vec2[i];
            }

            if (norm1 == 0 || norm2 == 0)
            {
                _logger.LogWarning($"Нулевая норма: norm1={norm1}, norm2={norm2}");
                return 0f;
            }

            var similarity = dotProduct / (float)(Math.Sqrt(norm1) * Math.Sqrt(norm2));
            _logger.LogDebug($"Рассчитанное сходство: {similarity}");
            return similarity;
        }

        private class EmbeddingResponse
        {
            public float[] embedding { get; set; }
        }
    }
}