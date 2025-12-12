using AppQuiz.Data;
using AppQuiz.Models;
using Microsoft.AspNetCore.Diagnostics.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

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
    .AddNewtonsoftJson(); // Необходим для сериализации сложных объектов в TempData

// Добавление поддержки сессий
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
});

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