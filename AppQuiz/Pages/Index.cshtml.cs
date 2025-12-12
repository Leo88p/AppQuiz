using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;

namespace AppQuiz.Pages
{
    public class IndexModel : PageModel
    {
        [BindProperty]
        public string SelectedTopic { get; set; }

        [BindProperty]
        public string QuestionCount { get; set; }

        public void OnGet()
        {
        }

        public IActionResult OnPostStartQuiz()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            // Сохранение выбора пользователя в TempData для передачи на страницу викторины
            TempData["SelectedTopic"] = SelectedTopic;
            TempData["QuestionCount"] = QuestionCount;

            return RedirectToPage("/Quiz");
        }
    }
}