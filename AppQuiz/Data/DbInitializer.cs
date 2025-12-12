using AppQuiz.Data;
using AppQuiz.Models;
using Microsoft.EntityFrameworkCore;

namespace AppQuiz.Data
{
    public static class DbInitializer
    {
        public static void Initialize(ApplicationDbContext context)
        {
            // Применяем миграции (если еще не применены)
            //context.Database.Migrate();

            // Если вопросы уже есть в базе, ничего не делаем
            if (context.Questions.Any())
            {
                return;
            }

            // Добавляем вопросы
            var questions = GenerateQuestions();
            context.Questions.AddRange(questions);
            context.SaveChanges();
        }

        private static List<Question> GenerateQuestions()
        {
            var questions = new List<Question>();

            // 15 вопросов по биологии
            questions.AddRange(GetBiologyQuestions());

            // 15 вопросов по географии
            questions.AddRange(GetGeographyQuestions());

            // 15 вопросов по истории
            questions.AddRange(GetHistoryQuestions());

            // 15 вопросов по музыкальной литературе
            questions.AddRange(GetMusicQuestions());

            return questions;
        }

        private static List<Question> GetBiologyQuestions()
        {
            return new List<Question>
            {
                new Question { Topic = "biology", Text = "Какой орган отвечает за очистку крови в организме человека?", Answer = "Почки" },
                new Question { Topic = "biology", Text = "Как называется процесс, при котором растения производят пищу с помощью солнечного света?", Answer = "Фотосинтез" },
                new Question { Topic = "biology", Text = "Какой газ выделяют растения в процессе фотосинтеза?", Answer = "Кислород" },
                new Question { Topic = "biology", Text = "Какой орган является самым большим в человеческом теле?", Answer = "Кожа" },
                new Question { Topic = "biology", Text = "Как называется наука о животных?", Answer = "Зоология" },
                new Question { Topic = "biology", Text = "Какой витамин вырабатывается в коже под воздействием солнечного света?", Answer = "Витамин D" },
                new Question { Topic = "biology", Text = "Как называются красные кровяные тельца?", Answer = "Эритроциты" },
                new Question { Topic = "biology", Text = "Как называется процесс деления клетки?", Answer = "Митоз" },
                new Question { Topic = "biology", Text = "Какой отдел мозга отвечает за координацию движений?", Answer = "Мозжечок" },
                new Question { Topic = "biology", Text = "Как называется наука о растениях?", Answer = "Ботаника" },
                new Question { Topic = "biology", Text = "Какой орган вырабатывает инсулин?", Answer = "Поджелудочная железа" },
                new Question { Topic = "biology", Text = "Как называется жидкая часть крови?", Answer = "Плазма" },
                new Question { Topic = "biology", Text = "Какой гормон отвечает за стрессовую реакцию организма?", Answer = "Адреналин" },
                new Question { Topic = "biology", Text = "Какой процесс позволяет растениям поглощать воду из почвы?", Answer = "Осмос" },
                new Question { Topic = "biology", Text = "Как называется способность организма сопротивляться инфекциям?", Answer = "Иммунитет" }
            };
        }

        private static List<Question> GetGeographyQuestions()
        {
            return new List<Question>
            {
                new Question { Topic = "geography", Text = "Какая самая длинная река в мире?", Answer = "Амазонка" },
                new Question { Topic = "geography", Text = "Какой океан самый большой по площади?", Answer = "Тихий океан" },
                new Question { Topic = "geography", Text = "Столица какой страны - город Канберра?", Answer = "Австралия" },
                new Question { Topic = "geography", Text = "Какая самая высокая гора в мире?", Answer = "Джомолунгма" },
                new Question { Topic = "geography", Text = "Какой материк является самым жарким?", Answer = "Африка" },
                new Question { Topic = "geography", Text = "Какое море является самым соленым в мире?", Answer = "Мертвое море" },
                new Question { Topic = "geography", Text = "Какой канал соединяет Средиземное и Красное моря?", Answer = "Суэцкий канал" },
                new Question { Topic = "geography", Text = "В какой стране находится водопад Ниагара?", Answer = "Канада" },
                new Question { Topic = "geography", Text = "Какая самая большая пустыня в мире?", Answer = "Сахара" },
                new Question { Topic = "geography", Text = "Как называется полуостров, на котором расположена Испания и Португалия?", Answer = "Пиренейский полуостров" },
                new Question { Topic = "geography", Text = "Какой остров является самым большим в мире?", Answer = "Гренландия" },
                new Question { Topic = "geography", Text = "В каком городе находится Тадж-Махал?", Answer = "Агра" },
                new Question { Topic = "geography", Text = "Какая страна имеет наибольшее количество озер?", Answer = "Канада" },
                new Question { Topic = "geography", Text = "Как называется точка пересечения экватора и нулевого меридиана?", Answer = "Гвинейский залив" },
                new Question { Topic = "geography", Text = "Какой пролив разделяет Европу и Азию?", Answer = "Босфор" }
            };
        }

