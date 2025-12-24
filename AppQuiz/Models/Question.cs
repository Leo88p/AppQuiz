using Pgvector;

namespace AppQuiz.Models
{
    public class Question
    {
        public int Id { get; set; }
        public string Topic { get; set; } // biology, geography, history, music
        public string Text { get; set; }
        public string Answer { get; set; }

        // Эмбеддинги для разных моделей (используем Pgvector тип)
        public Vector? NomicEmbedTextEmbedding { get; set; }
        public Vector? AllMiniLMEmbedding { get; set; }
        public Vector? MxbaiEmbedLargeEmbedding { get; set; }

        // Метаданные о моделях
        public DateTime? EmbeddingsGeneratedAt { get; set; }
        public string? GeneratedEmbeddingModels { get; set; }
    }
}