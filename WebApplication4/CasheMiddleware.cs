using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading.Tasks;



namespace WebApplication4


{
    public class CasheMiddleware
    {
        private readonly RequestDelegate _next;

        // Колекція для зберігання кешованих даних
        private static readonly ConcurrentDictionary<string, (string Response, DateTime Expiration)> _cache
            = new ConcurrentDictionary<string, (string Response, DateTime Expiration)>();

        // Тривалість кешу (5 хвилин)
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

        public CacheMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Перевіряємо, чи метод запиту GET
            if (context.Request.Method == HttpMethods.Get)
            {
                // Генеруємо ключ для кешу
                var cacheKey = GenerateCacheKey(context.Request);

                // Перевіряємо, чи є кешована відповідь і чи не закінчився термін дії
                if (_cache.TryGetValue(cacheKey, out var cachedResponse) && cachedResponse.Expiration > DateTime.UtcNow)
                {
                    // Якщо є актуальний кеш, повертаємо його
                    context.Response.ContentType = "application/json"; // Встановіть відповідний Content-Type
                    await context.Response.WriteAsync(cachedResponse.Response);
                    return;
                }

                // Якщо кешу немає, зберігаємо результат
                var originalBodyStream = context.Response.Body; // Зберігаємо оригінальний потік

                using (var memoryStream = new MemoryStream())
                {
                    context.Response.Body = memoryStream; // Перенаправляємо відповідь у тимчасовий потік

                    // Передаємо запит далі в пайплайн
                    await _next(context);

                    // Зчитуємо відповідь
                    memoryStream.Seek(0, SeekOrigin.Begin);
                    var responseText = await new StreamReader(memoryStream).ReadToEndAsync();
                    memoryStream.Seek(0, SeekOrigin.Begin);

                    // Копіюємо відповідь назад в оригінальний потік
                    await memoryStream.CopyToAsync(originalBodyStream);
                    context.Response.Body = originalBodyStream;

                    // Зберігаємо відповідь у кеш
                    _cache[cacheKey] = (responseText, DateTime.UtcNow.Add(CacheDuration));
                }
            }
            else
            {
                // Для інших методів (POST, PUT, DELETE) передаємо запит далі
                await _next(context);
            }
        }

        // Метод для генерації унікального ключа кешу на основі URL і параметрів запиту
        private string GenerateCacheKey(HttpRequest request)
        {
            var endpoint = request.Path.ToString();
            var query = request.QueryString.ToString();
            return $"{endpoint}{query}";
        }
    }
}
