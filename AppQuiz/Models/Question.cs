namespace AppQuiz.Models
{
    public class Question
    {
        public int Id { get; set; }
        public string Topic { get; set; } // biology, geography, history, music
        public string Text { get; set; }
        public string Answer { get; set; }
    }
}