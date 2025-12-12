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

// Запускаем инициализацию модели Ollama в фоновом режиме
_ = InitializeOllamaModel(app.Services);

app.Run();

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

// Метод для инициализации модели Ollama
// Метод для инициализации модели Ollama
static async Task InitializeOllamaModel(IServiceProvider services)
{
    try
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogInformation("Начинаем инициализацию модели Ollama...");

        // Получаем конфигурацию
        var configuration = services.GetRequiredService<IConfiguration>();
        var ollamaUrl = configuration["OLLAMA_API_URL"] ?? "http://ollama:11434";
        logger.LogInformation($"Попытка подключения к Ollama по адресу: {ollamaUrl}");

        // Создаем отдельный scope для получения scoped-сервисов
        using (var scope = services.CreateScope())
        {
            // Получаем сервисы внутри scope
            var ollamaService = scope.ServiceProvider.GetRequiredService<OllamaService>();

            // Проверяем доступность Ollama
            using var httpClient = new HttpClient();
            var response = await httpClient.GetAsync($"{ollamaUrl}/");
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning($"Ollama недоступен. Код ответа: {response.StatusCode}");
                return;
            }

            logger.LogInformation("Ollama сервер доступен");

            // Проверяем, загружена ли уже модель
            var modelsResponse = await httpClient.GetAsync($"{ollamaUrl}/api/tags");
            if (modelsResponse.IsSuccessStatusCode)
            {
                var modelsContent = await modelsResponse.Content.ReadAsStringAsync();
                var modelsJson = JsonDocument.Parse(modelsContent);

                var hasModel = false;
                if (modelsJson.RootElement.TryGetProperty("models", out var modelsArray) && modelsArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var model in modelsArray.EnumerateArray())
                    {
                        if (model.TryGetProperty("name", out var nameProperty) &&
                            string.Equals(nameProperty.GetString(), "nomic-embed-text", StringComparison.OrdinalIgnoreCase))
                        {
                            hasModel = true;
                            logger.LogInformation("Модель nomic-embed-text уже загружена в Ollama");
                            break;
                        }
                    }
                }

                if (hasModel)
                {
                    // Проверяем работу модели простым запросом
                    try
                    {
                        var testResult = await ollamaService.IsAnswerCorrectAsync("тест", "тест");
                        logger.LogInformation("Модель работает корректно");
                        return;
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Модель загружена, но возникла ошибка при проверке. Требуется повторная загрузка.");
                    }
                }
            }

            // Если модель не загружена, загружаем ее
            logger.LogInformation("Загружаем модель nomic-embed-text в Ollama...");

            var pullRequest = new
            {
                name = "nomic-embed-text"
            };

            var pullResponse = await httpClient.PostAsJsonAsync($"{ollamaUrl}/api/pull", pullRequest);

            if (pullResponse.IsSuccessStatusCode)
            {
                // Читаем потоковый ответ от Ollama
                using var streamReader = new StreamReader(await pullResponse.Content.ReadAsStreamAsync());
                while (!streamReader.EndOfStream)
                {
                    var line = await streamReader.ReadLineAsync();
                    if (!string.IsNullOrEmpty(line))
                    {
                        try
                        {
                            var status = JsonSerializer.Deserialize<Dictionary<string, object>>(line);
                            if (status != null && status.TryGetValue("status", out var statusValue))
                            {
                                logger.LogInformation($"Загрузка модели: {statusValue}");
                            }
                        }
                        catch
                        {
                            // Игнорируем ошибки разбора JSON
                        }
                    }
                }

                logger.LogInformation("Модель nomic-embed-text успешно загружена в Ollama");

                // Проверяем работу модели
                try
                {
                    var testResult = await ollamaService.IsAnswerCorrectAsync("тест", "тест");
                    logger.LogInformation("Модель успешно проверена и готова к работе");
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Ошибка при проверке работы модели после загрузки");
                }
            }
            else
            {
                logger.LogError($"Ошибка загрузки модели. Код ответа: {pullResponse.StatusCode}");
                var errorContent = await pullResponse.Content.ReadAsStringAsync();
                logger.LogError($"Тело ответа: {errorContent}");
            }
        }
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, $"Критическая ошибка при инициализации модели Ollama: {ex.Message}");
    }
}