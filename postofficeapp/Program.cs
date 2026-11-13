using System.Net.Http.Json;
using System.Text.Json;

const string apiEndpoint = "https://localhost:44390/api/getcheckinfo";
string[] columns = ["BusPartner", "Name", "National ID", "Cheque Number", "Status", "Amount Paid"];
using var httpClient = new HttpClient();

while (true)
{
    Console.Write("BP Number: ");
    var bpNumber = Console.ReadLine();
    if (bpNumber is null)
    {
        break;
    }

    Console.Write("Check Number: ");
    var checkNumber = Console.ReadLine();
    if (checkNumber is null)
    {
        break;
    }

    try
    {
        using var response = await httpClient.PostAsJsonAsync(apiEndpoint, new { bp = bpNumber, check = checkNumber });
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"API request failed ({(int)response.StatusCode} {response.ReasonPhrase}): {errorBody}");
        }
        else
        {
            await using var responseStream = await response.Content.ReadAsStreamAsync();
            using var document = await JsonDocument.ParseAsync(responseStream);
            if (!document.RootElement.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Array)
            {
                Console.WriteLine("The API response did not contain a valid payload array.");
            }
            else if (payload.GetArrayLength() == 0)
            {
                Console.WriteLine("No check information found.");
            }
            else
            {
                foreach (var item in payload.EnumerateArray())
                {
                    foreach (var column in columns)
                    {
                        Console.WriteLine($"{column}: {GetValue(item, column)}");
                    }

                    if (string.Equals(GetValue(item, "Status"), "Open", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.Write("Cash this cheque? (Y/N): ");
                        var cashChoice = Console.ReadLine();
                        Console.WriteLine(cashChoice?.Trim().Equals("Y", StringComparison.OrdinalIgnoreCase) == true
                            ? "Cash selection: Yes."
                            : "Cash selection: No.");
                    }

                    Console.WriteLine();
                }
            }
        }
    }
    catch (HttpRequestException exception)
    {
        Console.WriteLine($"Could not contact the API: {exception.Message}");
    }
    catch (JsonException exception)
    {
        Console.WriteLine($"Could not parse the API response: {exception.Message}");
    }

    Console.WriteLine("Press Enter to do another search (Ctrl+C to exit).");
    if (Console.ReadLine() is null)
    {
        break;
    }

    Console.Clear();
}

static string GetValue(JsonElement item, string propertyName)
{
    if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(propertyName, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
    {
        return string.Empty;
    }

    return value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.ToString();
}
