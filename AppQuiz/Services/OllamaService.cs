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

        public async Task<bool> IsAnswerCorrectAsync(string userAnswer, string correctAnswer, double similarityThreshold = 0.75)
        {
            try
            {
                _logger.LogInformation($"Проверка ответа: '{userAnswer}' против '{correctAnswer}'");

                // Сначала проверяем простое совпадение (для точных совпадений)
                if (string.Equals(userAnswer.Trim(), correctAnswer.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("Точные совпадения ответов обнаружены");
                    return true;
                }

                // Получаем эмбеддинги для обоих ответов
                var userAnswerEmbedding = await GetEmbeddingAsync(userAnswer);
                var correctAnswerEmbedding = await GetEmbeddingAsync(correctAnswer);

                if (userAnswerEmbedding == null || correctAnswerEmbedding == null ||
                    userAnswerEmbedding.Length == 0 || correctAnswerEmbedding.Length == 0)
                {
                    _logger.LogWarning("Получены пустые эмбеддинги. Используем резервную проверку.");
                    return string.Equals(userAnswer.Trim(), correctAnswer.Trim(), StringComparison.OrdinalIgnoreCase);
                }

                // Вычисляем косинусное сходство между эмбеддингами
                var similarity = CalculateCosineSimilarity(userAnswerEmbedding, correctAnswerEmbedding);
                _logger.LogInformation($"Сходство ответов: {similarity:P2}");

                // Если сходство выше порога, считаем ответ правильным
                return similarity >= similarityThreshold;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при проверке ответа через Ollama: {ex.Message}");
                // В случае ошибки возвращаем традиционную проверку
                return string.Equals(userAnswer.Trim(), correctAnswer.Trim(), StringComparison.OrdinalIgnoreCase);
            }
        }

        public async Task<float[]> GetEmbeddingAsync(string text)
        {
            try
            {
                _logger.LogDebug($"Запрос эмбеддинга для текста: '{text.Substring(0, Math.Min(50, text.Length))}...'");

                // Правильный формат запроса для получения эмбеддингов в Ollama
                var request = new
                {
                    model = "nomic-embed-text",
                    prompt = text,
                    options = new
                    {
                        embedding_only = true
                    }
                };

                // Логируем запрос для отладки
                var requestJson = JsonSerializer.Serialize(request);
                _logger.LogDebug($"Отправка запроса к Ollama: {requestJson}");

                var response = await _httpClient.PostAsJsonAsync("/api/embeddings", request);

                // Логируем статус ответа
                _logger.LogDebug($"Статус ответа от Ollama: {response.StatusCode}");

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"Ошибка Ollama API: {response.StatusCode}. Ответ: {errorContent}");
                    throw new Exception($"Ошибка Ollama API: {response.StatusCode}. {errorContent}");
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogDebug($"Ответ от Ollama: {responseContent.Substring(0, Math.Min(200, responseContent.Length))}...");

                // Правильная десериализация ответа
                var result = JsonSerializer.Deserialize<EmbeddingResponse>(responseContent);

                if (result?.embedding == null || result.embedding.Length == 0)
                {
                    _logger.LogWarning("Получен пустой эмбеддинг от Ollama");
                    return Array.Empty<float>();
                }

                _logger.LogDebug($"Получен эмбеддинг размером: {result.embedding.Length}");
                return result.embedding;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Ошибка при получении эмбеддинга: {ex.Message}");
                return Array.Empty<float>();
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