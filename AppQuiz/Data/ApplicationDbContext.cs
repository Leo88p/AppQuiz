using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using AppQuiz.Models;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace AppQuiz.Data
{
    public class ApplicationDbContext : IdentityDbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Question> Questions { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            base.OnConfiguring(optionsBuilder);
            // Включаем detailed log для отладки
            optionsBuilder.LogTo(Console.WriteLine, LogLevel.Information);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Правильный порядок регистрации расширений
            modelBuilder.HasPostgresExtension("vector");

            // Настройка entity с векторами
            modelBuilder.Entity<Question>(entity =>
            {
                entity.Property(q => q.NomicEmbedTextEmbedding)
                    .HasColumnType("vector(768)")
                    .IsRequired(false);

                entity.Property(q => q.AllMiniLMEmbedding)
                    .HasColumnType("vector(384)")
                    .IsRequired(false);

                entity.Property(q => q.MxbaiEmbedLargeEmbedding)
                    .HasColumnType("vector(1024)")
                    .IsRequired(false);
            });
        }
    }
}