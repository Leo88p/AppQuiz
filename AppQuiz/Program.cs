using AppQuiz.Data;
using AppQuiz.Models;
using AppQuiz.Services;
using Microsoft.AspNetCore.Diagnostics.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

var builder = WebApplication.CreateBuilder(args);

// Настройка строки подключения для Docker
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Server=sqlserver;Database=AppQuizDb;User ID=sa;Password=YourStrong@Passw0rd;TrustServerCertificate=True";
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

// Настройка Identity с минимальными требованиями к паролю
builder.Services.AddDefaultIdentity<IdentityUser>(options =>
{
    // Отключение требований к паролю кроме длины
    options.Password.RequireDigit = false;
    options.Password.RequireLowercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    options.Password.RequiredLength = 4;

    // Разрешить регистрацию без подтверждения email
    options.SignIn.RequireConfirmedAccount = false;
})
.AddEntityFrameworkStores<ApplicationDbContext>();

// Добавление сервисов Razor Pages
builder.Services.AddRazorPages()
    .AddRazorPagesOptions(options =>
    {
        options.Conventions.ConfigureFilter(new IgnoreAntiforgeryTokenAttribute());
    });

// Настройка TempData для сериализации сложных объектов
builder.Services.AddControllersWithViews()
    .AddNewtonsoftJson();

// Добавление поддержки сессий
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
});

// Регистрация сервисов для работы с Ollama
builder.Services.AddHttpClient<OllamaService>();
builder.Services.AddScoped<OllamaService>();

var app = builder.Build();

// Конфигурация middleware
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}
else
{
    // Разрешить детальные ошибки в режиме разработки
    app.UseDeveloperExceptionPage();
    app.UseMigrationsEndPoint();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseSession(); // Добавление middleware для сессий
app.MapRazorPages();

// Инициализация базы данных
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<ApplicationDbContext>();

        // Применить миграции
        context.Database.Migrate();

        // Инициализация вопросов
        DbInitializer.Initialize(context);

        // Инициализация пользователей
        var userManager = services.GetRequiredService<UserManager<IdentityUser>>();
        await SeedUsers(userManager);

        // Загрузка моделей Ollama перед запуском приложения
        //await InitializeOllamaModels(services, builder.Configuration);
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Произошла ошибка при инициализации базы данных.");

        // Для разработки показываем детали ошибки в консоли
        Console.WriteLine($"Ошибка инициализации БД: {ex.Message}");
        Console.WriteLine(ex.StackTrace);
    }
}

app.Run();

// Метод для загрузки моделей Ollama
static async Task InitializeOllamaModels(IServiceProvider services, IConfiguration configuration)
{
    var logger = services.GetRequiredService<ILogger<Program>>();
    var httpClient = services.GetRequiredService<HttpClient>();

    // Получаем URL Ollama из конфигурации
    var ollamaUrl = configuration["OLLAMA_API_URL"] ?? "http://ollama:11434";
    logger.LogInformation($"Используется Ollama URL: {ollamaUrl}");

    // Модели, которые нужно загрузить
    var modelsToPull = new[] { "nomic-embed-text", "all-minilm", "mxbai-embed-large", "phi3" };

    // Увеличиваем таймаут для загрузки больших моделей
    httpClient.Timeout = TimeSpan.FromMinutes(30);

    foreach (var modelName in modelsToPull)
    {
        try
        {
            logger.LogInformation($"Начинаю загрузку модели: {modelName}");
            Console.WriteLine($"Начинаю загрузку модели: {modelName}");

            var pullRequest = new
            {
                name = modelName
            };

            var response = await httpClient.PostAsJsonAsync($"{ollamaUrl}/api/pull", pullRequest);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                logger.LogError($"Ошибка при загрузке модели {modelName}: {response.StatusCode}. Ответ: {errorContent}");
                Console.WriteLine($"Ошибка при загрузке модели {modelName}: {response.StatusCode}");
                Console.WriteLine($"Детали ошибки: {errorContent}");
                continue;
            }

            // Постепенное чтение прогресса загрузки
            using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream);

            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync();
                if (!string.IsNullOrWhiteSpace(line))
                {
                    try
                    {
                        var progress = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(line);

                        if (progress.TryGetValue("status", out var status) &&
                            progress.TryGetValue("completed", out var completed) &&
                            progress.TryGetValue("total", out var total))
                        {
                            var completedBytes = completed.GetInt64();
                            var totalBytes = total.GetInt64();
                            var percent = totalBytes > 0 ? (double)completedBytes / totalBytes * 100 : 0;

                            Console.Write($"\rЗагрузка {modelName}: {percent:F1}% - {status.GetString()}");
                        }
                        else if (progress.TryGetValue("status", out var finalStatus))
                        {
                            Console.WriteLine($"\n{modelName}: {finalStatus.GetString()}");
                        }
                    }
                    catch (JsonException)
                    {
                        // Игнорируем ошибки парсинга, если строка не в формате JSON
                        Console.WriteLine($"\n{line}");
                    }
                }
            }

            logger.LogInformation($"Модель {modelName} успешно загружена");
            Console.WriteLine($"\nМодель {modelName} успешно загружена");

            // Небольшая пауза между загрузками моделей
            await Task.Delay(1000);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"Критическая ошибка при загрузке модели {modelName}: {ex.Message}");
            Console.WriteLine($"Критическая ошибка при загрузке модели {modelName}: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }

    // Проверяем, какие модели доступны после загрузки
    try
    {
        logger.LogInformation("Проверка доступных моделей в Ollama...");
        Console.WriteLine("Проверка доступных моделей в Ollama...");

        var response = await httpClient.GetAsync($"{ollamaUrl}/api/tags");
        if (response.IsSuccessStatusCode)
        {
            var modelsResponse = await response.Content.ReadFromJsonAsync<JsonDocument>();
            var modelsArray = modelsResponse.RootElement.GetProperty("models");

            Console.WriteLine("\nДоступные модели в Ollama:");
            foreach (var model in modelsArray.EnumerateArray())
            {
                var name = model.GetProperty("name").GetString();
                var size = model.GetProperty("size").GetInt64() / (1024 * 1024); // MB
                Console.WriteLine($"- {name} ({size} MB)");
            }
        }
        else
        {
            logger.LogWarning($"Не удалось получить список моделей: {response.StatusCode}");
            Console.WriteLine($"Не удалось получить список моделей: {response.StatusCode}");
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, $"Ошибка при проверке доступных моделей: {ex.Message}");
        Console.WriteLine($"Ошибка при проверке доступных моделей: {ex.Message}");
    }
}

// Метод для создания начальных пользователей
static async Task SeedUsers(UserManager<IdentityUser> userManager)
{
    // Создание администратора, если его нет
    if (await userManager.FindByNameAsync("admin") == null)
    {
        var adminUser = new IdentityUser { UserName = "admin", Email = "admin@example.com" };
        await userManager.CreateAsync(adminUser, "Admin1234");
        Console.WriteLine("Администратор успешно создан.");
    }
}