        private static List<Question> GetHistoryQuestions()
        {
            return new List<Question>
            {
                new Question { Topic = "history", Text = "В каком году началась Вторая мировая война?", Answer = "1939" },
                new Question { Topic = "history", Text = "Кто был первым президентом США?", Answer = "Джордж Вашингтон" },
                new Question { Topic = "history", Text = "В каком году произошла Французская революция?", Answer = "1789" },
                new Question { Topic = "history", Text = "Как звали русского царя, которого прозвали 'Грозным'?", Answer = "Иван IV" },
                new Question { Topic = "history", Text = "В каком году человек впервые ступил на Луну?", Answer = "1969" },
                new Question { Topic = "history", Text = "Какое сражение считается переломным во Второй мировой войне на территории СССР?", Answer = "Сталинградская битва" },
                new Question { Topic = "history", Text = "Кто написал '95 тезисов', положивших начало Реформации?", Answer = "Мартин Лютер" },
                new Question { Topic = "history", Text = "В каком году распался Советский Союз?", Answer = "1991" },
                new Question { Topic = "history", Text = "Кто был лидером большевиков во время Октябрьской революции?", Answer = "Владимир Ленин" },
                new Question { Topic = "history", Text = "Как назывался торговый путь, соединявший Европу и Азию в средние века?", Answer = "Шелковый путь" },
                new Question { Topic = "history", Text = "В каком году началась Первая мировая война?", Answer = "1914" },
                new Question { Topic = "history", Text = "Кто был последним фараоном Древнего Египта?", Answer = "Клеопатра" },
                new Question { Topic = "history", Text = "Какое событие произошло 9 ноября 1989 года?", Answer = "Падение Берлинской стены" },
                new Question { Topic = "history", Text = "В каком году была принята Декларация независимости США?", Answer = "1776" },
                new Question { Topic = "history", Text = "Кто открыл Америку в 1492 году?", Answer = "Христофор Колумб" }
            };
        }

        private static List<Question> GetMusicQuestions()
        {
            return new List<Question>
            {
                new Question { Topic = "music", Text = "Кто написал оперу 'Свадьба Фигаро'?", Answer = "Вольфганг Амадей Моцарт" },
                new Question { Topic = "music", Text = "Какой инструмент является символом русской музыки?", Answer = "Балалайка" },
                new Question { Topic = "music", Text = "Кто является автором симфонии 'Патетическая'?", Answer = "Пётр Ильич Чайковский" },
                new Question { Topic = "music", Text = "Как называется высшая точка звукоряда?", Answer = "Кульминация" },
                new Question { Topic = "music", Text = "Какой музыкальный инструмент является самым большим в симфоническом оркестре?", Answer = "Контрабас" },
                new Question { Topic = "music", Text = "Кто написал оперу 'Евгений Онегин'?", Answer = "Пётр Ильич Чайковский" },
                new Question { Topic = "music", Text = "Какое произведение Бетховена известно как 'Лунная соната'?", Answer = "Соната №14 для фортепиано" },
                new Question { Topic = "music", Text = "Кто написал цикл 'Времена года'?", Answer = "Антонио Вивальди" },
                new Question { Topic = "music", Text = "Как называется многоголосное пение?", Answer = "Полифония" },
                new Question { Topic = "music", Text = "Кто является основоположником русской классической музыки?", Answer = "Михаил Глинка" },
                new Question { Topic = "music", Text = "Как называется музыкальный символ, обозначающий паузу?", Answer = "Цезура" },
                new Question { Topic = "music", Text = "Кто написал оперу 'Волшебная флейта'?", Answer = "Вольфганг Амадей Моцарт" },
                new Question { Topic = "music", Text = "Как называется инструмент, на котором играл Садко?", Answer = "Гусли" },
                new Question { Topic = "music", Text = "Какой композитор написал оперу 'Кармен'?", Answer = "Жорж Бизе" },
                new Question { Topic = "music", Text = "Как называется музыкальный размер 3/4?", Answer = "Вальс" }
            };
        }
    }
}