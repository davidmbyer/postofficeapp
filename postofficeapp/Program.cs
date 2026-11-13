using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

const string apiEndpoint = "https://localhost:44390/api/getcheckinfo";
const string updateApiEndpoint = "https://localhost:44390/api/updatecheckstatus";
var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
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
        var lookupRequest = new { bp = bpNumber, check = checkNumber };
        Console.WriteLine($"POST {apiEndpoint}");
        Console.WriteLine(JsonSerializer.Serialize(lookupRequest, jsonOptions));
        using var response = await httpClient.PostAsJsonAsync(apiEndpoint, lookupRequest, jsonOptions);
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
                        if (cashChoice?.Trim().Equals("Y", StringComparison.OrdinalIgnoreCase) == true)
                        {
                            var nisNumber = GetValue(item, "NISNumber");
                            if (string.IsNullOrWhiteSpace(nisNumber))
                            {
                                nisNumber = GetValue(item, "NIS Number");
                            }

                            if (string.IsNullOrWhiteSpace(nisNumber))
                            {
                                Console.Write("NIS Number: ");
                                nisNumber = Console.ReadLine();
                            }

                            if (string.IsNullOrWhiteSpace(nisNumber))
                            {
                                Console.WriteLine("Cannot generate the JSON without an NIS number.");
                            }
                            else if (!TryGetDecimal(item, "Amount Paid", out var chequeAmount))
                            {
                                Console.WriteLine("Cannot generate the JSON: Amount Paid is missing or invalid.");
                            }
                            else
                            {
                                var cashRequest = new
                                {
                                    chequeNumber = GetValue(item, "Cheque Number"),
                                    postOfficeCode = GeneratePostOfficeCode(),
                                    nationalRegistrationNumber = GetValue(item, "National ID"),
                                    chequeAmount,
                                    NISNumber = nisNumber.Trim(),
                                    cashedDate = DateOnly.FromDateTime(DateTime.Today)
                                };

                                Console.WriteLine($"POST {updateApiEndpoint}");
                                Console.WriteLine("Cash request JSON:");
                                Console.WriteLine(JsonSerializer.Serialize(cashRequest, jsonOptions));

                                using var updateResponse = await httpClient.PostAsJsonAsync(updateApiEndpoint, cashRequest, jsonOptions);
                                var updateResponseBody = await updateResponse.Content.ReadAsStringAsync();
                                var responseLabel = updateResponse.IsSuccessStatusCode ? "Update API response" : "Update API request failed";
                                Console.WriteLine($"{responseLabel} ({(int)updateResponse.StatusCode} {updateResponse.ReasonPhrase}):");
                                Console.WriteLine(string.IsNullOrWhiteSpace(updateResponseBody) ? "(empty response)" : updateResponseBody);
                            }
                        }
                        else
                        {
                            Console.WriteLine("Cash selection: No.");
                        }
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

static bool TryGetDecimal(JsonElement item, string propertyName, out decimal result)
{
    result = 0;
    if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(propertyName, out var value))
    {
        return false;
    }

    if (value.ValueKind == JsonValueKind.Number)
    {
        return value.TryGetDecimal(out result);
    }

    return value.ValueKind == JsonValueKind.String
        && decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out result);
}

static string GeneratePostOfficeCode()
{
    var digits = string.Concat(Enumerable.Range(0, 9).Select(_ => RandomNumberGenerator.GetInt32(10)));
    var suffix = (char)('A' + RandomNumberGenerator.GetInt32(26));
    return $"GPS{digits}{suffix}";
}
