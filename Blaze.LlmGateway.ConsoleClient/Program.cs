using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();

using var host = builder.Build();
await host.StartAsync();

var clientFactory = host.Services.GetRequiredService<IHttpClientFactory>();
var client = clientFactory.CreateClient();
client.BaseAddress = new Uri("http://api");

Console.WriteLine("=== LLM Gateway Console Client ===");
Console.WriteLine("Type your message and press Enter to chat. Type 'exit' to quit.");

while (true)
{
    Console.Write("\nUser: ");
    var input = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input) || input.Equals("exit", StringComparison.OrdinalIgnoreCase))
    {
        break;
    }

    var requestBody = new
    {
        messages = new[]
        {
            new { role = "user", content = input }
        }
    };

    var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
    
    using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
    {
        Content = content
    };

    try
    {
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        Console.Write("Assistant: ");
        while (await reader.ReadLineAsync() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            if (line.StartsWith("data: "))
            {
                var data = line.Substring(6).Trim();
                if (data == "[DONE]")
                {
                    Console.WriteLine();
                    break;
                }

                try
                {
                    using var doc = JsonDocument.Parse(data);
                    var text = doc.RootElement
                        .GetProperty("choices")[0]
                        .GetProperty("delta")
                        .GetProperty("content")
                        .GetString();

                    if (!string.IsNullOrEmpty(text))
                    {
                        Console.Write(text);
                    }
                }
                catch (JsonException)
                {
                    // Ignore malformed chunks or [DONE] if not perfectly formatted
                }
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"\nError: {ex.Message}");
    }
}

await host.StopAsync();
