using System.Globalization;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/guide", async (
    double lat, double lng, string lang,
    IHttpClientFactory factory, IConfiguration config) =>
{
    // Wikipedia ke liye client
    var client = factory.CreateClient();
    client.DefaultRequestHeaders.UserAgent.ParseAdd("AiPlaceGuide/1.0 (learning project)");

    string latS = lat.ToString(CultureInfo.InvariantCulture);
    string lngS = lng.ToString(CultureInfo.InvariantCulture);

    // 1) Wikipedia: is jagah ke 10 km ke andar ki 4 cheezein
    var geoUrl = "https://en.wikipedia.org/w/api.php?action=query&list=geosearch" +
                 $"&gscoord={latS}|{lngS}&gsradius=10000&gslimit=4&format=json";
    var geoJson = await client.GetStringAsync(geoUrl);
    using var geoDoc = JsonDocument.Parse(geoJson);
    var found = geoDoc.RootElement.GetProperty("query").GetProperty("geosearch");

    var titles = new List<string>();
    var facts = new List<string>();

    foreach (var item in found.EnumerateArray())
    {
        var title = item.GetProperty("title").GetString()!;
        titles.Add(title);

        // 2) Har title ka chhota summary
        var sumUrl = "https://en.wikipedia.org/api/rest_v1/page/summary/" +
                     Uri.EscapeDataString(title);
        var sumJson = await client.GetStringAsync(sumUrl);
        using var sumDoc = JsonDocument.Parse(sumJson);

        var dist = item.GetProperty("dist").GetDouble();
        facts.Add($"{title} (about {dist:F0} m from the clicked point): {sumDoc.RootElement.GetProperty("extract").GetString()}");
    }

    if (titles.Count == 0)
        return Results.NotFound(new { message = "“I couldn’t find anything about this place on Wikipedia.”" });

    // 3) AI ko sirf yehi facts dekar guide likhwao
    var language = lang == "it" ? "Italian" : "English";
    var prompt = $"Using ONLY the facts below, write a friendly visitor guide of about 100 words in {language}. " +
                 "These are nearby places, not necessarily at the exact spot, so mention roughly how far each is. " +
                 "Do not invent anything.\n\nFacts:\n" + string.Join("\n", facts);

    var apiKey = config["Gemini:ApiKey"];

    // AI ke liye alag client, timeout sirf 30 second
    var aiClient = factory.CreateClient();
    aiClient.Timeout = TimeSpan.FromSeconds(30);
    aiClient.DefaultRequestHeaders.Add("x-goog-api-key", apiKey);

    var body = new { contents = new[] { new { parts = new[] { new { text = prompt } } } } };

    // Pehla model, aur secrets mein diya ho to backup model
    var modelsToTry = new List<string> { config["Gemini:Model"] ?? "gemini-3.5-flash-lite" };
    var fallback = config["Gemini:FallbackModel"];
    if (!string.IsNullOrEmpty(fallback)) modelsToTry.Add(fallback);

    HttpResponseMessage? aiResponse = null;

    foreach (var model in modelsToTry)
    {
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                aiResponse = await aiClient.PostAsJsonAsync(
                    $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent", body);

                if (aiResponse.IsSuccessStatusCode) break;      // kaam ho gaya

                int code = (int)aiResponse.StatusCode;
                if (code != 503 && code != 429) break;          // retry se theek nahi hoga
            }
            catch (TaskCanceledException) { aiResponse = null; }  // timeout
            catch (HttpRequestException) { aiResponse = null; }   // internet ka masla

            await Task.Delay(attempt * 2000);                   // thora ruk kar dobara
        }
        if (aiResponse is { IsSuccessStatusCode: true }) break;
    }

    if (aiResponse is null || !aiResponse.IsSuccessStatusCode)
        return Results.Problem("The AI isn’t responding right now. Please try again after a while.");

    using var aiDoc = JsonDocument.Parse(await aiResponse.Content.ReadAsStringAsync());
    var guide = aiDoc.RootElement.GetProperty("candidates")[0]
                     .GetProperty("content").GetProperty("parts")[0]
                     .GetProperty("text").GetString();

    return Results.Ok(new { places = titles, guide });
});

// Helper: address ke dictionary se pehli maujood value uthao
static string? Pick(JsonElement addr, params string[] keys)
{
    foreach (var k in keys)
        if (addr.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String)
            return v.GetString();
    return null;
}

// Search: "Lahore", "Florence Italy", "Faisal Mosque" waghera
app.MapGet("/api/search", async (string q, IHttpClientFactory factory) =>
{
    if (string.IsNullOrWhiteSpace(q))
        return Results.BadRequest(new { message = "Search text is empty." });

    var client = factory.CreateClient();
    client.DefaultRequestHeaders.UserAgent.ParseAdd("AiPlaceGuide/1.0 (learning project)");

    var url = "https://nominatim.openstreetmap.org/search?format=jsonv2&limit=6&q=" +
              Uri.EscapeDataString(q);
    var json = await client.GetStringAsync(url);
    using var doc = JsonDocument.Parse(json);

    var results = new List<object>();
    foreach (var r in doc.RootElement.EnumerateArray())
    {
        results.Add(new
        {
            name = r.GetProperty("display_name").GetString(),
            lat = double.Parse(r.GetProperty("lat").GetString()!, CultureInfo.InvariantCulture),
            lng = double.Parse(r.GetProperty("lon").GetString()!, CultureInfo.InvariantCulture)
        });
    }
    return Results.Ok(results);
});

// Reverse: click ki hui jagah ka naam, shehar aur mulk
app.MapGet("/api/reverse", async (double lat, double lng, IHttpClientFactory factory) =>
{
    var client = factory.CreateClient();
    client.DefaultRequestHeaders.UserAgent.ParseAdd("AiPlaceGuide/1.0 (learning project)");

    string latS = lat.ToString(CultureInfo.InvariantCulture);
    string lngS = lng.ToString(CultureInfo.InvariantCulture);

    var json = await client.GetStringAsync(
        $"https://nominatim.openstreetmap.org/reverse?format=jsonv2&zoom=16&lat={latS}&lon={lngS}");
    using var doc = JsonDocument.Parse(json);

    // Samundar ya khali jagah par address nahi hota
    if (!doc.RootElement.TryGetProperty("address", out var addr))
        return Results.Ok(new { title = "Unnamed place", city = (string?)null, country = (string?)null });

    var title = Pick(addr, "attraction", "tourism", "building", "road", "neighbourhood",
                     "suburb", "city_district", "city", "town", "village", "county", "state")
                ?? "Unnamed place";
    var city = Pick(addr, "city", "town", "village", "municipality", "county");
    var country = Pick(addr, "country");

    return Results.Ok(new { title, city, country });
});

app.Run();